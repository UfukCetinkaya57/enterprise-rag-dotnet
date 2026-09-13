using System.Runtime.CompilerServices;
using System.Text;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Application.Retrieval;
using KurumsalRAG.Application.Sessions;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Query use-case: soru → kill-switch/bütçe/cache → guard → embed → session-filtreli retrieve →
/// rerank → prompt → generate → faithfulness. Tüm bağımlılıklar port; somut sağlayıcıya bağlı değil.
/// </summary>
public sealed class RagQueryService : IRagQueryService
{
    private readonly IPromptGuard _promptGuard;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly ILlmProvider _llm;
    private readonly IFaithfulnessEvaluator _faithfulness;
    private readonly IResponseCache _cache;
    private readonly ITokenBudgetGuard _budget;
    private readonly ISessionAccessor _session;
    private readonly IConversationStore _conversation;
    private readonly IQueryRewriter _queryRewriter;
    private readonly IUserLlmResolver _userLlm;
    private readonly RagOptions _options;
    private readonly DemoOptions _demo;
    private readonly ILogger<RagQueryService> _logger;

    public RagQueryService(
        IPromptGuard promptGuard,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IReranker reranker,
        ILlmProvider llm,
        IFaithfulnessEvaluator faithfulness,
        IResponseCache cache,
        ITokenBudgetGuard budget,
        ISessionAccessor session,
        IConversationStore conversation,
        IQueryRewriter queryRewriter,
        IUserLlmResolver userLlm,
        IOptions<RagOptions> options,
        IOptions<DemoOptions> demo,
        ILogger<RagQueryService> logger)
    {
        _promptGuard = promptGuard;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _reranker = reranker;
        _llm = llm;
        _faithfulness = faithfulness;
        _cache = cache;
        _budget = budget;
        _session = session;
        _conversation = conversation;
        _queryRewriter = queryRewriter;
        _userLlm = userLlm;
        _options = options.Value;
        _demo = demo.Value;
        _logger = logger;
    }

    private const string BlockedText =
        "Bu istek güvenlik nedeniyle işlenemedi (olası prompt injection tespit edildi).";

    private const string LimitedText =
        "Günlük demo limiti doldu veya demo geçici olarak kapalı. Lütfen yarın tekrar deneyin.";

    private const string LowGroundednessWarning =
        "\n\n⚠️ Not: Bu cevabın bir kısmı sağlanan dokümanlarca tam olarak desteklenmiyor olabilir.";

    // ---------------------------------------------------------------- Non-stream

    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        // Kill switch / bütçe: LLM'e gitmeden nazik "limited" cevabı.
        if (await IsLimitedAsync(cancellationToken))
            return LimitedAnswer();

        var guard = _promptGuard.Inspect(question);
        if (guard.ShouldBlock)
        {
            _logger.LogWarning("İstek bloklandı. Kural: {Rule}", guard.MatchedRule);
            return new RagAnswer(BlockedText, [], Blocked(), AnswerType.Normal);
        }

        // BYOK cevap-üreten LLM'i EN BAŞTA çöz: kullanıcı desteklenmeyen bir sağlayıcı gönderdiyse
        // (InvalidApiKeyException) embedding'e para harcamadan burada reddedilsin. Yoksa havuz LLM'i.
        var activeLlm = _userLlm.Resolve() ?? _llm;

        // Konuşma geçmişi (multi-turn açıksa). Takip sorularında retrieval + prompt bağlamı buradan.
        var history = await LoadHistoryAsync(cancellationToken);

