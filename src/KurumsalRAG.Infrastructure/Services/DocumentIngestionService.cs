using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Infrastructure.Ingestion;
using Microsoft.Extensions.Logging;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Ingestion use-case: PDF → metin → chunk → embed → pgvector.
/// Port'lara (IEmbeddingProvider, IVectorStore) bağlıdır, somut sağlayıcıya değil.
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly PdfTextExtractor _pdfExtractor;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        PdfTextExtractor pdfExtractor,
        TextChunker chunker,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        ILogger<DocumentIngestionService> logger)
    {
        _pdfExtractor = pdfExtractor;
        _chunker = chunker;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestPdfAsync(
        Stream pdfStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var text = _pdfExtractor.ExtractText(pdfStream);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"'{fileName}' dosyasından metin çıkarılamadı (boş ya da taranmış PDF olabilir).");

        var chunkTexts = _chunker.Chunk(text);
        _logger.LogInformation("'{File}' {ChunkCount} parçaya bölündü.", fileName, chunkTexts.Count);

        var embeddings = await _embeddings.EmbedBatchAsync(chunkTexts, cancellationToken);

        var document = DocumentEntity.Create(fileName);
        var chunks = new List<DocumentChunk>(chunkTexts.Count);
        var totalTokens = 0;

        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var tokens = TokenEstimator.Estimate(chunkTexts[i]);
            totalTokens += tokens;
            chunks.Add(new DocumentChunk
            {
                Id = Guid.CreateVersion7(),
                DocumentId = document.Id,
                Content = chunkTexts[i],
                ChunkIndex = i,
                TokenCount = tokens,
                Embedding = embeddings[i]
            });
        }

        document.ChunkCount = chunks.Count;
        await _vectorStore.SaveDocumentAsync(document, cancellationToken);
        await _vectorStore.UpsertChunksAsync(chunks, cancellationToken);

        _logger.LogInformation(
            "'{File}' yazıldı: {Chunks} chunk, ~{Tokens} embedding token.",
            fileName, chunks.Count, totalTokens);

        return new IngestionResult(document.Id, fileName, chunks.Count, totalTokens);
    }
}
