namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// Google Gemini provider konfigürasyonu. Model isimleri appsettings'ten; API key .env'den
/// (GEMINI_API_KEY). Provider-agnostik mimarinin ikinci somut sağlayıcısı.
/// </summary>
public sealed record GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>API anahtarı — .env / GEMINI_API_KEY'den gelir, appsettings'te DURMAZ.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Çoklu anahtar havuzu — .env / GEMINI_API_KEYS'ten (virgülle ayrılmış, N adet).
    /// Rotasyon + failover için: bir anahtar 429/503 dolunca sonrakine geçilir.
    /// Boşsa tekil <see cref="ApiKey"/>'e düşülür (geriye uyumluluk).
    /// </summary>
    public string ApiKeys { get; set; } = string.Empty;

    /// <summary>
    /// Bir anahtar 429/503 aldığında kaç saniye pas geçileceği (cooldown). Dolan anahtarı
    /// ısrarla denememek için; süre sonunda havuza geri döner. Free-tier RPM ~dakika penceresi.
    /// </summary>
    public int KeyCooldownSeconds { get; init; } = 60;

    /// <summary>
    /// Havuzdaki tüm anahtarları (çoklu + tekil) tekilleştirip döndürür. Sıra korunur.
    /// GEMINI_API_KEYS öncelikli; boşsa GEMINI_API_KEY. İkisi de doluysa birleştirilir.
    /// </summary>
    public IReadOnlyList<string> ResolveKeyPool()
    {
        var keys = new List<string>();
        foreach (var k in ApiKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            keys.Add(k);
        if (!string.IsNullOrWhiteSpace(ApiKey))
            keys.Add(ApiKey.Trim());
        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Embedding modeli. output_dimensionality ile boyut sabitlenir.</summary>
    public string EmbeddingModel { get; init; } = "gemini-embedding-001";

    /// <summary>Embedding çıktı boyutu (şema vector(N) ile eşleşmeli). Gemini için 768 seçtik.</summary>
    public int EmbeddingDimensions { get; init; } = 768;

    /// <summary>Sohbet/üretim modeli (ücretsiz katman flash). gemini-2.x yeni kullanıcılara kapalı.</summary>
    public string ChatModel { get; init; } = "gemini-flash-lite-latest";

    /// <summary>Maliyet tahmini için 1M token başına USD (ücretsiz katmanda 0).</summary>
    public PricingOptions Pricing { get; init; } = new();
}
