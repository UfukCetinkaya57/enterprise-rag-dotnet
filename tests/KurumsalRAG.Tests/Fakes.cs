using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Tests;

// RagQueryService'i izole test etmek için minimal sahte (fake) port implementasyonları.

internal sealed class FakePromptGuard : IPromptGuard
{
    public PromptGuardResult Inspect(string userInput) => PromptGuardResult.Clean(userInput);
}

internal sealed class FakeEmbedding : IEmbeddingProvider
{
    public int Dimensions => 3;
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());
}

internal sealed class FakeVectorStore : IVectorStore
{
    public IReadOnlyCollection<string>? LastAllowedSessions { get; private set; }

    public Task SaveDocumentAsync(DocumentEntity d, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertChunksAsync(IReadOnlyList<DocumentChunk> c, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<int> PurgeExpiredAsync(DateTimeOffset o, string keep, CancellationToken ct = default) => Task.FromResult(0);

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] q, int topK, IReadOnlyCollection<string> allowedSessionIds, CancellationToken ct = default)
    {
        LastAllowedSessions = allowedSessionIds;
        var chunk = new DocumentChunk { Id = Guid.NewGuid(), Content = "örnek içerik", SessionId = "seed" };
        return Task.FromResult<IReadOnlyList<ScoredChunk>>([new ScoredChunk(chunk, 0.9)]);
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchKeywordAsync(
        string query, int topK, IReadOnlyCollection<string> allowedSessionIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScoredChunk>>([]);
}

internal sealed class FakeReranker : IReranker
{
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string q, IReadOnlyList<ScoredChunk> c, int topN, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScoredChunk>>(c.Take(topN).ToArray());
}

internal sealed class FakeLlm : ILlmProvider
{
    public int CompleteCalls { get; private set; }
    // Testin cevabı özelleştirebilmesi için (ör. refusal metni döndürmek).
    public string Response { get; init; } = "üretilen cevap [chunk:1]";

    public Task<LlmCompletion> CompleteAsync(IReadOnlyList<ChatMessage> m, CancellationToken ct = default)
    {
        CompleteCalls++;
        return Task.FromResult(new LlmCompletion(Response, new TokenUsage(100, 20)));
    }
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> m,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        CompleteCalls++;
        yield return "üretilen ";
        yield return "cevap";
        await Task.CompletedTask;
    }
}

internal sealed class FakeFaithfulness : IFaithfulnessEvaluator
{
    public Task<FaithfulnessResult> EvaluateAsync(string a, RetrievedContext c, CancellationToken ct = default)
        => Task.FromResult(new FaithfulnessResult(1.0, [], true));
}

internal sealed class FakeCache : IResponseCache
{
    private readonly CachedAnswer? _hit;
    public bool SetCalled { get; private set; }
    public long HitCount { get; private set; }
    public FakeCache(CachedAnswer? hit = null) => _hit = hit;

    public Task<CachedAnswer?> GetAsync(string s, string q, CancellationToken ct = default)
    {
        if (_hit is not null) HitCount++;
        return Task.FromResult(_hit);
    }
    public Task SetAsync(string s, string q, CachedAnswer a, CancellationToken ct = default)
    {
        SetCalled = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeBudget : ITokenBudgetGuard
{
    private readonly bool _within;
    public int RecordedTokens { get; private set; }
    public FakeBudget(bool within = true) => _within = within;

    public Task<BudgetStatus> CheckAsync(CancellationToken ct = default)
        => Task.FromResult(BudgetStatus.From(_within ? 0 : 1_000, 100));
    public Task RecordUsageAsync(int tokens, CancellationToken ct = default)
    {
        RecordedTokens += tokens;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSession : ISessionAccessor
{
    public FakeSession(string id) => SessionId = id;
    public string SessionId { get; }
}

internal sealed class FakeConversationStore : IConversationStore
{
    private readonly List<ConversationTurn> _turns;
    public bool AppendCalled { get; private set; }

    public FakeConversationStore(params ConversationTurn[] history) => _turns = [.. history];

    public Task AppendAsync(string s, string q, string a, CancellationToken ct = default)
    {
        AppendCalled = true;
        _turns.Add(new ConversationTurn(q, a));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string s, int count, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationTurn>>(_turns.TakeLast(count).ToArray());
}

// Varsayılan: BYOK yok → null döner, RagQueryService havuz LLM'ini kullanır.
internal sealed class FakeUserLlmResolver : IUserLlmResolver
{
    private readonly ILlmProvider? _byok;
    public FakeUserLlmResolver(ILlmProvider? byok = null) => _byok = byok;
    public ILlmProvider? Resolve() => _byok;
}

internal sealed class FakeQueryRewriter : IQueryRewriter
{
    public string? LastRewrittenFrom { get; private set; }

    // Geçmiş varsa "yeniden yazıldı" işaretleyerek orijinali değiştir; yoksa aynen döndür.
    public Task<string> RewriteAsync(
        string question, IReadOnlyList<ConversationTurn> history, CancellationToken ct = default)
    {
        if (history.Count == 0)
            return Task.FromResult(question);
        LastRewrittenFrom = question;
        return Task.FromResult($"[rewritten] {question}");
    }
}
