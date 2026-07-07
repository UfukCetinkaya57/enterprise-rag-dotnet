namespace KurumsalRAG.Domain.Entities;

/// <summary>
/// Yüklenen bir kaynak dokümanı temsil eder (bir PDF dosyası).
/// Bir doküman birden çok <see cref="DocumentChunk"/> içerir.
/// </summary>
public sealed class DocumentEntity
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public DateTimeOffset UploadedAt { get; init; }
    public int ChunkCount { get; set; }

    public static DocumentEntity Create(string fileName)
        => new()
        {
            // .NET 9+ Guid.CreateVersion7 -> zaman-sıralı (index/insert lokalitesi için iyi).
            Id = Guid.CreateVersion7(),
            FileName = fileName,
            UploadedAt = DateTimeOffset.UtcNow
        };
}
