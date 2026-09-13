using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Api.Controllers;

/// <summary>
/// Aktif RAG konfigürasyonunu döndürür (observability paneli için). Hangi tekniklerin devrede
/// olduğunu (chunking stratejisi, reranker, hybrid, provider, faithfulness, reflection, cache)
/// canlı gösterir. Hassas bilgi (anahtar vb.) İÇERMEZ — yalnızca "sistem şu an nasıl çalışıyor".
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly RagOptions _rag;
    private readonly AiProviderOptions _provider;
    private readonly DemoOptions _demo;

    public SystemController(
        IOptions<RagOptions> rag, IOptions<AiProviderOptions> provider, IOptions<DemoOptions> demo)
    {
        _rag = rag.Value;
        _provider = provider.Value;
        _demo = demo.Value;
    }

    /// <summary>Aktif konfigürasyon özeti — UI paneli bunu okuyup rozet olarak gösterir.</summary>
    [HttpGet("config")]
    public IActionResult Config() => Ok(new
    {
        provider = _provider.Provider,
        retrieval = new
        {
            topK = _rag.Retrieval.TopK,
            topN = _rag.Retrieval.TopN,
            hybrid = _rag.Retrieval.Hybrid,
            reranker = _rag.Retrieval.RerankerType
        },
        chunking = new
        {
            strategy = _rag.Chunking.Strategy,
            maxTokens = _rag.Chunking.MaxTokens
        },
        quality = new
        {
            faithfulness = _rag.Faithfulness.Enabled,
            reflection = _rag.Reflection.Enabled,
            conversation = _rag.Conversation.Enabled
        },
        cache = _demo.Cache.Provider
    });
}
