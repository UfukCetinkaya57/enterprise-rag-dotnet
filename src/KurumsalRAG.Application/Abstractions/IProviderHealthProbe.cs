namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// LLM/embedding sağlayıcısının erişilebilirliğini (kimlik + ağ) token harcamadan doğrular.
/// Derin health kontrolünde kullanılır — pahalı üretim çağrısı YAPMAZ.
/// </summary>
public interface IProviderHealthProbe
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);
}
