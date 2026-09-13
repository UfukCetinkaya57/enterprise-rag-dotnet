using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Infrastructure.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Ingestion use-case: PDF → metin → chunk → embed → pgvector.
/// Port'lara (IEmbeddingProvider, IVectorStore) bağlıdır, somut sağlayıcıya değil.
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly PdfTextExtractor _pdfExtractor;
    private readonly TextChunker _chunker;
    private readonly SemanticChunker _semanticChunker;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly UploadOptions _upload;
    private readonly ChunkingOptions _chunking;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        PdfTextExtractor pdfExtractor,
        TextChunker chunker,
        SemanticChunker semanticChunker,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IOptions<DemoOptions> demo,
        IOptions<RagOptions> rag,
        ILogger<DocumentIngestionService> logger)
    {
        _pdfExtractor = pdfExtractor;
        _chunker = chunker;
        _semanticChunker = semanticChunker;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _upload = demo.Value.Upload;
        _chunking = rag.Value.Chunking;
        _logger = logger;
    }

    private bool ParentDocumentEnabled =>
        string.Equals(_chunking.Strategy, "ParentDocument", StringComparison.OrdinalIgnoreCase);

    private bool SemanticEnabled =>
        string.Equals(_chunking.Strategy, "Semantic", StringComparison.OrdinalIgnoreCase);

    public async Task<IngestionResult> IngestPdfAsync(
        Stream pdfStream,
        string fileName,
        string sessionId,
        long fileBytes,
        CancellationToken cancellationToken = default)
    {
        var extract = _pdfExtractor.ExtractText(pdfStream, _upload.MaxPages);
        if (string.IsNullOrWhiteSpace(extract.Text))
            throw new InvalidOperationException($"'{fileName}' dosyasından metin çıkarılamadı (boş ya da taranmış PDF olabilir).");

        // Strategy'ye göre chunk'la. ParentDocument: child ile embed, LLM'e parent bağlamı.
        // Semantic: cümleleri embed edip anlam sınırlarında böl. FixedSize: klasik.
        IReadOnlyList<string> childTexts;
        IReadOnlyList<string?> parentTexts;
        if (SemanticEnabled)
        {
            // Cümlelere böl → cümleleri PARÇALI batch embed (provider batch limitini aşmamak için)
            // → ardışık benzerlikle grupla. Büyük PDF binlerce cümle üretebilir; tek dev batch
            // Gemini/OpenAI limitine takılır, o yüzden EmbedInBatchesAsync ile böleriz.
            var sentences = _semanticChunker.SplitSentences(extract.Text);
            if (sentences.Count == 0)
                throw new InvalidOperationException($"'{fileName}' cümlelere bölünemedi.");
            var sentenceEmbeddings = await EmbedInBatchesAsync(sentences, cancellationToken);
            childTexts = _semanticChunker.GroupBySimilarity(sentences, sentenceEmbeddings);
            parentTexts = new string?[childTexts.Count]; // Semantic tek başına, parent yok
        }
        else if (ParentDocumentEnabled)
        {
            // Guard: parent, child'dan belirgin büyük olmalı (small-to-big). Değilse parent≈child
            // olur ve özellik sessizce anlamsızlaşır → uyar (yanlış config production'a sızmasın).
            if (_chunking.ParentMaxTokens <= _chunking.MaxTokens)
                _logger.LogWarning(
                    "ParentDocument stratejisi etkin ama ParentMaxTokens ({Parent}) <= MaxTokens ({Child}); " +
                    "parent bağlamı child'a yakın olacak, small-to-big faydası kaybolur. ParentMaxTokens'ı artırın.",
                    _chunking.ParentMaxTokens, _chunking.MaxTokens);

            var pairs = _chunker.ChunkWithParents(extract.Text);
            childTexts = pairs.Select(p => p.Child).ToArray();
            parentTexts = pairs.Select(p => (string?)p.Parent).ToArray();
        }
        else
        {
            childTexts = _chunker.Chunk(extract.Text);
            parentTexts = new string?[childTexts.Count]; // hepsi null
        }

        _logger.LogInformation("'{File}' (session={Session}) {ChunkCount} parçaya bölündü (strateji={Strategy}).",
            fileName, sessionId, childTexts.Count, _chunking.Strategy);

        // Embedding HER ZAMAN child üzerinden (isabetli retrieval). Parçalı batch (limit koruması).
        var embeddings = await EmbedInBatchesAsync(childTexts, cancellationToken);

        var document = DocumentEntity.Create(fileName, sessionId, fileBytes);
        var chunks = new List<DocumentChunk>(childTexts.Count);
        var totalTokens = 0;

        for (var i = 0; i < childTexts.Count; i++)
        {
            var tokens = TokenEstimator.Estimate(childTexts[i]);
            totalTokens += tokens;
            chunks.Add(new DocumentChunk
            {
                Id = Guid.CreateVersion7(),
                DocumentId = document.Id,
                SessionId = sessionId,
                Content = childTexts[i],
                ParentContent = parentTexts[i],
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

    /// <summary>
    /// Metinleri sabit boyutlu partiler halinde embed eder (tek dev batch provider limitini aşmasın).
    /// Semantic yolda cümle sayısı çok yüksek olabilir; Gemini batchEmbedContents ve OpenAI input
    /// dizisi sınırlıdır. Sıra korunur (partiler ardışık birleştirilir).
    /// </summary>
    private const int EmbedBatchSize = 96;

    private async Task<IReadOnlyList<float[]>> EmbedInBatchesAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count <= EmbedBatchSize)
            return await _embeddings.EmbedBatchAsync(texts, cancellationToken);

        var all = new List<float[]>(texts.Count);
        for (var start = 0; start < texts.Count; start += EmbedBatchSize)
        {
            var batch = texts.Skip(start).Take(EmbedBatchSize).ToArray();
            var embedded = await _embeddings.EmbedBatchAsync(batch, cancellationToken);
            all.AddRange(embedded);
        }
        return all;
    }
}
