namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Geçerli istek için CEVAP-ÜRETEN LLM sağlayıcısını çözer. Kullanıcı kendi anahtarı + sağlayıcısını
/// verdiyse ('X-User-Api-Key' + 'X-User-Ai-Provider'), o sağlayıcının (OpenAI/Gemini/Grok) adapter'ı
/// döner; vermediyse null → çağıran varsayılan havuz LLM'ini (<see cref="ILlmProvider"/>) kullanır.
///
/// NOT: Yalnızca cevap üretimi kullanıcıya gider. Embedding/retrieval HER ZAMAN bizim Gemini
/// havuzumuzdadır (vektör boyutu DB şemasıyla sabit; sağlayıcılar arası embedding uyumsuzdur).
/// </summary>
public interface IUserLlmResolver
{
    /// <summary>
    /// BYOK sağlayıcısı+anahtarı varsa ona bağlı bir ILlmProvider döndürür; yoksa null.
    /// Desteklenmeyen sağlayıcı adı verilirse <see cref="InvalidApiKeyException"/> fırlatır.
    /// </summary>
    ILlmProvider? Resolve();
}
