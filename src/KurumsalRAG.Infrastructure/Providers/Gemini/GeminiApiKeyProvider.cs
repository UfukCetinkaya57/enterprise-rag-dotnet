using System.Collections.Concurrent;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.Gemini;

/// <summary>
/// <see cref="IApiKeyProvider"/>'ın Gemini implementasyonu: BYOK önceliği + çoklu-anahtar
/// havuzunda round-robin seçim + 429/503 failover (cooldown).
///
/// SINGLETON: havuz durumu (round-robin sayacı, cooldown zamanları) istekler arasında
/// paylaşılır. BYOK anahtarı ise istek scope'undan (<see cref="IUserApiKeyAccessor"/>) okunur
/// ve havuz yönetiminin tamamen dışındadır.
/// </summary>
public sealed class GeminiApiKeyProvider : IApiKeyProvider
{
    private readonly IUserApiKeyAccessor _userKey;
    private readonly ILogger<GeminiApiKeyProvider> _logger;
    private readonly IReadOnlyList<string> _pool;
    private readonly TimeSpan _cooldown;

    // Anahtar indeksi -> cooldown bitiş zamanı (UTC). O ana kadar o anahtar pas geçilir.
    private readonly ConcurrentDictionary<int, DateTimeOffset> _cooldownUntil = new();
    private int _cursor = -1; // round-robin sayacı (Interlocked ile artırılır)

    public GeminiApiKeyProvider(
        IUserApiKeyAccessor userKey,
        IOptions<GeminiOptions> options,
        ILogger<GeminiApiKeyProvider> logger)
    {
        _userKey = userKey;
        _logger = logger;
        var opts = options.Value;
        _pool = opts.ResolveKeyPool();
        _cooldown = TimeSpan.FromSeconds(Math.Max(1, opts.KeyCooldownSeconds));
    }

    public ApiKeyLease Acquire()
    {
        // 1) BYOK — kullanıcı kendi anahtarını verdiyse onu kullan (havuz yönetimi dışı).
        var byok = _userKey.UserApiKey;
        if (!string.IsNullOrWhiteSpace(byok))
            return new ApiKeyLease(byok, IsByok: true, PoolIndex: -1);

        if (_pool.Count == 0)
            throw new InvalidOperationException(
                "Gemini API anahtarı yok: BYOK verilmedi ve havuz (GEMINI_API_KEYS / GEMINI_API_KEY) boş.");

        // 2) Havuz — round-robin ile başlayıp cooldown'da OLMAYAN ilk anahtarı seç.
        var now = DateTimeOffset.UtcNow;
        var start = Interlocked.Increment(ref _cursor);
        for (var offset = 0; offset < _pool.Count; offset++)
        {
            var idx = (int)((uint)(start + offset) % (uint)_pool.Count);
            if (!IsInCooldown(idx, now))
                return new ApiKeyLease(_pool[idx], IsByok: false, PoolIndex: idx);
        }

        // Hepsi cooldown'da: en erken serbest kalacak anahtarı yine de dene (teslim olmaktan iyi).
        var fallbackIdx = (int)((uint)start % (uint)_pool.Count);
        _logger.LogWarning("Tüm Gemini anahtarları cooldown'da; #{Idx} yine de deneniyor.", fallbackIdx);
        return new ApiKeyLease(_pool[fallbackIdx], IsByok: false, PoolIndex: fallbackIdx);
    }

    public void ReportFailure(ApiKeyLease lease)
    {
        // BYOK: kullanıcının kendi kotası, biz cooldown yönetmeyiz.
        if (lease.IsByok || lease.PoolIndex < 0)
            return;

        var until = DateTimeOffset.UtcNow + _cooldown;
        _cooldownUntil[lease.PoolIndex] = until;
        _logger.LogWarning(
            "Gemini anahtar #{Idx} kotası/aşırı yükü nedeniyle {Sec}sn cooldown'a alındı.",
            lease.PoolIndex, _cooldown.TotalSeconds);
    }

    private bool IsInCooldown(int idx, DateTimeOffset now)
        => _cooldownUntil.TryGetValue(idx, out var until) && until > now;
}
