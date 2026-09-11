namespace KurumsalRAG.Api.Middleware;

/// <summary>
/// BYOK (Bring Your Own Key): 'X-User-Api-Key' header'ı varsa okuyup HttpContext.Items'a koyar.
/// HttpUserApiKeyAccessor buradan alır; anahtar yalnızca istek scope'unda yaşar — body'ye,
/// loga veya cache anahtarına ASLA girmez. Header yoksa hiçbir şey yapılmaz (ücretsiz havuz).
/// </summary>
public sealed class UserApiKeyMiddleware
{
    public const string HeaderName = "X-User-Api-Key";
    public const string ProviderHeaderName = "X-User-Ai-Provider";
    public const string ItemKey = "UserApiKey";
    public const string ProviderItemKey = "UserAiProvider";

    private readonly RequestDelegate _next;

    public UserApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var value))
        {
            var key = value.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(key))
                context.Items[ItemKey] = key;
        }

        // Sağlayıcı seçimi (openai|gemini|grok) — anahtarla birlikte cevap-üreten LLM'i belirler.
        if (context.Request.Headers.TryGetValue(ProviderHeaderName, out var provider))
        {
            var p = provider.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(p))
                context.Items[ProviderItemKey] = p;
        }

        await _next(context);
    }
}
