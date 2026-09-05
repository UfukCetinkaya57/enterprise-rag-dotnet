using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.Gemini;

/// <summary>
/// Google Gemini Embeddings API adapter'ı (gemini-embedding-001).
/// IEmbeddingProvider port'unun ikinci somut implementasyonu — iş mantığı bunu bilmez.
/// Auth (x-goog-api-key) DI'da HttpClient default header'ına eklenir; burada sadece body gönderilir.
/// </summary>
public sealed class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiEmbeddingProvider(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public int Dimensions => _options.EmbeddingDimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        // Tek metin için Gemini'nin embedContent endpoint'i.
        var modelPath = $"models/{_options.EmbeddingModel}";
        var request = new EmbedContentRequest(
            modelPath,
            new Content([new Part(text)]),
            _options.EmbeddingDimensions);

        using var response = await _http.PostAsJsonAsync(
            $"{modelPath}:embedContent", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbedContentResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Gemini embedding cevabı boş döndü.");

        return payload.Embedding.Values;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var modelPath = $"models/{_options.EmbeddingModel}";

        // Batch endpoint: her metin ayrı bir request nesnesi. Model + boyut her istekte tekrarlanır.
        var request = new BatchEmbedContentsRequest(
            texts.Select(t => new EmbedContentRequest(
                modelPath,
                new Content([new Part(t)]),
                _options.EmbeddingDimensions)).ToArray());

        using var response = await _http.PostAsJsonAsync(
            $"{modelPath}:batchEmbedContents", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<BatchEmbedContentsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Gemini batch embedding cevabı boş döndü.");

        // API sırayı korur — request sırası ile embeddings sırası birebir eşleşir.
        return payload.Embeddings
            .Select(e => e.Values)
            .ToArray();
    }

    // --- Wire modelleri ---
    private sealed record EmbedContentRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] Content Content,
        [property: JsonPropertyName("outputDimensionality")] int OutputDimensionality);

    private sealed record BatchEmbedContentsRequest(
        [property: JsonPropertyName("requests")] IReadOnlyList<EmbedContentRequest> Requests);

    private sealed record Content(
        [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Part(
        [property: JsonPropertyName("text")] string Text);

    private sealed record EmbedContentResponse(
        [property: JsonPropertyName("embedding")] Embedding Embedding);

    private sealed record BatchEmbedContentsResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<Embedding> Embeddings);

    private sealed record Embedding(
        [property: JsonPropertyName("values")] float[] Values);
}
