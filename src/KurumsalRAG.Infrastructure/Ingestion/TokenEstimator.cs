namespace KurumsalRAG.Infrastructure.Ingestion;

/// <summary>
/// Yaklaşık token sayacı. Gerçek tiktoken yerine "≈4 karakter = 1 token" sezgisi
/// kullanır — chunking ve maliyet gözlemi için yeterli, bağımlılık eklemez.
/// İleride tiktoken tabanlı bir implementasyonla değiştirilebilir.
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 4.0;

    public static int Estimate(string text)
        => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);
}
