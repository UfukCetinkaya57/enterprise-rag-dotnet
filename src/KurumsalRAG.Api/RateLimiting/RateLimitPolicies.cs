namespace KurumsalRAG.Api.RateLimiting;

/// <summary>Rate limit politika adları (controller'larda [EnableRateLimiting] ile referanslanır).</summary>
public static class RateLimitPolicies
{
    public const string Queries = "per-ip-queries";
    public const string Uploads = "per-ip-uploads";
}
