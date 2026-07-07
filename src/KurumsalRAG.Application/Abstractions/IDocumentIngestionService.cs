namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Ingestion use-case portu: PDF → metin çıkarımı → chunking → embed → vektör deposuna yaz.
/// </summary>
public interface IDocumentIngestionService
{
    Task<IngestionResult> IngestPdfAsync(
        Stream pdfStream,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed record IngestionResult(Guid DocumentId, string FileName, int ChunkCount, int TotalTokens);
