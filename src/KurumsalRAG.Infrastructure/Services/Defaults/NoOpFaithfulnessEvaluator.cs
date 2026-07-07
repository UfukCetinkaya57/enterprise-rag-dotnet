using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Services.Defaults;

/// <summary>
/// Faz 1 varsayılanı: değerlendirme yapmaz (skor 1.0). Faz 3'te SK tabanlı
/// LlmFaithfulnessEvaluator bunun yerine kayıtlanır.
/// </summary>
public sealed class NoOpFaithfulnessEvaluator : IFaithfulnessEvaluator
{
    public Task<FaithfulnessResult> EvaluateAsync(
        string answer,
        RetrievedContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new FaithfulnessResult(1.0, [], Passed: true));
}
