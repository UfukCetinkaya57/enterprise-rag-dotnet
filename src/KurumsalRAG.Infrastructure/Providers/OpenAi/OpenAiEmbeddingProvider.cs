using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.OpenAi;

/// <summary>
/// OpenAI Embeddings API adapter'ı (text-embedding-3-small).
/// IEmbeddingProvider port'unun somut implementasyonu — iş mantığı bunu bilmez.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiEmbeddingProvider(HttpClient http, IOptions<OpenAiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public int Dimensions => _options.EmbeddingDimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await EmbedBatchAsync([text], cancellationToken);
        return result[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var request = new EmbeddingRequest(_options.EmbeddingModel, texts, _options.EmbeddingDimensions);

        using var response = await _http.PostAsJsonAsync("embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken)
            ?? throw new InvalidOperationException("OpenAI embeddings cevabı boş döndü.");

        // API sırayı korur ama garanti altına almak için index'e göre sırala.
        return payload.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
