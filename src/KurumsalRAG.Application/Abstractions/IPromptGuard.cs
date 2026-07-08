namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Prompt injection tespiti/temizliği portu. Kullanıcı girdisinde talimat ezme,
/// system prompt sızdırma, delimiter kaçışı gibi kalıpları arar.
/// Şu an kural tabanlı; ileride bu portun arkasına LLM tabanlı bir classifier
/// veya harici bir içerik-filtreleme servisi eklenebilir — çağıran değişmez.
/// </summary>
public interface IPromptGuard
{
    PromptGuardResult Inspect(string userInput);
}

/// <summary>Guard tespit ettiğinde ne yapılacağı (appsettings'ten seçilir).</summary>
public enum GuardAction
{
    /// <summary>Şüpheli girdi işlenmeye devam eder ama işaretlenir ve context ayrımıyla nötralize edilir.</summary>
    SanitizeAndWarn,

    /// <summary>Şüpheli girdi tümüyle reddedilir (istek işlenmez).</summary>
    Block
}

/// <param name="IsSuspicious">Girdi şüpheli işaretlendi mi.</param>
/// <param name="MatchedRule">Tetiklenen kuralın adı (yoksa null).</param>
/// <param name="Action">Tespit halinde uygulanan davranış.</param>
/// <param name="Reasons">Tetiklenen kural(lar)ın insan-okur açıklaması.</param>
/// <param name="SanitizedInput">İşleme sokulacak temizlenmiş girdi.</param>
public sealed record PromptGuardResult(
    bool IsSuspicious,
    string? MatchedRule,
    GuardAction Action,
    IReadOnlyList<string> Reasons,
    string SanitizedInput)
{
    /// <summary>Girdi bloklanmalı mı (şüpheli + Action=Block).</summary>
    public bool ShouldBlock => IsSuspicious && Action == GuardAction.Block;

    public static PromptGuardResult Clean(string input)
        => new(false, null, GuardAction.SanitizeAndWarn, [], input);
}
