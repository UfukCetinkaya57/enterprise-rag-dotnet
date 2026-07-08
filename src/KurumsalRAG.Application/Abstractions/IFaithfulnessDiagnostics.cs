namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Faithfulness tanılama portu: bir soruyu tam agentic akıştan (retrieve → answer → check
/// → reflection) geçirir ve cevabı, groundedness skorunu, desteklenmeyen iddiaları ve
/// reflection izlerini birlikte döndürür. Demo/gözlem amaçlı.
/// </summary>
public interface IFaithfulnessDiagnostics
{
    Task<FaithfulnessReport> InspectAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Denetçiyi izole test etmek için: verilen (elle kurgulanmış) cevabı, verilen soruya
    /// getirilen context'e karşı denetler. Uydurma yakalamayı kanıtlamakta kullanılır.
    /// </summary>
    Task<FaithfulnessReport> CheckClaimAsync(
        string question, string candidateAnswer, CancellationToken cancellationToken = default);
}

public sealed record FaithfulnessReport(
    string Question,
    string Answer,
    double Score,
    bool Passed,
    IReadOnlyList<string> UnsupportedClaims,
    bool ReflectionApplied,
    double FirstScore,
    IReadOnlyList<ContextRef> UsedContext);

public sealed record ContextRef(int Reference, Guid ChunkId, double Score, string Snippet);
