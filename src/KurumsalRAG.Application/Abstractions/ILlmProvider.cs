using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// LLM üretim sağlayıcısı portu. Bugün OpenAI; yarın Azure OpenAI (KVKK/TR bölgesi)
/// veya on-prem Ollama — iş mantığına dokunmadan bu port'un arkasında değişir.
/// Dependency Inversion'ın (SOLID) kanıtı.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Tek seferde tam cevap üretir. Kullanılan token'lar <paramref name="usage"/> ile döner.</summary>
    Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>Cevabı token token stream eder (SSE için).</summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir üretim çağrısının sonucu + maliyet/gözlem için token kullanımı.</summary>
public sealed record LlmCompletion(string Content, TokenUsage Usage);

public sealed record TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
    public static TokenUsage Zero { get; } = new(0, 0);
}
