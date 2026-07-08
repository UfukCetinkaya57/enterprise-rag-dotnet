using System.Text.RegularExpressions;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Security;

/// <summary>
/// Kural tabanlı prompt injection guard'ı (IPromptGuard). Türkçe + İngilizce kalıpları
/// üç kategoride arar: talimat ezme, rol/system sızdırma, delimiter/çıkış kaçışı.
///
/// Bu, savunmanın İLK katmanıdır; tek başına yeterli değildir. Asıl güvence, kullanıcı
/// içeriğinin system prompt'tan net delimiter'larla ayrılmasıdır (bkz. RagPromptBuilder).
/// İleride bu portun arkasına LLM tabanlı bir injection-classifier veya harici bir
/// içerik-filtreleme servisi eklenebilir; RagQueryService değişmez.
/// </summary>
public sealed partial class RuleBasedPromptGuard : IPromptGuard
{
    private readonly GuardAction _action;

    public RuleBasedPromptGuard(IOptions<RagOptions> options)
    {
        _action = Enum.TryParse<GuardAction>(options.Value.Security.PromptGuardAction, ignoreCase: true, out var a)
            ? a
            : GuardAction.SanitizeAndWarn;
    }

    // --- Kategori 1: Talimat ezme ---
    [GeneratedRegex(
        @"(ignore|disregard|forget|override)\s+(all\s+)?(previous|prior|above|the\s+above|your)\s+(instructions?|prompts?|rules?|context)" +
        @"|(önceki|yukarıdaki|tüm)\s+(tüm\s+)?(talimatları|kuralları|yönergeleri)\s*(unut|yok\s*say|görmezden\s*gel|boşver)" +
        @"|yukarıdakileri\s+(yok\s*say|unut|görmezden\s*gel)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstructionOverridePattern();

    // --- Kategori 2: Rol/system sızdırma ---
    [GeneratedRegex(
        @"(reveal|show|print|repeat|display|leak)\s+(me\s+)?(your\s+)?(system\s+)?(prompt|instructions?|rules?)" +
        @"|(you\s+are\s+now|act\s+as|pretend\s+to\s+be|from\s+now\s+on\s+you)" +
        @"|system\s*prompt('?u|'?nu)?\s*(göster|yaz|ver|söyle|paylaş)" +
        @"|(sen\s+artık|bundan\s+böyle\s+sen|şu\s+rolde|rolündesin|rolüne\s+gir)" +
        @"|talimatların(ı)?\s*(göster|yaz|söyle|açıkla)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleLeakPattern();

    // --- Kategori 3: Delimiter/çıkış kaçışı (sahte context/soru bloğu enjeksiyonu) ---
    [GeneratedRegex(
        @"(>>>|<<<|```|###|---)\s*(context|soru|question|system|talimat|instruction)" +
        @"|(context|soru|system)\s*:\s*.*(yap|uygula|ignore|unut|reveal|göster)" +
        @"|\n\s*(system|assistant)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelimiterEscapePattern();

    public PromptGuardResult Inspect(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return PromptGuardResult.Clean(userInput);

        var reasons = new List<string>();
        string? matchedRule = null;

        if (InstructionOverridePattern().IsMatch(userInput))
        {
            matchedRule ??= "InstructionOverride";
            reasons.Add("Talimat ezme girişimi (ör. 'önceki talimatları unut').");
        }
        if (RoleLeakPattern().IsMatch(userInput))
        {
            matchedRule ??= "RoleLeak";
            reasons.Add("Rol/system prompt sızdırma girişimi.");
        }
        if (DelimiterEscapePattern().IsMatch(userInput))
        {
            matchedRule ??= "DelimiterEscape";
            reasons.Add("Delimiter/çıkış kaçışı ile talimat enjeksiyonu denemesi.");
        }

        if (reasons.Count == 0)
            return PromptGuardResult.Clean(userInput);

        // Sanitize: şüpheli delimiter/kontrol kalıplarını nötralize et (block değilse kullanılır).
        var sanitized = Sanitize(userInput);

        return new PromptGuardResult(
            IsSuspicious: true,
            MatchedRule: matchedRule,
            Action: _action,
            Reasons: reasons,
            SanitizedInput: sanitized);
    }

    /// <summary>Delimiter ve rol etiketlerini zararsız hale getirir (system/user karışmasını engeller).</summary>
    private static string Sanitize(string input)
        => input
            .Replace(">>>", " ").Replace("<<<", " ")
            .Replace("```", " ").Replace("###", " ")
            .Replace("\r", " ").Replace("\n", " ")
            .Trim();
}
