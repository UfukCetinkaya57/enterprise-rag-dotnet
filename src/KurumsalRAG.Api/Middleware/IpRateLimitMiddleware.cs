using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Api.Middleware;

/// <summary>
/// DB tabanlı (restart-dayanıklı) IP rate limiting. Yol bazlı kapsam belirler:
/// /api/chat* → Query, /api/documents (POST) → Upload. Gerçek client IP'si
/// ForwardedHeaders sonrası RemoteIpAddress'ten okunur. Aşımda 429 + nazik mesaj.
/// .NET yerleşik in-memory limiter yerine geçer (kalıcı sayaç için).
/// </summary>
public sealed class IpRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpRateLimitMiddleware> _logger;

    public IpRateLimitMiddleware(RequestDelegate next, ILogger<IpRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIpRateLimiter limiter, IOptions<DemoOptions> demo)
    {
        var scope = ResolveScope(context);
        if (scope is null)
        {
            await _next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var limit = scope == RateScope.Upload ? demo.Value.Ip.UploadsPerDay : demo.Value.Ip.QueriesPerDay;

        var decision = await limiter.HitAsync(ip, scope.Value, limit, context.RequestAborted);
        if (!decision.Allowed)
        {
            _logger.LogWarning("IP kotası aşıldı. ip={Ip} scope={Scope} hits={Hits}/{Limit}",
                ip, scope, decision.HitsToday, limit);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.io/429",
                title = "Günlük kullanım limitine ulaştınız. Lütfen yarın tekrar deneyin.",
                status = 429
            });
            return;
        }

        await _next(context);
    }

    /// <summary>İsteğin hangi kotaya girdiğini belirler (yoksa null → limit uygulanmaz).</summary>
    private static RateScope? ResolveScope(HttpContext context)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/api/documents") && HttpMethods.IsPost(context.Request.Method))
            return RateScope.Upload;

        if (path.StartsWithSegments("/api/chat"))
            return RateScope.Query;

        return null;
    }
}
