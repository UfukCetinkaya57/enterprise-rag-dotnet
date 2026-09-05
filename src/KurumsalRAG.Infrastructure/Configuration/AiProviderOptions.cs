namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// Aktif AI sağlayıcısını seçer (appsettings "Ai:Provider"). Provider-agnostik mimarinin
/// tek anahtarı: "OpenAI" ya da "Gemini" — iş mantığına dokunmadan sağlayıcı değişir.
/// </summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; init; } = "OpenAI";

    public bool IsGemini => string.Equals(Provider, "Gemini", StringComparison.OrdinalIgnoreCase);
    public bool IsOpenAI => string.Equals(Provider, "OpenAI", StringComparison.OrdinalIgnoreCase);
}
