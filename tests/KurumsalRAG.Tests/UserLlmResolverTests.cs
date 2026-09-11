using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Providers;
using KurumsalRAG.Infrastructure.Providers.Gemini;
using KurumsalRAG.Infrastructure.Providers.OpenAi;
using Microsoft.Extensions.Options;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// BYOK cevap-üreten LLM çözümü: anahtar yoksa null (havuz), sağlayıcıya göre doğru adapter,
/// desteklenmeyen sağlayıcı → net hata. Embedding'e dokunmaz (yalnızca LLM).
/// </summary>
public sealed class UserLlmResolverTests
{
    private sealed class FakeUserKey(string? key, string? provider) : IUserApiKeyAccessor
    {
        public string? UserApiKey { get; } = key;
        public string? UserProvider { get; } = provider;
    }

    // Named client'ı gerçekten üretebilen minimal factory (BaseAddress resolver'da set edilir).
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static UserLlmResolver Build(string? key, string? provider) => new(
        new FakeUserKey(key, provider),
        new FakeHttpClientFactory(),
        Options.Create(new OpenAiOptions()),
        Options.Create(new GeminiOptions()),
        Options.Create(new ByokOptions()));

    [Fact]
    public void Anahtar_yoksa_null_doner_havuz_kullanilir()
    {
        Assert.Null(Build(null, null).Resolve());
        Assert.Null(Build("", "openai").Resolve());
    }

    [Fact]
    public void Openai_secilince_openai_adapteri_doner()
    {
        var llm = Build("sk-test", "openai").Resolve();
        Assert.IsType<OpenAiLlmProvider>(llm);
    }

    [Fact]
    public void Grok_secilince_openai_uyumlu_adapter_doner()
    {
        // Grok, OpenAI-uyumlu Chat Completions API kullanır → OpenAiLlmProvider paylaşılır.
        var llm = Build("xai-test", "grok").Resolve();
        Assert.IsType<OpenAiLlmProvider>(llm);
    }

    [Fact]
    public void Gemini_secilince_gemini_adapteri_doner()
    {
        var llm = Build("AIza-test", "gemini").Resolve();
        Assert.IsType<GeminiLlmProvider>(llm);
    }

    [Fact]
    public void Saglayici_belirtilmezse_varsayilan_gemini()
    {
        // Geriye uyumluluk: eski BYOK (yalnızca anahtar) → Gemini.
        var llm = Build("AIza-test", null).Resolve();
        Assert.IsType<GeminiLlmProvider>(llm);
    }

    [Fact]
    public void Buyuk_kucuk_harf_ve_bosluk_tolere_edilir()
    {
        Assert.IsType<OpenAiLlmProvider>(Build("sk-test", "  OpenAI ").Resolve());
    }

    [Fact]
    public void Desteklenmeyen_saglayici_net_hata_firlatir()
    {
        Assert.Throws<InvalidApiKeyException>(() => Build("key", "cohere").Resolve());
    }
}
