using System.Globalization;
using System.Text;

namespace KurumsalRAG.Application.Evaluation;

/// <summary>
/// Metinde beklenen gerçeğin geçip geçmediğini tolere ederek kontrol eder: küçük harf +
/// Türkçe diakritik sadeleştirme ("yıllık"≈"yillik") + boşluk normalizasyonu. Saf, test edilebilir.
/// </summary>
public static class FactMatcher
{
    /// <summary><paramref name="expectedFacts"/>'ten HERHANGİ biri metinde geçiyor mu (OR).</summary>
    public static bool ContainsAny(string text, IReadOnlyList<string> expectedFacts)
    {
        if (expectedFacts.Count == 0)
            return false;
        var haystack = Normalize(text);
        return expectedFacts.Any(f => haystack.Contains(Normalize(f), StringComparison.Ordinal));
    }

    /// <summary>
    /// Cevap bir "reddetme" mi (context dışı → "dokümanlarda yok"). LLM refusal metnini kelimesi
    /// kelimesine üretmeyebilir (ör. "...maaş zammı bilgisi bulunmuyor" veya "...bulunmuyor [chunk:1]").
    /// Bu yüzden tam string yerine refusal'ın ÇEKİRDEK ifadesini (normalize) ararız.
    /// </summary>
    public static bool IsRefusal(string answer)
    {
        var normalized = Normalize(answer);
        // "bilgi bulunmuyor" / "bulunmuyor" / "yer almiyor" / "belirtilmemis" refusal sinyalleri.
        return normalized.Contains("bulunmuyor", StringComparison.Ordinal)
            || normalized.Contains("yer almiyor", StringComparison.Ordinal)
            || normalized.Contains("belirtilmemis", StringComparison.Ordinal)
            || normalized.Contains("bilgi yok", StringComparison.Ordinal);
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var lowered = s.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var stripped = StripDiacritics(lowered);
        return string.Join(' ', stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string StripDiacritics(string s)
    {
        // Türkçe özel harfleri ASCII karşılığına indir (unaccent benzeri).
        var map = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            map.Append(ch switch
            {
                'ı' or 'i' or 'î' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ü' or 'û' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                'â' => 'a',
                _ => ch
            });
        }
        return map.ToString();
    }
}
