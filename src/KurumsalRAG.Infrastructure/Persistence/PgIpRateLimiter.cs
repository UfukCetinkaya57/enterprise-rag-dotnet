using KurumsalRAG.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL tabanlı IP rate limiter (IIpRateLimiter). ip_usage tablosunda günlük sayaç tutar;
/// süreç yeniden başlasa da pencere korunur (in-memory'nin aksine restart'ta sıfırlanmaz).
/// DB hatasında FAIL-OPEN: isteği geçirir ve uyarı loglar (maliyet zaten token bütçesiyle korunur).
/// </summary>
public sealed class PgIpRateLimiter : IIpRateLimiter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgIpRateLimiter> _logger;

    public PgIpRateLimiter(NpgsqlDataSource dataSource, ILogger<PgIpRateLimiter> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<RateLimitDecision> HitAsync(
        string ip, RateScope scope, int dailyLimit, CancellationToken cancellationToken = default)
    {
        // Atomik upsert + artırılmış değeri geri döndür (tek round-trip, yarış koşulu yok).
        const string sql = """
            INSERT INTO ip_usage (usage_date, ip, scope, hits)
            VALUES (@date, @ip, @scope, 1)
            ON CONFLICT (usage_date, ip, scope) DO UPDATE
                SET hits = ip_usage.hits + 1
            RETURNING hits
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("date", DateTime.UtcNow.Date);
            cmd.Parameters.AddWithValue("ip", ip);
            cmd.Parameters.AddWithValue("scope", scope.ToString().ToLowerInvariant());
            var hits = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;

            return new RateLimitDecision(hits <= dailyLimit, hits);
        }
        catch (Exception ex)
        {
            // FAIL-OPEN: quota DB'si takılırsa gerçek kullanıcıyı 429'la cezalandırma.
            _logger.LogWarning(ex, "IP rate limiter DB hatası; istek geçiriliyor (fail-open). ip={Ip} scope={Scope}", ip, scope);
            return new RateLimitDecision(true, 0);
        }
    }
}
