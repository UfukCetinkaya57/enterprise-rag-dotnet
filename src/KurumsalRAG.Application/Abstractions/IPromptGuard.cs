namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Prompt injection tespiti/temizliği portu. Kullanıcı girdisinde
/// "önceki talimatları unut", system prompt sızdırma, delimiter kaçışı gibi
/// kalıpları arar. Basit kural tabanlı + istenirse LLM tabanlı sınıflandırma.
/// </summary>
public interface IPromptGuard
{
    PromptGuardResult Inspect(string userInput);
}

/// <param name="IsSuspicious">Girdi şüpheli işaretlendi mi.</param>
/// <param name="Reasons">Tetiklenen kural(lar)ın açıklaması.</param>
/// <param name="SanitizedInput">İşleme sokulacak temizlenmiş girdi.</param>
public sealed record PromptGuardResult(
    bool IsSuspicious,
    IReadOnlyList<string> Reasons,
    string SanitizedInput)
{
    public static PromptGuardResult Clean(string input) => new(false, [], input);
}
