namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Kullanıcının verdiği BYOK anahtarı sağlayıcı tarafından reddedildiğinde (400/401/403) fırlatılır.
/// Web katmanı bunu 400 + anlaşılır bir mesaja çevirir — jenerik 500 yerine kullanıcı anahtarının
/// geçersiz olduğunu anlar. Havuz anahtarları için KULLANILMAZ (onlar bizim sorumluluğumuz).
/// </summary>
public sealed class InvalidApiKeyException : Exception
{
    public InvalidApiKeyException()
        : base("Verdiğiniz API anahtarı geçersiz veya reddedildi. Lütfen anahtarınızı kontrol edin.")
    {
    }
}
