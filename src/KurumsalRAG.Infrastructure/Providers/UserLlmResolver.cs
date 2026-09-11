using System.Net.Http.Headers;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Providers.Gemini;
using KurumsalRAG.Infrastructure.Providers.OpenAi;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers;

/// <summary>
/// IUserLlmResolver implementasyonu: kullanıcının 'X-User-Ai-Provider' + 'X-User-Api-Key'
/// bilgisine göre cevap-üreten LLM adapter'ını (OpenAI/Gemini/Grok) kurar. Anahtar yoksa null.
///
/// Her istekte taze bir HttpClient (IHttpClientFactory named client "byok") üretir; BaseUrl + auth
/// header'ı sağlayıcıya göre set edilir. Grok, OpenAI-uyumlu Chat Completions API'si kullandığından
/// OpenAiLlmProvider'ı paylaşır (yalnızca BaseUrl + model farklı).
/// </summary>
public sealed class UserLlmResolver : IUserLlmResolver
{
    /// <summary>BYOK HttpClient'ları için named client (DI'da timeout ile yapılandırılır).</summary>
    public const string HttpClientName = "byok";

    private readonly IUserApiKeyAccessor _userKey;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiOptions _openAiDefaults;
    private readonly GeminiOptions _geminiDefaults;
    private readonly ByokOptions _byok;

    private ILlmProvider? _resolved;   // istek başına memoize (scoped)
    private bool _resolvedComputed;

    public UserLlmResolver(
        IUserApiKeyAccessor userKey,
        IHttpClientFactory httpClientFactory,
        IOptions<OpenAiOptions> openAiDefaults,
        IOptions<GeminiOptions> geminiDefaults,
        IOptions<ByokOptions> byok)
    {
        _userKey = userKey;
        _httpClientFactory = httpClientFactory;
        _openAiDefaults = openAiDefaults.Value;
        _geminiDefaults = geminiDefaults.Value;
        _byok = byok.Value;
    }

    public ILlmProvider? Resolve()
    {
        // Scoped → aynı istekte birden çok çağrıda yeniden kurma (istemci/adapter tek kez).
        if (_resolvedComputed)
            return _resolved;

        _resolved = ResolveCore();
        _resolvedComputed = true;
        return _resolved;
    }

    private ILlmProvider? ResolveCore()
    {
        var key = _userKey.UserApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return null; // BYOK yok → çağıran havuz LLM'ini kullanır.

        // Sağlayıcı belirtilmemişse geriye uyumlu varsayılan: Gemini (eski BYOK davranışı).
        var provider = (_userKey.UserProvider ?? "gemini").Trim().ToLowerInvariant();

        return provider switch
        {
            "openai" => BuildOpenAiCompatible(key, _openAiDefaults.BaseUrl, _openAiDefaults.ChatModel),
            "grok" => BuildOpenAiCompatible(key, _byok.GrokBaseUrl, _byok.GrokChatModel),
            "gemini" => BuildGemini(key),
            _ => throw new InvalidApiKeyException() // desteklenmeyen sağlayıcı → net hata
        };
    }

    /// <summary>OpenAI ve Grok: aynı Chat Completions API, Bearer auth. Sadece BaseUrl+model farklı.</summary>
    private ILlmProvider BuildOpenAiCompatible(string key, string baseUrl, string model)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        // Havuz OpenAI ayarlarını klonla (tüm tuned alanlar korunur), yalnızca bağlantı+model'i override et.
        var options = Options.Create(_openAiDefaults with { ApiKey = key, BaseUrl = baseUrl, ChatModel = model });
        return new OpenAiLlmProvider(http, options);
    }

    private ILlmProvider BuildGemini(string key)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(_geminiDefaults.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("x-goog-api-key", key);

        // Havuz Gemini ayarlarını klonla; yalnızca anahtarı BYOK'unkiyle değiştir (model/boyut korunur).
        var options = Options.Create(_geminiDefaults with { ApiKey = key });
        return new GeminiLlmProvider(http, options);
    }
}
