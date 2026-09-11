namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>
/// BYOK (kullanıcının kendi anahtarı) cevap-üreten LLM ayarları. Kullanıcı hangi sağlayıcıyı
/// seçerse (OpenAI/Gemini/Grok) onun base URL + model'i buradan gelir — hard-code yok, model
/// isimleri değişince config yeter. Embedding'i ETKİLEMEZ (o hep havuz Gemini'sidir).
/// </summary>
public sealed class ByokOptions
{
    public const string SectionName = "Byok";

    /// <summary>Grok (xAI) — OpenAI-uyumlu Chat Completions API.</summary>
    public string GrokBaseUrl { get; init; } = "https://api.x.ai/v1";
    public string GrokChatModel { get; init; } = "grok-2-latest";
}
