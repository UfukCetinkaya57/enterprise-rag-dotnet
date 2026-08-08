namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Query use-case portu: soru → (kill-switch/bütçe/cache) → guard → embed → retrieve →
/// rerank → prompt → generate. Hem non-stream (JSON) hem stream (SSE) cevap üretir.
/// </summary>
public interface IRagQueryService
{
    /// <summary>Non-stream: tam cevabı + gözlem metadata'sını döndürür.</summary>
    Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream: yapılandırılmış olaylar akıtır (status → token'lar → final).
    /// UI kaynakları, groundedness skorunu ve cached/limited durumunu bu olaylardan alır.
    /// </summary>
    IAsyncEnumerable<RagStreamChunk> StreamAsync(string question, CancellationToken cancellationToken = default);
}

/// <summary>Cevabın türü — kill-switch/bütçe ("limited"), cache ("cached") ya da normal.</summary>
public enum AnswerType
{
    Normal,
    Cached,
    Limited
}

/// <summary>Bir RAG cevabı + gözlem/değerlendirme metadata'sı.</summary>
public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<CitedSource> Sources,
    RagObservability Observability,
    AnswerType Type = AnswerType.Normal)
{
    public bool Cached => Type == AnswerType.Cached;
    public bool Limited => Type == AnswerType.Limited;
}

public sealed record CitedSource(int Reference, Guid ChunkId, double Score);

/// <summary>Cevap başına maliyet + kalite gözlemi.</summary>
public sealed record RagObservability(
    int RetrievedCount,
    int RerankedCount,
    int PromptTokens,
    int CompletionTokens,
    double? FaithfulnessScore = null,
    bool PromptGuardTriggered = false)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// SSE akışındaki tek bir olay. Kind: "status" (başta tür bilgisi), "token" (cevap parçası),
/// "final" (kaynaklar + groundedness + tür).
/// </summary>
public sealed record RagStreamChunk(string Kind, string? Token = null, RagAnswer? Final = null)
{
    public static RagStreamChunk Status(AnswerType type) => new("status", Token: type.ToString());
    public static RagStreamChunk TokenChunk(string token) => new("token", Token: token);
    public static RagStreamChunk FinalChunk(RagAnswer answer) => new("final", Final: answer);
}
