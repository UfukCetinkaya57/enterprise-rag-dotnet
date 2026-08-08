using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Api.Session;

/// <summary>
/// ISessionAccessor'ın web implementasyonu: session id'yi HttpContext.Items'tan okur
/// (SessionCookieMiddleware tarafından set edilir). Use-case servisleri HttpContext'e
/// doğrudan bağlı olmaz; yalnızca port'u görür.
/// </summary>
public sealed class HttpSessionAccessor : ISessionAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpSessionAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string SessionId
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(SessionCookieMiddleware.ItemKey, out var value)
                && value is string sid && !string.IsNullOrWhiteSpace(sid))
            {
                return sid;
            }
            // HTTP dışı bağlam (ör. arka plan) — anonim/izole varsayılan.
            return "anonymous";
        }
    }
}
