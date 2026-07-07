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

    /// <summary>Sorgu vektörüne en yakın top-k chunk'ı cosine benzerliği ile getirir.</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>DB erişilebilirlik kontrolü (health check).</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
