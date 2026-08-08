using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// Cache anahtarı için soruyu normalize eder: küçük harf (TR kültürü), fazla boşluk sadeleştirme,
/// sondaki noktalama temizliği. "Kaç gün?" ile "kaç gün" aynı cache girdisine düşsün diye.
/// </summary>
public static class QuestionNormalizer
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Normalize(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return string.Empty;

        var lowered = question.Trim().ToLower(Tr);
        // Boşlukları tekilleştir.
        var collapsed = string.Join(' ', lowered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        // Sondaki noktalama işaretlerini at (?, !, ., …).
        return collapsed.TrimEnd('?', '!', '.', ',', ';', ':', ' ', '…');
    }

    /// <summary>Normalize edilmiş sorunun sabit uzunlukta hash'i (cache birincil anahtar bileşeni).</summary>
    public static string Hash(string question)
    {
        var normalized = Normalize(question);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }
}
