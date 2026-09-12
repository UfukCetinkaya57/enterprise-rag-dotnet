namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// Cohere Rerank API konfigürasyonu (cross-encoder yeniden sıralama). RerankerType="Cohere" ise
/// kullanılır. API key .env'den (COHERE_API_KEY); model/URL appsettings'ten. Ücretsiz trial key
/// destekler. Cross-encoder kalitesi (query+doc çiftini birlikte skorlar) — bi-encoder cosine'dan
/// daha isabetli, ama soru başına 1 dış çağrı (kotalı).
/// </summary>
public sealed class CohereOptions
{
    public const string SectionName = "Cohere";

    /// <summary>API anahtarı — .env / COHERE_API_KEY'den gelir, appsettings'te DURMAZ.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.cohere.com";

    /// <summary>Rerank modeli. Çok dilli (Türkçe dahil) için rerank-multilingual önerilir.</summary>
    public string Model { get; init; } = "rerank-multilingual-v3.0";
}
