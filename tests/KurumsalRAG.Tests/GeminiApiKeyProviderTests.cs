using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Providers.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// BYOK + çoklu-anahtar rotasyonu davranışının kanıtı: BYOK önceliği, round-robin dağıtım,
/// 429/503 cooldown failover ve "hepsi cooldown'da" geri düşüşü.
/// </summary>
public sealed class GeminiApiKeyProviderTests
{
    private sealed class FakeUserKey(string? key) : IUserApiKeyAccessor
    {
        public string? UserApiKey { get; set; } = key;
        public string? UserProvider { get; set; }
    }

    private static GeminiApiKeyProvider Build(string keys, IUserApiKeyAccessor userKey, int cooldown = 60)
    {
        var options = Options.Create(new GeminiOptions { ApiKeys = keys, KeyCooldownSeconds = cooldown });
        return new GeminiApiKeyProvider(userKey, options, NullLogger<GeminiApiKeyProvider>.Instance);
    }

    [Fact]
    public void Byok_verildiginde_havuz_yerine_kullanici_anahtari_kullanilir()
    {
        var provider = Build("pool-1,pool-2", new FakeUserKey("user-key"));

        var lease = provider.Acquire();

        Assert.Equal("user-key", lease.Key);
        Assert.True(lease.IsByok);
        Assert.Equal(-1, lease.PoolIndex);
    }

    [Fact]
    public void Byok_openai_saglayicisiyla_verildiginde_gemini_havuzuna_dusulur()
    {
        // KRİTİK: Kullanıcı OpenAI anahtarı verdiyse o anahtar Gemini EMBEDDING'ine KOYULMAMALI
        // (400/401 olur). Embedding hep havuzla çalışmalı; BYOK yalnızca provider=gemini iken geçerli.
        var provider = Build("havuz-key", new FakeUserKey("sk-openai-key") { UserProvider = "openai" });

        var lease = provider.Acquire();

        Assert.False(lease.IsByok);                 // BYOK olarak kullanılmadı
        Assert.Equal("havuz-key", lease.Key);       // havuz anahtarı seçildi
    }

    [Fact]
    public void Byok_gemini_saglayicisiyla_verildiginde_kullanici_anahtari_kullanilir()
    {
        var provider = Build("havuz-key", new FakeUserKey("user-gemini-key") { UserProvider = "gemini" });

        var lease = provider.Acquire();

        Assert.True(lease.IsByok);
        Assert.Equal("user-gemini-key", lease.Key);
    }

    [Fact]
    public void Byok_yoksa_havuzda_round_robin_dagitir()
    {
        var provider = Build("a,b,c", new FakeUserKey(null));

        // Ardışık üç istek üç farklı anahtarı dolaşmalı (sırayla).
        var keys = new[] { provider.Acquire().Key, provider.Acquire().Key, provider.Acquire().Key };

        Assert.Equal(3, keys.Distinct().Count());
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
    }

    [Fact]
    public void Basarisiz_bildirilen_anahtar_cooldown_boyunca_atlanir()
    {
        var provider = Build("a,b", new FakeUserKey(null));

        // İlk anahtarı al ve başarısız (429/503) bildir → cooldown'a girsin.
        var first = provider.Acquire();
        provider.ReportFailure(first);

        // Sonraki birkaç istek cooldown'daki anahtarı ASLA döndürmemeli.
        for (var i = 0; i < 5; i++)
        {
            var next = provider.Acquire();
            Assert.NotEqual(first.Key, next.Key);
        }
    }

    [Fact]
    public void Tum_anahtarlar_cooldownda_ise_yine_de_bir_anahtar_doner()
    {
        var provider = Build("a,b", new FakeUserKey(null));

        provider.ReportFailure(new ApiKeyLease("a", false, 0));
        provider.ReportFailure(new ApiKeyLease("b", false, 1));

        // Teslim olmak yerine (boş dönmek yerine) yine de bir anahtar denenmeli.
        var lease = provider.Acquire();
        Assert.Contains(lease.Key, new[] { "a", "b" });
    }

    [Fact]
    public void Byok_anahtari_icin_failure_bildirimi_havuzu_etkilemez()
    {
        var provider = Build("a,b", new FakeUserKey(null));

        // BYOK lease'i başarısız bildirmek no-op olmalı (kullanıcının kendi kotası).
        provider.ReportFailure(new ApiKeyLease("user-key", IsByok: true, PoolIndex: -1));

        // Havuz tamamen sağlam: iki anahtar da erişilebilir olmalı.
        var seen = new HashSet<string>();
        for (var i = 0; i < 4; i++)
            seen.Add(provider.Acquire().Key);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Havuz_bos_ve_byok_yoksa_anlamli_hata_firlatir()
    {
        var provider = Build("", new FakeUserKey(null));

        Assert.Throws<InvalidOperationException>(() => provider.Acquire());
    }

    [Fact]
    public void Tekil_ApiKey_de_havuza_dahil_edilir()
    {
        var options = Options.Create(new GeminiOptions { ApiKey = "solo", ApiKeys = "" });
        var provider = new GeminiApiKeyProvider(
            new FakeUserKey(null), options, NullLogger<GeminiApiKeyProvider>.Instance);

        Assert.Equal("solo", provider.Acquire().Key);
    }
}
