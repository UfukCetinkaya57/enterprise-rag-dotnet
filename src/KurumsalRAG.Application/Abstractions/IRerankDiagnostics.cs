namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Rerank'in değerini görünür kılan tanılama portu: aynı sorgu için
/// rerank ÖNCESİ (cosine) ve SONRASI sıralamayı yan yana döndürür. Demo/gözlem amaçlı.
/// </summary>
public interface IRerankDiagnostics
{
    Task<RerankComparison> CompareAsync(string question, CancellationToken cancellationToken = default);
}

public sealed record RerankComparison(
    string Question,
    IReadOnlyList<RankedItem> BeforeRerank,
    IReadOnlyList<RankedItem> AfterRerank);

/// <param name="Rank">1-tabanlı sıra.</param>
/// <param name="Score">Cosine (öncesi) ya da rerank (sonrası) skoru.</param>
/// <param name="Source">Chunk'ın geldiği doküman adı.</param>
/// <param name="Snippet">İçerikten kısa bir kesit.</param>
public sealed record RankedItem(
    int Rank,
    Guid ChunkId,
    double Score,
    string Source,
    string Snippet);
