namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Değerlendirme (evaluation) tanılama portu: bir soruyu tam akıştan geçirip
/// tek bir yapılandırılmış gözlem kaydı üretir — soru, kullanılan chunk'lar,
/// faithfulness, retrieval skorları, guard sonucu, token ve tahmini maliyet.
/// </summary>
public interface IEvaluationDiagnostics
{
    Task<EvaluationRecord> EvaluateAsync(string question, CancellationToken cancellationToken = default);
}

public sealed record EvaluationRecord(
    string Question,
    string Answer,
    GuardInfo Guard,
    IReadOnlyList<RetrievalScore> RetrievedChunks,
    double? FaithfulnessScore,
    bool FaithfulnessPassed,
    IReadOnlyList<string> UnsupportedClaims,
    bool ReflectionApplied,
    TokenCost Cost);

public sealed record GuardInfo(bool IsSuspicious, string? MatchedRule, string Action, IReadOnlyList<string> Reasons);

public sealed record RetrievalScore(int Reference, Guid ChunkId, double Score);

public sealed record TokenCost(int PromptTokens, int CompletionTokens, int EmbeddingTokens, decimal EstimatedUsd)
{
    public int TotalTokens => PromptTokens + CompletionTokens + EmbeddingTokens;
}
