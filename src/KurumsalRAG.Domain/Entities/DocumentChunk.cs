namespace KurumsalRAG.Domain.Entities;

/// <summary>
/// Bir dokümandan çıkarılmış, embed edilmiş metin parçası.
/// Vektör deposunda saklanan ve retrieval'da geri getirilen temel birim.
/// </summary>
public sealed class DocumentChunk
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }

    /// <summary>Session izolasyonu için denormalize edilmiş session id ('seed' = örnek).</summary>
    public string SessionId { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    /// <summary>Doküman içindeki 0-tabanlı sıra numarası.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Embedding vektörü (varsayılan text-embedding-3-small için 1536 boyut).</summary>
    public float[] Embedding { get; init; } = [];

    /// <summary>Maliyet/gözlem için yaklaşık token sayısı.</summary>
    public int TokenCount { get; init; }

    public static DocumentChunk Create(Guid documentId, string content, int chunkIndex, int tokenCount)
        => new()
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            TokenCount = tokenCount
        };
}
