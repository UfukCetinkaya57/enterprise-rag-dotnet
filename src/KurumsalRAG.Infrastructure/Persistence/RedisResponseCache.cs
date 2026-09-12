using System.Text.Json;
using System.Threading;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// Redis tabanlı yanıt cache'i (IResponseCache). Anahtar: prefix + session_id + normalize soru hash.
/// TTL Redis'in kendi expiry'siyle uygulanır (SET ... EX) — ayrı temizlik gerekmez. Dağıtık ve
/// düşük gecikmeli; PgResponseCache ile birebir aynı sözleşme, yalnızca depo farklı.
///
/// Paylaşımlı Redis güvenliği: tüm anahtarlar <see cref="CacheOptions.RedisKeyPrefix"/> ile yazılır
/// → aynı Redis'i kullanan başka uygulamalarla çakışmaz.
/// </summary>
public sealed class RedisResponseCache : IResponseCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisResponseCache> _logger;
    private long _hitCount;

    public RedisResponseCache(
        IConnectionMultiplexer redis, IOptions<DemoOptions> options, ILogger<RedisResponseCache> logger)
    {
        _redis = redis;
        _options = options.Value.Cache;
        _logger = logger;
    }

    public long HitCount => Interlocked.Read(ref _hitCount);

    public async Task<CachedAnswer?> GetAsync(
        string sessionId, string question, CancellationToken cancellationToken = default)
    {
        // Cache OPSİYONEL bir optimizasyondur: Redis hatası = cache miss (isteği çökertmez).
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(Key(sessionId, question));
            if (value.IsNullOrEmpty)
                return null;

            var cached = JsonSerializer.Deserialize<CachedAnswer>((string)value!);
            if (cached is not null)
                Interlocked.Increment(ref _hitCount);
            return cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache okuma hatası; cache-miss olarak devam ediliyor.");
            return null;
        }
    }

    public async Task SetAsync(
        string sessionId, string question, CachedAnswer answer, CancellationToken cancellationToken = default)
    {
        // Yazma hatası da isteği çökertmemeli — cevap zaten üretildi.
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(answer);
            // TTL en az 1 saat (yanlış config'te 0/negatif expiry Redis'i hataya düşürmesin).
            var ttl = TimeSpan.FromHours(Math.Max(1, _options.TtlHours));
            await db.StringSetAsync(Key(sessionId, question), json, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache yazma hatası; yok sayılıyor.");
        }
    }

    /// <summary>Prefix + session + normalize soru hash → çakışmasız, deterministik anahtar.</summary>
    private string Key(string sessionId, string question)
        => $"{_options.RedisKeyPrefix}{sessionId}:{QuestionNormalizer.Hash(question)}";
}
