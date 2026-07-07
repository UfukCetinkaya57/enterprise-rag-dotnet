using KurumsalRAG.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KurumsalRAG.Api.Health;

/// <summary>DB bağlantısı + embedding provider erişilebilirliğini kontrol eder.</summary>
public sealed class RagHealthCheck : IHealthCheck
{
    private readonly IVectorStore _vectorStore;

    public RagHealthCheck(IVectorStore vectorStore) => _vectorStore = vectorStore;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var dbOk = await _vectorStore.IsHealthyAsync(cancellationToken);
        return dbOk
            ? HealthCheckResult.Healthy("pgvector erişilebilir.")
            : HealthCheckResult.Unhealthy("pgvector'a bağlanılamıyor.");
    }
}
