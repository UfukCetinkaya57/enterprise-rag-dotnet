using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Vektör deposu portu. Bugün PostgreSQL + pgvector; yarın Qdrant/Milvus/Azure AI Search —
/// bu port'un arkasında değişir. Cosine benzerliği ile top-k arama yapar.
/// </summary>
public interface IVectorStore
{
    /// <summary>Doküman kaydını yazar.</summary>
    Task SaveDocumentAsync(DocumentEntity document, CancellationToken cancellationToken = default);

    /// <summary>Chunk'ları embedding'leriyle birlikte upsert eder.</summary>
    Task UpsertChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sorgu vektörüne en yakın top-k chunk'ı cosine benzerliği ile getirir.
    /// Session izolasyonu: yalnızca <paramref name="allowedSessionIds"/> içindeki
    /// session'lara ait chunk'lar aranır (kullanıcının kendi + 'seed').
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        IReadOnlyCollection<string> allowedSessionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keyword (full-text) araması — hybrid search'ün keyword ayağı. Postgres tsvector/ts_rank
    /// ile sorgudaki kelimelerin geçtiği chunk'ları alaka sırasına göre getirir. Session filtreli.
    /// Vektör aramasından farkı: anlamı değil TAM KELİME eşleşmesini yakalar (kod/isim/kısaltma için).
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> SearchKeywordAsync(
        string query,
        int topK,
        IReadOnlyCollection<string> allowedSessionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// TTL temizliği: verilen tarihten eski chunk'ları/dokümanları siler,
    /// <paramref name="keepSessionId"/> (seed) hariç. Silinen chunk sayısını döndürür.
    /// </summary>
    Task<int> PurgeExpiredAsync(
        DateTimeOffset olderThan,
        string keepSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>DB erişilebilirlik kontrolü (health check).</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