        // Cache: (session + normalize soru). Hit'te LLM'e gidilmez. Multi-turn AÇIKKEN cache
        // devre dışı — aynı soru farklı bağlamda farklı cevap verebilir (takip cevabı önceki tura bağlı).
        if (!_options.Conversation.Enabled)
        {
            var cached = await _cache.GetAsync(_session.SessionId, guard.SanitizedInput, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Cache hit (session={Session}). Toplam hit={Hits}",
                    _session.SessionId, _cache.HitCount);
                return new RagAnswer(
                    cached.Answer, cached.Sources,
                    new RagObservability(0, cached.Sources.Count, 0, 0, cached.FaithfulnessScore, guard.IsSuspicious),
                    AnswerType.Cached);
            }
        }

        // Observability: retrieval ve generation sürelerini ölç (panelde gösterilir).
        var retrievalSw = System.Diagnostics.Stopwatch.StartNew();
        var (context, sources, embeddingTokens, safeQuestion) =
            await RetrieveContextAsync(guard, history, cancellationToken);
        retrievalSw.Stop();
        var messages = RagPromptBuilder.Build(guard.SanitizedInput, context, history);

        var generationSw = System.Diagnostics.Stopwatch.StartNew();
        LlmCompletion completion;
        try
        {
            completion = await activeLlm.CompleteAsync(messages, cancellationToken);
        }
        catch (Exception ex)
        {
            // Sağlayıcı hatası (ör. Gemini 429) — 500 yerine nazik "limited" cevabı.
            _logger.LogWarning(ex, "LLM çağrısı başarısız; nazik limited cevabı dönülüyor.");
            return new RagAnswer(
                "Şu anda yoğunluk var (ücretsiz kota sınırı). Lütfen birazdan tekrar deneyin.",
                [], new RagObservability(0, 0, 0, 0), AnswerType.Limited);
        }

        generationSw.Stop();

        // answerText: kaydedilecek/cache'lenecek TEMİZ cevap (uyarı banner'ı buraya EKLENMEZ).
        var answerText = completion.Content;
        var extraTokens = 0;
        var lowGroundedness = false;
        var reflectionApplied = false;

        double? faithfulnessScore = null;
        if (_options.Faithfulness.Enabled && !context.IsEmpty && !RagPromptBuilder.IsRefusal(answerText))
        {
            var eval = await _faithfulness.EvaluateAsync(answerText, context, cancellationToken);
            faithfulnessScore = eval.Score;
            if (!eval.Passed)
            {
                _logger.LogWarning("Düşük groundedness {Score:F2}: {Claims}",
                    eval.Score, string.Join(" | ", eval.UnsupportedClaims));

                // Self-correction (reflection): desteklenmeyen iddialarla yeniden retrieve edip
                // strict modda yeniden cevapla. Daha iyi/eşit skor gelirse onu al (orchestrator ile
                // tutarlı >=). safeQuestion + history thread'lenir ki multi-turn'de retrieval bozulmasın.
                if (_options.Reflection.Enabled)
                {
                    reflectionApplied = true;
                    var (correctedAnswer, correctedEval, correctedSources, reflectTokens) =
                        await SelfCorrectAsync(safeQuestion, history, answerText, eval, activeLlm, cancellationToken);
                    extraTokens += reflectTokens;
                    if (correctedEval.Score >= eval.Score)
                    {
                        answerText = correctedAnswer;
                        faithfulnessScore = correctedEval.Score;
                        sources = correctedSources;
                        eval = correctedEval;
                    }
                }

                // Hâlâ eşik altındaysa: banner'ı yalnızca KULLANICIYA gösterilen cevaba ekle,
                // kaydedilen/cache'lenen answerText'i KİRLETME (sonraki turları bozmasın).
                lowGroundedness = !eval.Passed;
            }
        }

        var totalTokens = completion.Usage.TotalTokens + embeddingTokens + extraTokens;
        await _budget.RecordUsageAsync(totalTokens, cancellationToken);

        var observability = new RagObservability(
            RetrievedCount: sources.Count == 0 ? 0 : _options.Retrieval.TopK,
            RerankedCount: sources.Count,
            PromptTokens: completion.Usage.PromptTokens,
            CompletionTokens: completion.Usage.CompletionTokens,
            FaithfulnessScore: faithfulnessScore,
            PromptGuardTriggered: guard.IsSuspicious,
            RetrievalMs: (int)retrievalSw.ElapsedMilliseconds,
            GenerationMs: (int)generationSw.ElapsedMilliseconds,
            ReflectionApplied: reflectionApplied);

        // Cache'le: yalnızca multi-turn KAPALIYKEN. Açıkken cache okuması hep atlanır
        // (ilk turdan sonra geçmiş dolar), o yüzden yazmak ölü kayıt olur.
        // Cache ve geçmişe TEMİZ cevap yazılır (banner'sız) — sonraki turlar/cache kirlenmesin.
        if (!_options.Conversation.Enabled)
            await _cache.SetAsync(_session.SessionId, guard.SanitizedInput,
                new CachedAnswer(answerText, sources, faithfulnessScore), cancellationToken);

        // Turu geçmişe kaydet (multi-turn açıksa) — sonraki takip sorusu bunu görecek.
        await PersistTurnAsync(guard.SanitizedInput, answerText, cancellationToken);

        _logger.LogInformation("Cevap üretildi. tokens={Tokens} faithfulness={Faith}",
            totalTokens, faithfulnessScore);

        // Uyarı banner'ı yalnızca kullanıcıya dönen yanıta eklenir (kalıcı metne değil).
        var displayAnswer = lowGroundedness ? answerText + LowGroundednessWarning : answerText;
        return new RagAnswer(displayAnswer, sources, observability, AnswerType.Normal);
    }

    // ---------------------------------------------------------------- Stream

    public async IAsyncEnumerable<RagStreamChunk> StreamAsync(
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await IsLimitedAsync(cancellationToken))
        {
            yield return RagStreamChunk.Status(AnswerType.Limited);
            yield return RagStreamChunk.TokenChunk(LimitedText);
            yield return RagStreamChunk.FinalChunk(LimitedAnswer());
            yield break;
        }

        var guard = _promptGuard.Inspect(question);
        if (guard.ShouldBlock)
        {
            yield return RagStreamChunk.Status(AnswerType.Normal);
            yield return RagStreamChunk.TokenChunk(BlockedText);
            yield return RagStreamChunk.FinalChunk(new RagAnswer(BlockedText, [], Blocked()));
            yield break;
        }

        // BYOK LLM'ini EN BAŞTA çöz. Desteklenmeyen sağlayıcı → embedding'e para harcamadan nazik hata.
        // yield catch içinde olamaz → hatayı yakala, mesajı dışarıda yield et.
        ILlmProvider? activeLlm = null;
        string? resolveError = null;
        try
        {
            activeLlm = _userLlm.Resolve() ?? _llm;
        }
        catch (InvalidApiKeyException ex)
        {
            resolveError = ex.Message;
        }

        if (resolveError is not null)
        {
            yield return RagStreamChunk.Status(AnswerType.Normal);
            yield return RagStreamChunk.TokenChunk(resolveError);
            yield return RagStreamChunk.FinalChunk(new RagAnswer(resolveError, [], Blocked()));
            yield break;
        }

        var history = await LoadHistoryAsync(cancellationToken);

        // Multi-turn açıkken cache devre dışı (takip cevabı önceki tura bağlı).
        if (!_options.Conversation.Enabled)
        {
            var cached = await _cache.GetAsync(_session.SessionId, guard.SanitizedInput, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Cache hit (stream, session={Session}).", _session.SessionId);
                yield return RagStreamChunk.Status(AnswerType.Cached);
                yield return RagStreamChunk.TokenChunk(cached.Answer);
                yield return RagStreamChunk.FinalChunk(new RagAnswer(
                    cached.Answer, cached.Sources,
                    new RagObservability(0, cached.Sources.Count, 0, 0, cached.FaithfulnessScore),
                    AnswerType.Cached));
                yield break;
            }
        }

        // Stream'de self-correction yok (token akarken yeniden üretim uygun değil) → SafeQuestion discard.
        var (context, sources, embeddingTokens, _) = await RetrieveContextAsync(guard, history, cancellationToken);
        var messages = RagPromptBuilder.Build(guard.SanitizedInput, context, history);

        yield return RagStreamChunk.Status(AnswerType.Normal);

        // Token'ları akıtırken tam cevabı biriktir (sonra faithfulness + cache için).
        // LLM stream'i hata verirse (ör. Gemini 429) manuel enumerate ile YAKALA:
        // yield'li metotta try/catch mümkün değil, bu yüzden enumerator elle sürülür.
        var full = new StringBuilder();
        // resolveError null olduğu için activeLlm burada kesinlikle dolu.
        var enumerator = activeLlm!.StreamAsync(messages, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                string token;
                var failed = false;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    token = enumerator.Current;
                }
                catch (Exception ex)
                {
                    // yield catch içinde olamaz — hatayı işaretle, catch dışında ele al.
                    _logger.LogWarning(ex, "LLM stream hatası (ör. 429); nazik mesaj gönderilecek.");
                    failed = true;
                    token = string.Empty;
                }

                if (failed)
                {
                    var msg = full.Length == 0
                        ? "Şu anda yoğunluk var (ücretsiz kota sınırı). Lütfen birazdan tekrar deneyin."
                        : "\n\n(Yanıtın kalanı alınamadı — ücretsiz kota sınırı. Lütfen birazdan tekrar deneyin.)";
                    yield return RagStreamChunk.TokenChunk(msg);
                    yield return RagStreamChunk.FinalChunk(new RagAnswer(
                        full + msg, sources,
                        new RagObservability(_options.Retrieval.TopK, sources.Count, 0, 0),
                        AnswerType.Limited));
                    yield break;
                }

                full.Append(token);
                yield return RagStreamChunk.TokenChunk(token);
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        var answer = full.ToString();

        double? faithfulnessScore = null;
        if (_options.Faithfulness.Enabled && !context.IsEmpty)
        {
            var eval = await _faithfulness.EvaluateAsync(answer, context, cancellationToken);
            faithfulnessScore = eval.Score;
        }

        // Stream'de gerçek usage yok; metin uzunluğundan kestir ve bütçeye yaz.
        var estTokens = TokenEstimator.Estimate(context.ToPromptBlock() + answer) + embeddingTokens;
        await _budget.RecordUsageAsync(estTokens, cancellationToken);

        if (!_options.Conversation.Enabled)
            await _cache.SetAsync(_session.SessionId, guard.SanitizedInput,
                new CachedAnswer(answer, sources, faithfulnessScore), cancellationToken);

        await PersistTurnAsync(guard.SanitizedInput, answer, cancellationToken);

        var observability = new RagObservability(
            _options.Retrieval.TopK, sources.Count, 0, 0, faithfulnessScore, guard.IsSuspicious);
        yield return RagStreamChunk.FinalChunk(new RagAnswer(answer, sources, observability, AnswerType.Normal));
    }

    // ---------------------------------------------------------------- Ortak

    /// <summary>Kill switch kapalı mı ya da günlük bütçe doldu mu?</summary>
    private async Task<bool> IsLimitedAsync(CancellationToken cancellationToken)
    {
        if (!_demo.Enabled)
            return true;

        var status = await _budget.CheckAsync(cancellationToken);
        if (!status.WithinBudget)
            _logger.LogWarning("Günlük token bütçesi doldu: {Used}/{Budget}", status.UsedToday, status.DailyBudget);
        return !status.WithinBudget;
    }

    private static RagAnswer LimitedAnswer()
        => new(LimitedText, [], new RagObservability(0, 0, 0, 0), AnswerType.Limited);

    private static RagObservability Blocked() => new(0, 0, 0, 0, PromptGuardTriggered: true);

    /// <summary>
    /// Multi-turn açıksa geçmişi çeker. Kapalıysa/boşsa boş liste (LLM/DB maliyeti yok).
    /// </summary>
    private async Task<IReadOnlyList<ConversationTurn>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        if (!_options.Conversation.Enabled)
            return [];
        return await _conversation.GetRecentAsync(
            _session.SessionId, _options.Conversation.HistoryWindow, cancellationToken);
    }

    /// <summary>
    /// Başarılı bir turu geçmişe kaydeder (multi-turn açıksa). Boş veya "bilgi yok" (refusal)
    /// cevapları KAYDETMEZ — sonraki query-rewrite/prompt'u zehirlememesi için. Kayıt hatası
    /// (ör. eşzamanlı çift-gönderim → PK çakışması) cevabı bozmasın diye YUTULUR (sadece loglanır).
    /// </summary>
    private async Task PersistTurnAsync(string question, string answer, CancellationToken cancellationToken)
    {
        if (!_options.Conversation.Enabled)
            return;

        // Boş cevap ya da refusal → geçmişe değmez (bağlamı bozar, HistoryWindow'u boşa doldurur).
        if (string.IsNullOrWhiteSpace(answer) ||
            answer.Contains(RagPromptBuilder.RefusalText, StringComparison.Ordinal))
            return;

        try
        {
            await _conversation.AppendAsync(_session.SessionId, question, answer, cancellationToken);
        }
        catch (Exception ex)
        {
            // Cevap zaten üretildi; geçmişe yazamamak isteği çökertmemeli.
            _logger.LogWarning(ex, "Konuşma turu kaydedilemedi (yok sayılıyor).");
        }
    }

    /// <summary>
    /// embed → session-filtreli retrieve → rerank → context. Embedding, GEÇMİŞLE yeniden yazılmış
    /// (bağımsız) soruyla yapılır ki takip soruları doğru chunk'ı bulsun. Embedding token tahminini de döndürür.
    /// </summary>
    private async Task<(RetrievedContext Context, IReadOnlyList<CitedSource> Sources, int EmbeddingTokens, string SafeQuestion)>
        RetrieveContextAsync(
            PromptGuardResult guard,
            IReadOnlyList<ConversationTurn> history,
            CancellationToken cancellationToken)
    {
        if (guard.IsSuspicious)
            _logger.LogWarning("Prompt guard (sanitize): {Reasons}", string.Join(", ", guard.Reasons));

        // Takip sorusunu geçmişle bağımsız tam soruya çevir (geçmiş boşsa aynen döner, LLM çağrısı yok).
        var safeQuestion = await _queryRewriter.RewriteAsync(guard.SanitizedInput, history, cancellationToken);
        var embeddingTokens = TokenEstimator.Estimate(safeQuestion);

        // Rewrite gerçekten LLM çağrısı yaptıysa (geçmiş vardı) o çağrının token maliyetini de
        // bütçeye yansıt — girdi (geçmiş + soru) + çıktı (yeni soru). Aksi halde gerçek harcama az görünür.
        if (history.Count > 0)
        {
            var rewriteCost = TokenEstimator.Estimate(
                string.Concat(history.Select(t => t.Question + t.Answer)) + guard.SanitizedInput + safeQuestion);
            embeddingTokens += rewriteCost;
        }

        var allowed = AllowedSessions();
        var queryEmbedding = await _embeddings.EmbedAsync(safeQuestion, cancellationToken);
        var vectorHits = await _vectorStore.SearchAsync(
            queryEmbedding, _options.Retrieval.TopK, allowed, cancellationToken);

        // Hybrid: vektör (anlam) + keyword (full-text) → RRF ile birleştir. Değilse yalnız vektör.
        IReadOnlyList<ScoredChunk> candidates;
        if (_options.Retrieval.Hybrid)
        {
            var keywordHits = await _vectorStore.SearchKeywordAsync(
                safeQuestion, _options.Retrieval.TopK, allowed, cancellationToken);
            candidates = ReciprocalRankFusion.Fuse([vectorHits, keywordHits], _options.Retrieval.TopK);
            _logger.LogInformation("Hybrid retrieval: vektör={V} keyword={K} → füzyon={F}",
                vectorHits.Count, keywordHits.Count, candidates.Count);
        }
        else
        {
            candidates = vectorHits;
        }

        var ranked = await _reranker.RerankAsync(safeQuestion, candidates, _options.Retrieval.TopN, cancellationToken);

        // Parent-document dedup + context kurulumu ortak yardımcıda (self-correction ile paylaşılır).
        var (context, sources) = BuildContext(ranked);
        return (context, sources, embeddingTokens, safeQuestion);
    }

    /// <summary>
    /// Self-correction (retrieval-augmented reflection): ilk cevabın desteklenmeyen iddialarını
    /// sorguya ekleyip YENİDEN retrieve eder (farklı/daha fazla chunk), sonra STRICT modda yeniden
    /// cevaplar ve yeniden ölçer. Çağıran, skoru arttıysa bu sonucu benimser. Ekstra token maliyetini
    /// döndürür. safeQuestion null geçilebilir — o zaman guard.SanitizedInput kullanılır.
    /// </summary>
    private async Task<(string Answer, FaithfulnessResult Eval, IReadOnlyList<CitedSource> Sources, int Tokens)>
        SelfCorrectAsync(
            string safeQuestion, IReadOnlyList<ConversationTurn> history, string previousAnswer,
            FaithfulnessResult previousEval, ILlmProvider activeLlm, CancellationToken cancellationToken)
    {
        // safeQuestion: geçmişle yeniden yazılmış (bağımsız) soru → multi-turn'de doğru retrieval.
        // Desteklenmeyen iddiaları sorguya ekle → retrieval o eksik bilgiyi hedeflesin.
        var augmentedQuery = previousEval.UnsupportedClaims.Count > 0
            ? safeQuestion + " " + string.Join(" ", previousEval.UnsupportedClaims)
            : safeQuestion;

        var allowed = AllowedSessions();
        var queryEmbedding = await _embeddings.EmbedAsync(augmentedQuery, cancellationToken);
        var vectorHits = await _vectorStore.SearchAsync(
            queryEmbedding, _options.Retrieval.TopK, allowed, cancellationToken);

        IReadOnlyList<ScoredChunk> candidates = vectorHits;
        if (_options.Retrieval.Hybrid)
        {
            var keywordHits = await _vectorStore.SearchKeywordAsync(
                augmentedQuery, _options.Retrieval.TopK, allowed, cancellationToken);
            candidates = ReciprocalRankFusion.Fuse([vectorHits, keywordHits], _options.Retrieval.TopK);
        }

        var ranked = await _reranker.RerankAsync(augmentedQuery, candidates, _options.Retrieval.TopN, cancellationToken);
        var (context, sources) = BuildContext(ranked);

        var tokens = TokenEstimator.Estimate(augmentedQuery);

        // Strict modda (context'e katı sadakat) yeniden cevapla — geçmiş korunur (multi-turn).
        var messages = RagPromptBuilder.BuildStrict(safeQuestion, context, history);
        LlmCompletion retryCompletion;
        try
        {
            retryCompletion = await activeLlm.CompleteAsync(messages, cancellationToken);
        }
        catch (Exception ex)
        {
            // Reflection çağrısı başarısızsa (ör. 429) ilk cevaba dokunma.
            _logger.LogWarning(ex, "Self-correction çağrısı başarısız; ilk cevap korunuyor.");
            return (previousAnswer, previousEval, sources, tokens);
        }

        tokens += retryCompletion.Usage.TotalTokens;
        var retriedEval = await _faithfulness.EvaluateAsync(retryCompletion.Content, context, cancellationToken);
        _logger.LogInformation("Self-correction: {Before:F2} → {After:F2}",
            previousEval.Score, retriedEval.Score);

        return (retryCompletion.Content, retriedEval, sources, tokens);
    }

    /// <summary>Ranked chunk'lardan RetrievedContext + kaynak listesi kurar (parent-document dedup dahil).</summary>
    private static (RetrievedContext Context, IReadOnlyList<CitedSource> Sources) BuildContext(
        IReadOnlyList<ScoredChunk> ranked)
    {
        var retrievedChunks = new List<RetrievedChunk>(ranked.Count);
        var sources = new List<CitedSource>(ranked.Count);
        var seenParents = new HashSet<string>(StringComparer.Ordinal);
        var reference = 0;
        foreach (var scored in ranked)
        {
            var parent = scored.Chunk.ParentContent;
            if (!string.IsNullOrEmpty(parent) && !seenParents.Add($"{scored.Chunk.DocumentId}|{parent}"))
                continue;

            reference++;
            var contextText = string.IsNullOrEmpty(parent) ? scored.Chunk.Content : parent;
            retrievedChunks.Add(new RetrievedChunk(reference, scored.Chunk.Id, contextText, scored.Score));
            // Kaynağa CHILD metnini koy: skor child'a ait, gösterilen metin de child olsun (tutarlı).
            // Parent bağlam LLM'e gider (retrievedChunks) ama kullanıcıya gösterilen atıf birimi child'dır.
            sources.Add(new CitedSource(reference, scored.Chunk.Id, scored.Score, scored.Chunk.Content));
        }
        return (new RetrievedContext(retrievedChunks), sources);
    }

    /// <summary>Retrieval'ın görebileceği session'lar: kullanıcının kendi + seed (örnek doküman).</summary>
    private string[] AllowedSessions() => SessionScope.Allowed(_session.SessionId, _demo.SeedSessionId);
}
