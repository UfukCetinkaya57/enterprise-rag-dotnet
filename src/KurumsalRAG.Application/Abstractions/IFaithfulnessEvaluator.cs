using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Groundedness (faithfulness) değerlendirici portu. Üretilen cevabın
/// gerçekten getirilen context'e dayanıp dayanmadığını (hallucination) denetler.
/// </summary>
public interface IFaithfulnessEvaluator
{
    Task<FaithfulnessResult> EvaluateAsync(
        string answer,
        RetrievedContext context,
        CancellationToken cancellationToken = default);
}

/// <param name="Score">0-1 arası groundedness skoru (1 = tamamen desteklenmiş).</param>
/// <param name="UnsupportedClaims">Context'te desteklenmeyen iddiaların listesi.</param>
/// <param name="Passed">Skor, konfigüre edilen eşiği geçti mi.</param>
public sealed record FaithfulnessResult(
    double Score,
    IReadOnlyList<string> UnsupportedClaims,
    bool Passed);
