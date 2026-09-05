using System.Net.Http.Headers;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Providers.OpenAi;

/// <summary>
/// OpenAI erişilebilirlik kontrolü: /models uç noktasına GET atar. Bu uç TOKEN HARCAMAZ —
/// yalnızca API anahtarının geçerli ve ağın erişilebilir olduğunu doğrular. Derin health için.
/// </summary>
public sealed class OpenAiHealthProbe : IProviderHealthProbe
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiHealthProbe(HttpClient http, IOptions<OpenAiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
