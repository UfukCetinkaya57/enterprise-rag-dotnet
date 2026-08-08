using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Tests;

public sealed class RagQueryServiceTests
{
    private static RagQueryService Build(
        out FakeLlm llm, out FakeVectorStore store, out FakeCache cache, out FakeBudget budget,
        CachedAnswer? cacheHit = null, bool demoEnabled = true, bool withinBudget = true,
        string sessionId = "user-1")
    {
        llm = new FakeLlm();
        store = new FakeVectorStore();
        cache = new FakeCache(cacheHit);
        budget = new FakeBudget(withinBudget);

        var rag = Options.Create(new RagOptions());
        var demo = Options.Create(new DemoOptions { Enabled = demoEnabled, SeedSessionId = "seed" });

        return new RagQueryService(
            new FakePromptGuard(), new FakeEmbedding(), store, new FakeReranker(),
            llm, new FakeFaithfulness(), cache, budget, new FakeSession(sessionId),
            rag, demo, NullLogger<RagQueryService>.Instance);
    }

    [Fact]
    public async Task Normal_flow_calls_llm_caches_and_filters_by_session_plus_seed()
    {
        var svc = Build(out var llm, out var store, out var cache, out var budget);

        var answer = await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.Equal(AnswerType.Normal, answer.Type);
        Assert.Equal(1, llm.CompleteCalls);
        Assert.True(cache.SetCalled);                       // başarılı cevap cache'lendi
        Assert.True(budget.RecordedTokens > 0);             // token kullanımı kaydedildi
        Assert.Equal(["user-1", "seed"], store.LastAllowedSessions);  // session izolasyonu
    }

    [Fact]
    public async Task Cache_hit_short_circuits_llm()
    {
        var hit = new CachedAnswer("önbellek cevabı", [], 0.95);
        var svc = Build(out var llm, out _, out _, out _, cacheHit: hit);

        var answer = await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.Equal(AnswerType.Cached, answer.Type);
        Assert.True(answer.Cached);
        Assert.Equal("önbellek cevabı", answer.Answer);
        Assert.Equal(0, llm.CompleteCalls);                 // LLM hiç çağrılmadı
    }

    [Fact]
    public async Task Kill_switch_returns_limited_without_llm()
    {
        var svc = Build(out var llm, out _, out _, out _, demoEnabled: false);

        var answer = await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.Equal(AnswerType.Limited, answer.Type);
        Assert.True(answer.Limited);
        Assert.Equal(0, llm.CompleteCalls);
    }

    [Fact]
    public async Task Budget_exceeded_returns_limited_without_llm()
    {
        var svc = Build(out var llm, out _, out _, out _, withinBudget: false);

        var answer = await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.Equal(AnswerType.Limited, answer.Type);
        Assert.Equal(0, llm.CompleteCalls);
    }

    [Fact]
    public async Task Own_session_equal_to_seed_is_deduped()
    {
        var svc = Build(out _, out var store, out _, out _, sessionId: "seed");

        await svc.AskAsync("soru");

        Assert.Equal(["seed"], store.LastAllowedSessions);
    }
}
