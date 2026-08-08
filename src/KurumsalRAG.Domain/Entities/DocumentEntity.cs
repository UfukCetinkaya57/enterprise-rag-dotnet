namespace KurumsalRAG.Domain.Entities;

/// <summary>
/// Yüklenen bir kaynak dokümanı temsil eder (bir PDF dosyası).
/// Bir doküman birden çok <see cref="DocumentChunk"/> içerir.
/// </summary>
public sealed class DocumentEntity
{
    public Guid Id { get; init; }

    /// <summary>Bu dokümanı yükleyen anonim session ('seed' = herkese açık örnek doküman).</summary>
    public string SessionId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Yüklenen dosyanın byte boyutu (session boyut kotası için).</summary>
    public long FileBytes { get; init; }

    public DateTimeOffset UploadedAt { get; init; }
    public int ChunkCount { get; set; }

    public static DocumentEntity Create(string fileName, string sessionId, long fileBytes)
        => new()
        {
            // .NET 9+ Guid.CreateVersion7 -> zaman-sıralı (index/insert lokalitesi için iyi).
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            FileName = fileName,
            FileBytes = fileBytes,
            UploadedAt = DateTimeOffset.UtcNow
        };
}
