using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Services;

namespace KurumsalRAG.Infrastructure.Ingestion;

/// <summary>
/// Semantic chunking: metni ANLAM sınırlarında böler. Ardışık cümlelerin embedding benzerliği
/// (cosine) bir eşiğin altına düşünce (konu değişimi) yeni chunk başlar. Böylece her chunk tek bir
/// konuya odaklanır (sabit-boyut bölmenin cümle ortasından/konu ortasından kesmesini önler).
///
/// SAF ve test edilebilir: embedding'leri KENDİ ÜRETMEZ — cümleleri döndürür (<see cref="SplitSentences"/>),
/// ingestion onları embed edip <see cref="GroupBySimilarity"/>'ye verir. Böylece dış bağımlılık yok.
/// </summary>
public sealed class SemanticChunker
{
    private readonly ChunkingOptions _options;

    public SemanticChunker(ChunkingOptions options) => _options = options;

    /// <summary>
    /// Metni cümlelere böler (. ! ? ve satır sonu). Bir "cümle" <see cref="ChunkingOptions.SemanticMaxTokens"/>'ı
    /// aşarsa (noktalama içermeyen dev paragraf) kelime bazında alt-bölünür → hiçbir birim embedding
    /// limitini/boyut sınırını aşmaz. Ondalık ("3.14") ortasındaki nokta bölme SAYILMAZ.
    /// </summary>
    public IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var raw = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            current.Append(ch);

            // Ondalık koruması: '.' iki rakam arasındaysa (3.14) cümle sonu değildir.
            if (ch == '.' && i > 0 && i + 1 < text.Length &&
                char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
                continue;

            if (ch is '.' or '!' or '?' or '\n')
            {
                var s = current.ToString().Trim();
                if (s.Length > 1)
                    raw.Add(s);
                current.Clear();
            }
        }
        var tail = current.ToString().Trim();
        if (tail.Length > 1)
            raw.Add(tail);

        // Aşırı büyük "cümle"leri (noktalamasız dev paragraf) kelime bazında alt-böl (üst sınır garantisi).
        var sentences = new List<string>(raw.Count);
        foreach (var s in raw)
            sentences.AddRange(SplitOversized(s));
        return sentences;
    }

    /// <summary>SemanticMaxTokens'ı aşan bir parçayı kelime bazında güvenli boyutlu alt-parçalara böler.</summary>
    private IEnumerable<string> SplitOversized(string sentence)
    {
        if (TokenEstimator.Estimate(sentence) <= _options.SemanticMaxTokens)
        {
            yield return sentence;
            yield break;
        }

        var words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // ~1.25 token/kelime → maxWords ≈ SemanticMaxTokens / 1.25.
        var maxWords = Math.Max(1, (int)(_options.SemanticMaxTokens / 1.25));
        for (var start = 0; start < words.Length; start += maxWords)
            yield return string.Join(' ', words.Skip(start).Take(maxWords));
    }

    /// <summary>
    /// Cümleleri, ardışık embedding benzerliğine göre chunk'lara gruplar. Benzerlik
    /// <see cref="ChunkingOptions.SemanticBreakThreshold"/>'un altına düşünce yeni chunk başlar;
    /// ayrıca bir chunk <see cref="ChunkingOptions.SemanticMaxTokens"/>'ı aşarsa zorla bölünür.
    /// </summary>
    /// <param name="sentences">SplitSentences çıktısı.</param>
    /// <param name="embeddings">Her cümlenin embedding'i (aynı sıra, aynı uzunluk).</param>
    public IReadOnlyList<string> GroupBySimilarity(
        IReadOnlyList<string> sentences, IReadOnlyList<float[]> embeddings)
    {
        if (sentences.Count == 0)
            return [];
        if (sentences.Count != embeddings.Count)
            throw new ArgumentException("Cümle ve embedding sayısı eşleşmeli.");

        var chunks = new List<string>();
        var current = new List<string> { sentences[0] };
        var currentTokens = TokenEstimator.Estimate(sentences[0]);

        for (var i = 1; i < sentences.Count; i++)
        {
            var similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);
            var nextTokens = TokenEstimator.Estimate(sentences[i]);

            // Konu değişimi (benzerlik düşük) VEYA boyut sınırı aşıldı → chunk'ı kapat, yenisini başlat.
            var breakHere = similarity < _options.SemanticBreakThreshold
                            || currentTokens + nextTokens > _options.SemanticMaxTokens;

            if (breakHere)
            {
                chunks.Add(string.Join(' ', current));
                current = [sentences[i]];
                currentTokens = nextTokens;
            }
            else
            {
                current.Add(sentences[i]);
                currentTokens += nextTokens;
            }
        }

        if (current.Count > 0)
            chunks.Add(string.Join(' ', current));

        return chunks;
    }

    /// <summary>
    /// İki vektörün cosine benzerliği. Boyut uyuşmazlığı bir yapılandırma/sağlayıcı hatasıdır →
    /// SESSİZCE 0 döndürüp her cümleyi ayrı chunk yapmak yerine açıkça hata fırlatırız (gizli
    /// bozulma yerine görünür hata). Sıfır-norm (boş embedding) 0 benzerlik = güvenli sınır.
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Embedding boyutları uyuşmuyor: {a.Length} vs {b.Length}.");
        if (a.Length == 0)
            return 0.0;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0)
            return 0.0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
