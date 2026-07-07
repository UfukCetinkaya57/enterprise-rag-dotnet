namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Embedding sağlayıcısı portu. Metni sabit boyutlu bir vektöre çevirir
/// (varsayılan: text-embedding-3-small, 1536 boyut). Provider-agnostik.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Tek bir metni embed eder.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Batch embed — ingestion'da çağrı sayısını/maliyeti azaltır.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>Üretilen vektörlerin boyutu (DB kolon boyutuyla eşleşmeli).</summary>
    int Dimensions { get; }
}
