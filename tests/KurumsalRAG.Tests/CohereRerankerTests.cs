using System.Net;
using System.Text;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Reranking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// Cohere cross-encoder reranker: API sonucunu doğru eşleştirir, hata/kota durumunda İSTEK
/// ÇÖKMEZ (retrieval sırasıyla devam eder — rerank iyileştirmedir, zorunluluk değil).
/// </summary>
public sealed class CohereRerankerTests
{
    // İstenen HTTP durum + gövdeyi döndüren sahte handler.
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static CohereReranker Build(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://api.cohere.com/") };
        return new CohereReranker(http, Options.Create(new CohereOptions()), NullLogger<CohereReranker>.Instance);
    }

    private static IReadOnlyList<ScoredChunk> Candidates() =>
    [
        new ScoredChunk(new DocumentChunk { Id = Guid.NewGuid(), Content = "aday-0" }, 0.5),
        new ScoredChunk(new DocumentChunk { Id = Guid.NewGuid(), Content = "aday-1" }, 0.4),
        new ScoredChunk(new DocumentChunk { Id = Guid.NewGuid(), Content = "aday-2" }, 0.3),
    ];

    [Fact]
    public async Task Api_sonucunu_alaka_sirasina_gore_yeniden_dizer()
    {
        // API index 2'yi en alakalı, sonra 0'ı döndürüyor → çıktı bu sırada olmalı.
        var body = """
            {"results":[{"index":2,"relevance_score":0.9},{"index":0,"relevance_score":0.6}]}
            """;
        var reranker = Build(HttpStatusCode.OK, body);
        var candidates = Candidates();

        var result = await reranker.RerankAsync("soru", candidates, topN: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("aday-2", result[0].Chunk.Content);   // en alakalı öne geldi
        Assert.Equal("aday-0", result[1].Chunk.Content);
        Assert.Equal(0.9, result[0].Score);                // Cohere skoru taşındı
    }

    [Fact]
    public async Task Api_hatasinda_istek_cokmez_retrieval_sirasiyla_doner()
    {
        // 429 (kota) → fallback: mevcut sırayla ilk top-n.
        var reranker = Build(HttpStatusCode.TooManyRequests, "{}");
        var candidates = Candidates();

        var result = await reranker.RerankAsync("soru", candidates, topN: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("aday-0", result[0].Chunk.Content);   // orijinal retrieval sırası korundu
        Assert.Equal("aday-1", result[1].Chunk.Content);
    }

    [Fact]
    public async Task Bos_aday_listesi_bos_doner()
    {
        var reranker = Build(HttpStatusCode.OK, "{\"results\":[]}");

        var result = await reranker.RerankAsync("soru", [], topN: 5);

        Assert.Empty(result);
    }
}
