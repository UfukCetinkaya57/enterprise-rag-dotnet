using System.Net;
using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Api.Health;

/// <summary>
/// /health endpoint'i:
///  - Sığ (varsayılan): DB ping. PUBLIC — Nginx/monitoring buraya erişir.
///  - Derin (?deep=true): DB + provider (OpenAI /models) erişilebilirliği. YALNIZCA localhost —
///    provider probe'u dışarıya sızmasın (ve loopback dışından reddedilsin).
/// </summary>
public static class HealthEndpoint
{
    public static void MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", async (
            HttpContext ctx, IVectorStore store, IProviderHealthProbe provider, CancellationToken ct) =>
        {
            var deep = ctx.Request.Query["deep"] == "true";

            if (deep && !IsLoopback(ctx))
                return Results.NotFound(); // derin kontrol yalnızca localhost; dışarıya "yok" göster

            var dbOk = await store.IsHealthyAsync(ct);

            if (!deep)
            {
                return dbOk
                    ? Results.Text("Healthy")
                    : Results.Text("Unhealthy", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var providerOk = await provider.IsReachableAsync(ct);
            var healthy = dbOk && providerOk;
            return Results.Json(new
            {
                status = healthy ? "Healthy" : "Unhealthy",
                db = dbOk ? "ok" : "down",
                provider = providerOk ? "ok" : "unreachable"
            }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
    }

    private static bool IsLoopback(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip);
    }
}
