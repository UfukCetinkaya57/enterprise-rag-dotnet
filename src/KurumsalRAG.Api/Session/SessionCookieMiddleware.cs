namespace KurumsalRAG.Api.Session;

/// <summary>
/// Anonim session yönetimi: httpOnly cookie yoksa yeni bir GUID üretip set eder,
/// varsa okur. Session id'yi HttpContext.Items'a koyar; SessionAccessor buradan okur.
/// </summary>
public sealed class SessionCookieMiddleware
{
    public const string CookieName = "rag_sid";
    public const string ItemKey = "SessionId";

    private readonly RequestDelegate _next;

    public SessionCookieMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var sessionId = context.Request.Cookies[CookieName];

        if (string.IsNullOrWhiteSpace(sessionId) || !Guid.TryParse(sessionId, out _))
        {
            sessionId = Guid.CreateVersion7().ToString();
            context.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,                        // JS erişemez (XSS'e karşı)
                Secure = context.Request.IsHttps,       // HTTPS'te yalnızca güvenli
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(7),
                IsEssential = true
            });
        }

        context.Items[ItemKey] = sessionId;
        await _next(context);
    }
}
