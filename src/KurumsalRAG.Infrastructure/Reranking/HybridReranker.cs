using System.Globalization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Reranking;

/// <summary>
/// Hibrit reranker (IReranker): cosine benzerliği + anahtar-kelime örtüşmesi ile yeniden sıralar.
/// LLM ÇAĞRISI YAPMAZ — soru başına ekstra API çağrısı yok. Gemini free-tier gibi
/// rate-limit'li ortamlarda LlmReranker yerine kullanılır (hızlı + kotasız).
/// İleride cross-encoder / Cohere Rerank yine bu port'un arkasına takılabilir.
/// </summary>
public sealed class HybridReranker : IReranker
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Cosine ve keyword skorlarının ağırlıkları (toplam 1.0).
    private const double CosineWeight = 0.7;
    private const double KeywordWeight = 0.3;

    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<ScoredChunk>>([]);

        var queryTerms = Tokenize(query);

        var reranked = candidates
            .Select(c =>
            {
                var keywordScore = KeywordOverlap(queryTerms, Tokenize(c.Chunk.Content));
                // cosine skoru zaten 0-1; hibrit = ağırlıklı ortalama.
                var hybrid = CosineWeight * c.Score + KeywordWeight * keywordScore;
                return new ScoredChunk(c.Chunk, hybrid);
            })
            .OrderByDescending(c => c.Score)
            .Take(topN)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ScoredChunk>>(reranked);
    }

    /// <summary>Sorgu terimlerinin chunk'ta ne oranda geçtiği (0-1).</summary>
    private static double KeywordOverlap(IReadOnlyCollection<string> queryTerms, HashSet<string> contentTerms)
    {
        if (queryTerms.Count == 0)
            return 0.0;

        var hits = queryTerms.Count(contentTerms.Contains);
        return (double)hits / queryTerms.Count;
    }

    /// <summary>Metni küçük harfli, 2+ karakterli anlamlı kelimelere ayırır (TR kültürü).</summary>
    private static HashSet<string> Tokenize(string text)
    {
        var tokens = text.ToLower(Tr)
            .Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '?', '!', '(', ')', '"', '\'', '-', '/'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2);
        return new HashSet<string>(tokens, StringComparer.Ordinal);
    }
}
