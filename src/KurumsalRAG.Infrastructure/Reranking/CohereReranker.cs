using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Reranking;

/// <summary>
/// Cohere Rerank API adapter'ı (IReranker) — gerçek bir CROSS-ENCODER. Bi-encoder cosine (soru ve
/// doküman AYRI embed edilir) yerine, cross-encoder query+doküman çiftini BİRLİKTE değerlendirip
/// alaka skoru üretir → daha isabetli sıralama. Bedeli: soru başına 1 dış API çağrısı (kotalı).
///
/// Dayanıklılık: API başarısız olursa (kota/ağ) İSTEK ÇÖKMEZ — adayların mevcut (retrieval) sırasıyla
/// ilk top-n döndürülür. Rerank bir iyileştirmedir, zorunluluk değil.
/// </summary>
public sealed class CohereReranker : IReranker
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<CohereReranker> _logger;

    public CohereReranker(HttpClient http, IOptions<CohereOptions> options, ILogger<CohereReranker> logger)
    {
        _http = http;
        _model = options.Value.Model;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return [];

        try
        {
            // top_n aday sayısını AŞAMAZ (Cohere v2 400 döner). Küçük setlerde clamp'le ki
            // rerank çalışsın (sıralamanın en kritik olduğu yer küçük setlerdir).
            var effectiveTopN = Math.Min(topN, candidates.Count);
            var request = new RerankRequest(
                _model, query,
                candidates.Select(c => c.Chunk.Content).ToArray(),
                effectiveTopN);

            using var response = await _http.PostAsJsonAsync("v2/rerank", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken);
            if (payload?.Results is null || payload.Results.Count == 0)
                return Fallback(candidates, topN);

            // API alaka sırasına göre döner; index orijinal aday listesine işaret eder.
            // NOT: Cohere relevance_score (0-1) cross-encoder ölçeğindedir — cosine ölçeğinden
            // farklı dağılır. SIRALAMA doğru; gösterilen skor mutlak değil göreli okunmalı.
            // (Hata durumunda fallback cosine skorlarını korur — ölçek tutarlılığı için kabul.)
            var reranked = payload.Results
                .Where(r => r.Index >= 0 && r.Index < candidates.Count)
                .Select(r => new ScoredChunk(candidates[r.Index].Chunk, r.RelevanceScore))
                .Take(topN)
                .ToArray();

            return reranked.Length > 0 ? reranked : Fallback(candidates, topN);
        }
        catch (Exception ex)
        {
            // Kota/ağ hatası rerank'i düşürmeli, isteği değil.
            _logger.LogWarning(ex, "Cohere rerank başarısız; retrieval sırasıyla devam ediliyor.");
            return Fallback(candidates, topN);
        }
    }

    private static IReadOnlyList<ScoredChunk> Fallback(IReadOnlyList<ScoredChunk> candidates, int topN)
        => candidates.Take(topN).ToArray();

    // --- Wire modelleri ---
    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents,
        [property: JsonPropertyName("top_n")] int TopN);

    private sealed record RerankResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<RerankResult> Results);

    private sealed record RerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}
