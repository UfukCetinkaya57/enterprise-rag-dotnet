using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Infrastructure.Services.Defaults;

/// <summary>
/// Faz 1 varsayılanı: girdiyi olduğu gibi geçirir. Faz 4'te kural tabanlı
/// RuleBasedPromptGuard bunun yerine kayıtlanır.
/// </summary>
public sealed class NoOpPromptGuard : IPromptGuard
{
    public PromptGuardResult Inspect(string userInput) => PromptGuardResult.Clean(userInput);
}
