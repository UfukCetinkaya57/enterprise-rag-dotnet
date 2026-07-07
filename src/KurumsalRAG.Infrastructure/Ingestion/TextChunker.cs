using KurumsalRAG.Application.Configuration;

namespace KurumsalRAG.Infrastructure.Ingestion;

/// <summary>
/// Metni ~MaxTokens boyutlu, %OverlapRatio örtüşmeli parçalara böler.
/// Örtüşme, bölünme noktasında bağlam kaybını azaltır. Cümle sınırlarını
/// (boşluklarla) korumaya çalışır; kelime ortasından bölmez.
/// </summary>
public sealed class TextChunker
{
    private readonly ChunkingOptions _options;

    public TextChunker(ChunkingOptions options) => _options = options;

    public IReadOnlyList<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        // Token bütçesini yaklaşık kelime sayısına çevir (TokenEstimator ile tutarlı).
        var maxWords = Math.Max(1, EstimateWordBudget(_options.MaxTokens));
        var overlapWords = Math.Clamp((int)(maxWords * _options.OverlapRatio), 0, maxWords - 1);
        var step = Math.Max(1, maxWords - overlapWords);

        var chunks = new List<string>();
        for (var start = 0; start < words.Length; start += step)
        {
            var window = words.Skip(start).Take(maxWords).ToArray();
            chunks.Add(string.Join(' ', window));

            if (start + maxWords >= words.Length)
                break; // Son pencere diziyi kapsadı.
        }
        return chunks;
    }

    // ~4 karakter/token ve ~5 karakter/kelime(+boşluk) => token ≈ kelime * 1.25.
    private static int EstimateWordBudget(int maxTokens) => (int)(maxTokens / 1.25);
}
