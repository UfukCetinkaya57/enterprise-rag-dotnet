using System.ComponentModel;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// RetrievalAgent — vektör arama + rerank'i bir Semantic Kernel plugin fonksiyonu
/// olarak sarmalar. Mevcut portları (IEmbeddingProvider, IVectorStore, IReranker)
/// kullanır; soyutlamalar korunur. Orchestrator hem düz metot olarak hem de SK
/// fonksiyonu olarak çağırabilir.
/// </summary>
public sealed class RetrievalAgent
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly RetrievalOptions _options;

    public RetrievalAgent(
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IReranker reranker,
        IOptions<RagOptions> options)
    {
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _reranker = reranker;
        _options = options.Value.Retrieval;
    }

    /// <summary>Soruya göre top-n chunk getirir (embed → search → rerank).</summary>
    [KernelFunction("retrieve")]
    [Description("Kullanıcı sorusuyla ilgili doküman parçalarını (chunk) getirir.")]
    public async Task<RetrievedContext> RetrieveAsync(
        [Description("Kullanıcının sorusu")] string question,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddings.EmbedAsync(question, cancellationToken);
        var candidates = await _vectorStore.SearchAsync(queryEmbedding, _options.TopK, cancellationToken);
        var ranked = await _reranker.RerankAsync(question, candidates, _options.TopN, cancellationToken);

        var chunks = new List<RetrievedChunk>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var scored = ranked[i];
            chunks.Add(new RetrievedChunk(i + 1, scored.Chunk.Id, scored.Chunk.Content, scored.Score));
        }
        return new RetrievedContext(chunks);
    }
}
