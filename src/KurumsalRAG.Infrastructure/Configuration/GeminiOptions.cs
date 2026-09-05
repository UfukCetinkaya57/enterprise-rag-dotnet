namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// Google Gemini provider konfigürasyonu. Model isimleri appsettings'ten; API key .env'den
/// (GEMINI_API_KEY). Provider-agnostik mimarinin ikinci somut sağlayıcısı.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>API anahtarı — .env / GEMINI_API_KEY'den gelir, appsettings'te DURMAZ.</summary>
    public string ApiKey { get; set; } = string.Empty;

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
