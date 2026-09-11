using System.Net;
using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Infrastructure.Providers.Gemini;

/// <summary>
/// Her Gemini HTTP isteğine 'x-goog-api-key' anahtarını EKLEYEN delegating handler.
/// Anahtar, sabit bir DefaultRequestHeader yerine <see cref="IApiKeyProvider"/>'dan istek anında
/// alınır — böylece BYOK ve çoklu-anahtar rotasyonu mümkün olur.
///
/// DI'da retry policy'nin İÇİNE kaydedilir (retry dışta): Polly bir 503'te yeniden denerken bu
/// handler tekrar çalışır ve <see cref="IApiKeyProvider.Acquire"/> ile TAZE bir anahtar seçer —
/// yani bir istek içinde bile dolu anahtardan sağlam anahtara failover olur. 429/503 dönen anahtar
/// havuza başarısız bildirilir (cooldown), böylece sonraki denemeler/istekler onu atlar.
/// BYOK anahtarında ReportFailure no-op'tur (kullanıcının kendi kotası).
/// </summary>
public sealed class GeminiAuthHandler : DelegatingHandler
{
    private const string ApiKeyHeader = "x-goog-api-key";
    private readonly IApiKeyProvider _keyProvider;

    public GeminiAuthHandler(IApiKeyProvider keyProvider) => _keyProvider = keyProvider;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var lease = _keyProvider.Acquire();

        // Header'ı bu istek için set et (önceki değeri varsa temizle — bağlantı yeniden kullanılabilir).
        request.Headers.Remove(ApiKeyHeader);
        request.Headers.Add(ApiKeyHeader, lease.Key);

        var response = await base.SendAsync(request, cancellationToken);

        // BYOK anahtarı reddedildi (400/401/403) → kullanıcıya anlaşılır hata (jenerik 500 yerine).
        // Yalnızca kullanıcının kendi anahtarı için: havuz anahtarında bu bizim yapılandırma sorunumuz,
        // orada jenerik akış devam etsin (kullanıcıya "anahtarınız geçersiz" demek yanıltıcı olur).
        if (lease.IsByok && response.StatusCode is
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new InvalidApiKeyException();
        }

        // Kota (429) veya geçici aşırı yük (503) → bu havuz anahtarını cooldown'a al.
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            _keyProvider.ReportFailure(lease);

        return response;
    }
}
