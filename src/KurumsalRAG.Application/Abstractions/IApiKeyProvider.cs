namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Geçerli istek için kullanılacak sağlayıcı API anahtarını seçer. İki kaynak:
///   1) BYOK — kullanıcı kendi anahtarını verdiyse (<see cref="IUserApiKeyAccessor"/>) o kullanılır.
///   2) Havuz — vermediyse, çoklu-anahtar havuzundan round-robin + failover ile bir anahtar seçilir.
///
/// Anahtar seçimi HTTP katmanında bir DelegatingHandler'da yapılır (her istekte 'x-goog-api-key'
/// header'ı buradan gelen değere set edilir). Bir anahtar 429/503 dönerse <see cref="ReportFailure"/>
/// ile bildirilir → o anahtar kısa süre cooldown'a alınır (dolmuş kotayı ısrarla denememek için).
/// </summary>
public interface IApiKeyProvider
{
    /// <summary>Bu istek için kullanılacak anahtarı döndürür (BYOK varsa o, yoksa havuzdan seçilen).</summary>
    ApiKeyLease Acquire();

    /// <summary>
    /// Bir havuz anahtarının başarısız olduğunu (429/503) bildirir → cooldown'a alınır.
    /// BYOK anahtarları için no-op (kullanıcının kendi kotası; biz yönetmeyiz).
    /// </summary>
    void ReportFailure(ApiKeyLease lease);
}

/// <summary>
/// Bir istekte kullanılacak anahtar + onu havuza geri bağlayan kimlik. BYOK ise <see cref="IsByok"/>
/// true ve <see cref="ReportFailure"/> etkisizdir. Havuz anahtarı ise cooldown'a alınabilir.
/// </summary>
/// <param name="Key">Kullanılacak ham API anahtarı.</param>
/// <param name="IsByok">Kullanıcının kendi anahtarı mı (havuz yönetimi dışı).</param>
/// <param name="PoolIndex">Havuz anahtarıysa havuzdaki indeksi (failover bildirimi için); BYOK ise -1.</param>
public readonly record struct ApiKeyLease(string Key, bool IsByok, int PoolIndex);
