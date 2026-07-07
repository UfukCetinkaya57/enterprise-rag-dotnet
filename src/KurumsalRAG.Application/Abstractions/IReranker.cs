using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Yeniden sıralama portu. Retrieval'ın getirdiği top-k adayı soruya alaka
/// açısından yeniden puanlayıp top-n'e daraltır. Bugün LLM/hibrit skor;
/// yarın cross-encoder veya Cohere Rerank bu port'un arkasına takılır.
/// </summary>
public interface IReranker
{
    Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default);
}
