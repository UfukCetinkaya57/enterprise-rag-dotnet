namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// IP başına günlük istek kotası bekçisi. Sayaç restart'a dayanıklı biçimde (DB'de) tutulur —
/// süreç yeniden başlasa da pencere sıfırlanmaz. daily_usage ile aynı desen.
/// </summary>
public interface IIpRateLimiter
{
    /// <summary>
    /// Bu IP + kapsam (query/upload) için isteği kaydeder ve limitin aşılıp aşılmadığını döner.
    /// Atomik artış: sayaç önce artırılır, dönen değer limitle karşılaştırılır.
    /// </summary>
    Task<RateLimitDecision> HitAsync(string ip, RateScope scope, int dailyLimit, CancellationToken cancellationToken = default);
}

public enum RateScope
{
    Query,
    Upload
}

/// <param name="Allowed">İsteğe izin verildi mi (bu istek dahil sayaç limiti aşmıyorsa true).</param>
/// <param name="HitsToday">Bugün bu IP+kapsam için toplam istek (bu istek dahil).</param>
public sealed record RateLimitDecision(bool Allowed, int HitsToday);
