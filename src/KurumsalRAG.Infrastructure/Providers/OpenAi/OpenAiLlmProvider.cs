using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.OpenAi;

/// <summary>
/// OpenAI Chat Completions API adapter'ı. ILlmProvider port'unun implementasyonu.
/// CompleteAsync token kullanımını da döndürür (maliyet gözlemi).
/// </summary>
public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiLlmProvider(HttpClient http, IOptions<OpenAiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, stream: false);

        using var response = await _http.PostAsJsonAsync("chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("OpenAI chat cevabı boş döndü.");

        var content = payload.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var usage = payload.Usage is { } u
            ? new TokenUsage(u.PromptTokens, u.CompletionTokens)
            : TokenUsage.Zero;

        return new LlmCompletion(content, usage);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // OpenAI stream'i SSE formatında: her satır "data: {json}" ya da "data: [DONE]".
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line["data:".Length..].Trim();
            if (json == "[DONE]")
                yield break;

            var token = ParseDeltaContent(json);
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private static string? ParseDeltaContent(string json)
    {
        // Tek bir stream parçasından delta.content'i güvenle çıkar.
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var delta = choices[0].GetProperty("delta");
        return delta.TryGetProperty("content", out var content) ? content.GetString() : null;
    }

    private ChatRequest BuildRequest(IReadOnlyList<ChatMessage> messages, bool stream)
        => new(
            _options.ChatModel,
            messages.Select(m => new ChatRequestMessage(RoleToString(m.Role), m.Content)).ToArray(),
            stream,
            stream ? new StreamOptions(IncludeUsage: false) : null);

    private static string RoleToString(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => "user"
    };

    // --- Wire modelleri ---
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatRequestMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("stream_options")] StreamOptions? StreamOptions);

    private sealed record StreamOptions(
        [property: JsonPropertyName("include_usage")] bool IncludeUsage);

    private sealed record ChatRequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice> Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ResponseMessage? Message);

    private sealed record ResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
