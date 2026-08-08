namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Session başına doküman kotası bekçisi (doküman sayısı + toplam boyut).
/// IP başına kotalar web katmanında rate limiting ile ele alınır; bu port session bazlıdır.
/// </summary>
public interface ISessionQuota
{
    /// <summary>
    /// Bu session yeni bir dokümanı (verilen boyutta) yükleyebilir mi? Mevcut kullanım
    /// documents tablosundan türetilir (kayıt = dokümanın kendisi, ayrı sayaç yok).
    /// </summary>
    Task<QuotaCheck> CanUploadAsync(string sessionId, long newFileBytes, CancellationToken cancellationToken = default);
}

/// <param name="Allowed">İşleme izin verildi mi.</param>
/// <param name="Reason">Reddedildiyse kullanıcı-dostu neden (aksi halde null).</param>
public sealed record QuotaCheck(bool Allowed, string? Reason)
{
    public static QuotaCheck Ok { get; } = new(true, null);
    public static QuotaCheck Deny(string reason) => new(false, reason);
}
