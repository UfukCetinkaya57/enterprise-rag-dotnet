using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Services.Defaults;

/// <summary>
/// Faz 1 varsayılanı: yeniden sıralama yapmaz, sadece cosine skoruna göre top-N alır.
/// Faz 2'de gerçek IReranker (LLM/hibrit) bunun yerine kayıtlanır — iş mantığı değişmez.
/// </summary>
public sealed class PassThroughReranker : IReranker
{
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScoredChunk> result = candidates
            .OrderByDescending(c => c.Score)
            .Take(topN)
            .ToArray();
        return Task.FromResult(result);
    }
}
