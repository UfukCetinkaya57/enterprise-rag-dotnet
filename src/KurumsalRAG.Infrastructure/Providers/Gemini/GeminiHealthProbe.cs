using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.Gemini;

/// <summary>
/// Gemini erişilebilirlik kontrolü: /models uç noktasına GET atar. TOKEN HARCAMAZ —
/// yalnızca API anahtarının geçerli ve ağın erişilebilir olduğunu doğrular. Derin health için.
/// </summary>
public sealed class GeminiHealthProbe : IProviderHealthProbe
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiHealthProbe(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 'x-goog-api-key' GeminiAuthHandler tarafından eklenir (BYOK/havuz rotasyonu).
            // Burada absolute URL kullanıyoruz; anahtar seçimi handler'a bırakılır.
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/models");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await _http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
