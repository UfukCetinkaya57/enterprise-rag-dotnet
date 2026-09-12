using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Providers.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// Çoklu-anahtar HAVUZ davranışının kanıtı: round-robin dağıtım, 429/503 cooldown failover,
/// "hepsi cooldown'da" geri düşüşü, boş havuz hatası. BYOK burada YOK — embedding + havuz LLM'i
/// hep havuzla çalışır (BYOK cevap LLM'i UserLlmResolver'da; bkz. UserLlmResolverTests).
/// </summary>
public sealed class GeminiApiKeyProviderTests
{
    private static GeminiApiKeyProvider Build(string keys, int cooldown = 60)
    {
        var options = Options.Create(new GeminiOptions { ApiKeys = keys, KeyCooldownSeconds = cooldown });
        return new GeminiApiKeyProvider(options, NullLogger<GeminiApiKeyProvider>.Instance);
    }

    [Fact]
    public void Havuzda_round_robin_dagitir()
    {
        var provider = Build("a,b,c");

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
        var provider = Build("a,b");

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
        var provider = Build("a,b");

        provider.ReportFailure(new ApiKeyLease("a", false, 0));
        provider.ReportFailure(new ApiKeyLease("b", false, 1));

        // Teslim olmak yerine (boş dönmek yerine) yine de bir anahtar denenmeli.
        var lease = provider.Acquire();
        Assert.Contains(lease.Key, new[] { "a", "b" });
    }

    [Fact]
    public void Byok_lease_icin_failure_bildirimi_havuzu_etkilemez()
    {
        var provider = Build("a,b");

        // IsByok lease'i (ör. resolver'dan kaçarsa) başarısız bildirmek no-op olmalı.
        provider.ReportFailure(new ApiKeyLease("user-key", IsByok: true, PoolIndex: -1));

        // Havuz tamamen sağlam: iki anahtar da erişilebilir olmalı.
        var seen = new HashSet<string>();
        for (var i = 0; i < 4; i++)
            seen.Add(provider.Acquire().Key);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Havuz_bos_ise_anlamli_hata_firlatir()
    {
        var provider = Build("");

        Assert.Throws<InvalidOperationException>(() => provider.Acquire());
    }

    [Fact]
    public void Tekil_ApiKey_de_havuza_dahil_edilir()
    {
        var options = Options.Create(new GeminiOptions { ApiKey = "solo", ApiKeys = "" });
        var provider = new GeminiApiKeyProvider(options, NullLogger<GeminiApiKeyProvider>.Instance);

        Assert.Equal("solo", provider.Acquire().Key);
    }
}
