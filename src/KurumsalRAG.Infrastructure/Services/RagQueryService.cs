using System.Runtime.CompilerServices;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Query use-case: soru → guard → embed → retrieve → rerank → prompt → generate.
/// Tüm bağımlılıklar port; hiçbir somut sağlayıcıya bağlı değil.
/// </summary>
public sealed class RagQueryService : IRagQueryService
{
    private readonly IPromptGuard _promptGuard;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly ILlmProvider _llm;
    private readonly IFaithfulnessEvaluator _faithfulness;
    private readonly RagOptions _options;
    private readonly ILogger<RagQueryService> _logger;

    public RagQueryService(
        IPromptGuard promptGuard,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IReranker reranker,
        ILlmProvider llm,
        IFaithfulnessEvaluator faithfulness,
        IOptions<RagOptions> options,
        ILogger<RagQueryService> logger)
    {
        _promptGuard = promptGuard;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _reranker = reranker;
        _llm = llm;
        _faithfulness = faithfulness;
        _options = options.Value;
        _logger = logger;
    }

    private const string BlockedText =
        "Bu istek güvenlik nedeniyle işlenemedi (olası prompt injection tespit edildi).";

    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var guard = _promptGuard.Inspect(question);
        if (guard.ShouldBlock)
        {
            _logger.LogWarning("İstek bloklandı. Kural: {Rule}", guard.MatchedRule);
            var blockedObs = new RagObservability(0, 0, 0, 0, PromptGuardTriggered: true);
            return new RagAnswer(BlockedText, [], blockedObs);
        }

        var (context, sources, guarded) = await RetrieveContextAsync(guard, cancellationToken);
        var messages = RagPromptBuilder.Build(guard.SanitizedInput, context);

        var completion = await _llm.CompleteAsync(messages, cancellationToken);

        double? faithfulnessScore = null;
        if (_options.Faithfulness.Enabled && !context.IsEmpty)
        {
            var eval = await _faithfulness.EvaluateAsync(completion.Content, context, cancellationToken);
            faithfulnessScore = eval.Score;
            if (!eval.Passed)
            {
                _logger.LogWarning(
                    "Düşük groundedness skoru {Score:F2}. Desteklenmeyen iddialar: {Claims}",
                    eval.Score, string.Join(" | ", eval.UnsupportedClaims));
            }
        }

        var observability = new RagObservability(
            RetrievedCount: sources.Count == 0 ? 0 : _options.Retrieval.TopK,
            RerankedCount: sources.Count,
            PromptTokens: completion.Usage.PromptTokens,
            CompletionTokens: completion.Usage.CompletionTokens,
            FaithfulnessScore: faithfulnessScore,
            PromptGuardTriggered: guarded);

        _logger.LogInformation(
            "Cevap üretildi. Tokenlar: prompt={Prompt}, completion={Completion}, faithfulness={Faith}",
            observability.PromptTokens, observability.CompletionTokens, faithfulnessScore);

        return new RagAnswer(completion.Content, sources, observability);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var guard = _promptGuard.Inspect(question);
        if (guard.ShouldBlock)
        {
            _logger.LogWarning("Stream isteği bloklandı. Kural: {Rule}", guard.MatchedRule);
            yield return BlockedText;
            yield break;
        }

        var (context, _, _) = await RetrieveContextAsync(guard, cancellationToken);
        var messages = RagPromptBuilder.Build(guard.SanitizedInput, context);

        await foreach (var token in _llm.StreamAsync(messages, cancellationToken))
            yield return token;
    }

    /// <summary>embed → retrieve → rerank → context kurma; guard önceden çalıştırılmış olarak gelir.</summary>
    private async Task<(RetrievedContext Context, IReadOnlyList<CitedSource> Sources, bool Guarded)>
        RetrieveContextAsync(PromptGuardResult guard, CancellationToken cancellationToken)
    {
        if (guard.IsSuspicious)
        {
            _logger.LogWarning(
                "Prompt guard tetiklendi (sanitize modu): {Reasons}", string.Join(", ", guard.Reasons));
        }
        var safeQuestion = guard.SanitizedInput;

        var queryEmbedding = await _embeddings.EmbedAsync(safeQuestion, cancellationToken);
        var candidates = await _vectorStore.SearchAsync(queryEmbedding, _options.Retrieval.TopK, cancellationToken);
        var ranked = await _reranker.RerankAsync(safeQuestion, candidates, _options.Retrieval.TopN, cancellationToken);

        var retrievedChunks = new List<RetrievedChunk>(ranked.Count);
        var sources = new List<CitedSource>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var reference = i + 1; // [chunk:1]-tabanlı
            var scored = ranked[i];
            retrievedChunks.Add(new RetrievedChunk(reference, scored.Chunk.Id, scored.Chunk.Content, scored.Score));
            sources.Add(new CitedSource(reference, scored.Chunk.Id, scored.Score));
        }

        return (new RetrievedContext(retrievedChunks), sources, guard.IsSuspicious);
    }
}
