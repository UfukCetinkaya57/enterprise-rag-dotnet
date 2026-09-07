using System.ComponentModel;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Application.Retrieval;
using KurumsalRAG.Application.Sessions;
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
    private readonly ISessionAccessor _session;
    private readonly RetrievalOptions _options;
    private readonly DemoOptions _demo;

    public RetrievalAgent(
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IReranker reranker,
        ISessionAccessor session,
        IOptions<RagOptions> options,
        IOptions<DemoOptions> demo)
    {
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _reranker = reranker;
        _session = session;
        _options = options.Value.Retrieval;
        _demo = demo.Value;
    }

    /// <summary>Soruya göre top-n chunk getirir (embed → session-filtreli search → rerank).</summary>
    [KernelFunction("retrieve")]
    [Description("Kullanıcı sorusuyla ilgili doküman parçalarını (chunk) getirir.")]
    public async Task<RetrievedContext> RetrieveAsync(
        [Description("Kullanıcının sorusu")] string question,
        CancellationToken cancellationToken = default)
    {
        var allowed = AllowedSessions();
        var queryEmbedding = await _embeddings.EmbedAsync(question, cancellationToken);
        var vectorHits = await _vectorStore.SearchAsync(queryEmbedding, _options.TopK, allowed, cancellationToken);

        IReadOnlyList<ScoredChunk> candidates;
        if (_options.Hybrid)
        {
            var keywordHits = await _vectorStore.SearchKeywordAsync(question, _options.TopK, allowed, cancellationToken);
            candidates = ReciprocalRankFusion.Fuse([vectorHits, keywordHits], _options.TopK);
        }
        else
        {
            candidates = vectorHits;
        }

        var ranked = await _reranker.RerankAsync(question, candidates, _options.TopN, cancellationToken);

        var chunks = new List<RetrievedChunk>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var scored = ranked[i];
            chunks.Add(new RetrievedChunk(i + 1, scored.Chunk.Id, scored.Chunk.Content, scored.Score));
        }
        return new RetrievedContext(chunks);
    }

    /// <summary>Retrieval'ın görebileceği session'lar: kullanıcının kendi + seed.</summary>
    private string[] AllowedSessions() => SessionScope.Allowed(_session.SessionId, _demo.SeedSessionId);
}
