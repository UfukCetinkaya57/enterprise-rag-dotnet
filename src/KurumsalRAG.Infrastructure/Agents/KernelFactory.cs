using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// Semantic Kernel <see cref="Kernel"/>'ini kurar ve OpenAI'yi SK'nın native
/// IChatCompletionService'i üzerinden bağlar. Bu, SK'nın provider soyutlamasını
/// gösterir; mevcut ILlmProvider soyutlamasıyla ÇELİŞMEZ — ikisi de Infrastructure
/// katmanında, farklı sorumluluklar için yaşar:
///   - ILlmProvider: uygulamanın kendi düz LLM portu (streaming, token usage).
///   - SK Kernel: agentic orkestrasyon (plugin'ler, fonksiyon çağırma).
/// Azure OpenAI'a geçişte sadece burada AddAzureOpenAIChatCompletion çağrılır.
/// </summary>
public sealed class KernelFactory
{
    private readonly OpenAiOptions _options;

    public KernelFactory(IOptions<OpenAiOptions> options) => _options = options.Value;

    public Kernel Create()
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: _options.ChatModel,
            apiKey: _options.ApiKey);
        return builder.Build();
    }
}
