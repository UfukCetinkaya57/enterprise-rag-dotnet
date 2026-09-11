namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Geçerli isteğin BYOK (Bring Your Own Key) anahtarını sağlar. Kullanıcı kendi API
/// anahtarını verdiyse ('X-User-Api-Key' header'ı) burada taşınır; vermediyse null.
/// Web katmanında HttpContext'ten okunur — istek scope'una özeldir, loglanmaz/saklanmaz.
///
/// Amaç: BYOK'ta kullanıcının kendi kotasını kullanmak (bizim key havuzumuza yük binmez);
/// BYOK yoksa <see cref="IApiKeyProvider"/> ücretsiz havuzdan bir anahtar seçer.
/// </summary>
public interface IUserApiKeyAccessor
{
    /// <summary>Kullanıcının verdiği API anahtarı; yoksa null (ücretsiz havuz kullanılır).</summary>
    string? UserApiKey { get; }

    /// <summary>
    /// Kullanıcının seçtiği sağlayıcı ('X-User-Ai-Provider' header'ı): "openai" | "gemini" | "grok".
    /// Anahtar varsa cevap-üretimi (LLM) bu sağlayıcıya gider. Boşsa/null ise varsayılan davranış
    /// (BYOK Gemini anahtarı olarak yorumlanır — geriye uyumluluk). Embedding HER ZAMAN havuzdadır.
    /// </summary>
    string? UserProvider { get; }
}
