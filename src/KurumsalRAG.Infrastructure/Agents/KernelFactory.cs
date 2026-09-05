using KurumsalRAG.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// Semantic Kernel <see cref="Kernel"/>'ini kurar. SK'nın IChatCompletionService'ini,
/// uygulamanın kendi ILlmProvider portuna köprüleyen <see cref="PortChatCompletionService"/>
/// ile besler — böylece SK agentic katmanı da hangi sağlayıcı seçiliyse (OpenAI/Gemini) ONU
/// kullanır. Provider-agnostik mimari SK dahil uçtan uca korunur; sağlayıcıyı değiştirmek için
/// burada hiçbir şey değişmez (yalnızca DI'da hangi ILlmProvider kayıtlıysa o kullanılır).
///
///   - ILlmProvider: uygulamanın düz LLM portu (streaming, token usage).
///   - SK Kernel: agentic orkestrasyon (plugin'ler, fonksiyon çağırma) — aynı port üzerinden.
/// </summary>
public sealed class KernelFactory
{
    private readonly ILlmProvider _llm;

    public KernelFactory(ILlmProvider llm) => _llm = llm;

    public Kernel Create()
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(new PortChatCompletionService(_llm));
        return builder.Build();
    }
}
