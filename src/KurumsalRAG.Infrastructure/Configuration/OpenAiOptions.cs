namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// OpenAI provider konfigürasyonu. Model isimleri appsettings'ten; API key .env'den.
/// Azure OpenAI'a geçişte sadece BaseUrl + deployment isimleri değişir.
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>API anahtarı — .env / ortam değişkeninden gelir, appsettings'te DURMAZ.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; init; } = 1536;
    public string ChatModel { get; init; } = "gpt-4o-mini";

    /// <summary>Rate limit (429) için retry sayısı.</summary>
    public int MaxRetries { get; init; } = 3;
}
