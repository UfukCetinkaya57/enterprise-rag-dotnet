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
/// Google Gemini generateContent API adapter'ı. ILlmProvider port'unun ikinci implementasyonu.
/// System mesajları Gemini'de ayrı system_instruction alanında taşınır (contents içinde "system" rolü yoktur).
/// CompleteAsync token kullanımını da döndürür (maliyet gözlemi).
/// </summary>
public sealed class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiLlmProvider(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages);

        using var response = await _http.PostAsJsonAsync(
            $"models/{_options.ChatModel}:generateContent", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Gemini generateContent cevabı boş döndü.");

        // İlk candidate'in tüm part'larını birleştir (genelde tek part gelir).
        var parts = payload.Candidates.FirstOrDefault()?.Content?.Parts;
        var content = parts is null
            ? string.Empty
            : string.Concat(parts.Select(p => p.Text));

        var usage = payload.UsageMetadata is { } u
            ? new TokenUsage(u.PromptTokenCount, u.CandidatesTokenCount)
            : TokenUsage.Zero;

        return new LlmCompletion(content, usage);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages);

        // SSE modu için alt=sse: her satır "data: {json}" formatında gelir.
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"models/{_options.ChatModel}:streamGenerateContent?alt=sse")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Gemini stream'i SSE formatında: her satır "data: {json}". [DONE] gelmeyebilir, stream bitince döngü biter.
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line["data:".Length..].Trim();
            if (json == "[DONE]")
                yield break;

            var token = ParseChunkText(json);
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private static string? ParseChunkText(string json)
    {
        // Tek bir stream parçasından candidates[0].content.parts[0].text'i güvenle çıkar.
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return null;

        return parts[0].TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private GenerateContentRequest BuildRequest(IReadOnlyList<ChatMessage> messages)
    {
        // System mesajlarını ayrı system_instruction alanında birleştir (\n\n ile).
        var systemTexts = messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content)
            .ToArray();

        var systemInstruction = systemTexts.Length > 0
            ? new Content(null, [new Part(string.Join("\n\n", systemTexts))])
            : null;

        // Kalan mesajları contents'e çevir: User -> "user", Assistant -> "model".
        var contents = messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new Content(RoleToString(m.Role), [new Part(m.Content)]))
            .ToArray();

        return new GenerateContentRequest(systemInstruction, contents);
    }

    private static string RoleToString(ChatRole role) => role switch
    {
        ChatRole.User => "user",
        ChatRole.Assistant => "model",
        _ => "user"
    };

    // --- Wire modelleri ---
    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("system_instruction"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Content? SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<Content> Contents);

    private sealed record Content(
        [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Part(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<Candidate> Candidates,
        [property: JsonPropertyName("usageMetadata")] UsageMetadata? UsageMetadata);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content);

    private sealed record UsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int CandidatesTokenCount,
        [property: JsonPropertyName("totalTokenCount")] int TotalTokenCount);
}
