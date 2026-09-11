using KurumsalRAG.Api.Middleware;
using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Api.Session;

/// <summary>
/// IUserApiKeyAccessor'ın web implementasyonu: BYOK anahtarını HttpContext.Items'tan okur
/// (<see cref="UserApiKeyMiddleware"/> tarafından 'X-User-Api-Key' header'ından set edilir).
/// Anahtar istek scope'unda yaşar; body'ye/loga/session'a yazılmaz.
/// </summary>
public sealed class HttpUserApiKeyAccessor : IUserApiKeyAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserApiKeyAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string? UserApiKey
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(UserApiKeyMiddleware.ItemKey, out var value)
                && value is string key && !string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
            return null; // BYOK verilmedi → ücretsiz havuz kullanılır.
        }
    }
}
