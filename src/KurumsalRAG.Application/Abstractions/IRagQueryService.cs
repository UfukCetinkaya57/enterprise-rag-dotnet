namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Query use-case portu: soru → embed → retrieve → rerank → prompt → generate.
/// Hem non-stream (JSON) hem stream (SSE) cevap üretir.
/// </summary>
public interface IRagQueryService
{
    /// <summary>Non-stream: tam cevabı + gözlem metadata'sını döndürür.</summary>
    Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>Stream: cevabı token token akıtır (SSE endpoint için).</summary>
    IAsyncEnumerable<string> StreamAsync(string question, CancellationToken cancellationToken = default);
}

/// <summary>Bir RAG cevabı + gözlem/değerlendirme metadata'sı.</summary>
public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<CitedSource> Sources,
    RagObservability Observability);

public sealed record CitedSource(int Reference, Guid ChunkId, double Score);

/// <summary>Cevap başına maliyet + kalite gözlemi (Faz 4 evaluation log'unun temeli).</summary>
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
