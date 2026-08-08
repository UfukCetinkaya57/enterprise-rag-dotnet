namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Yanıt cache'i portu. Anahtar: (session_id + normalize edilmiş soru). Cache hit'te
/// LLM'e gidilmez. Bugün PostgreSQL tablosu; yarın Redis bu portun arkasına takılabilir.
/// </summary>
public interface IResponseCache
{
    /// <summary>Cache'te geçerli (TTL içinde) bir cevap varsa döndürür, yoksa null.</summary>
    Task<CachedAnswer?> GetAsync(string sessionId, string question, CancellationToken cancellationToken = default);

    /// <summary>Cevabı cache'e yazar.</summary>
    Task SetAsync(string sessionId, string question, CachedAnswer answer, CancellationToken cancellationToken = default);

    /// <summary>Süreci başından beri toplam cache hit sayısı (gözlem metriği).</summary>
    long HitCount { get; }
}

/// <summary>Cache'lenen cevap gövdesi (LLM üretimi olmadan geri servis edilir).</summary>
public sealed record CachedAnswer(
    string Answer,
    IReadOnlyList<CitedSource> Sources,
    double? FaithfulnessScore);
