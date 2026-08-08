using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Sessions;

namespace KurumsalRAG.Tests;

public sealed class SessionScopeTests
{
    [Fact]
    public void Allowed_returns_self_and_seed()
    {
        var scope = SessionScope.Allowed("user-123", "seed");
        Assert.Equal(["user-123", "seed"], scope);
    }

    [Fact]
    public void Allowed_dedupes_when_self_is_seed()
    {
        var scope = SessionScope.Allowed("seed", "seed");
        Assert.Equal(["seed"], scope);
    }

    [Fact]
    public void Allowed_never_leaks_other_sessions()
    {
        var scope = SessionScope.Allowed("user-A", "seed");
        Assert.DoesNotContain("user-B", scope);
    }
}

public sealed class BudgetStatusTests
{
    [Fact]
    public void Within_budget_when_used_below_limit()
        => Assert.True(BudgetStatus.From(usedToday: 199_000, dailyBudget: 200_000).WithinBudget);

    [Fact]
    public void Not_within_budget_when_used_equals_limit()
        => Assert.False(BudgetStatus.From(usedToday: 200_000, dailyBudget: 200_000).WithinBudget);

    [Fact]
    public void Not_within_budget_when_used_over_limit()
        => Assert.False(BudgetStatus.From(usedToday: 250_000, dailyBudget: 200_000).WithinBudget);
}
