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
        string sessionId = "user-1",
        bool conversationEnabled = false, FakeConversationStore? conversation = null,
        FakeQueryRewriter? rewriter = null)
    {
        llm = new FakeLlm();
        store = new FakeVectorStore();
        cache = new FakeCache(cacheHit);
        budget = new FakeBudget(withinBudget);

        var rag = Options.Create(new RagOptions
        {
            Conversation = new ConversationOptions { Enabled = conversationEnabled, HistoryWindow = 5 }
        });
        var demo = Options.Create(new DemoOptions { Enabled = demoEnabled, SeedSessionId = "seed" });

        return new RagQueryService(
            new FakePromptGuard(), new FakeEmbedding(), store, new FakeReranker(),
            llm, new FakeFaithfulness(), cache, budget, new FakeSession(sessionId),
            conversation ?? new FakeConversationStore(), rewriter ?? new FakeQueryRewriter(),
            new FakeUserLlmResolver(), rag, demo, NullLogger<RagQueryService>.Instance);
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

    // --- Multi-turn konuşma hafızası ---

    [Fact]
    public async Task Multiturn_kapaliyken_tur_kaydedilmez_ve_rewrite_yapilmaz()
    {
        var conv = new FakeConversationStore(new ConversationTurn("önceki", "cevap"));
        var rewriter = new FakeQueryRewriter();
        // conversationEnabled: false → geçmiş yüklenmez, rewrite/append çağrılmaz.
        var svc = Build(out _, out _, out _, out _, conversationEnabled: false,
            conversation: conv, rewriter: rewriter);

        await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.False(conv.AppendCalled);            // tur kaydedilmedi
        Assert.Null(rewriter.LastRewrittenFrom);    // rewrite yapılmadı (geçmiş yüklenmedi)
    }

    [Fact]
    public async Task Multiturn_acikken_basarili_tur_gecmise_kaydedilir()
    {
        var conv = new FakeConversationStore();
        var svc = Build(out var llm, out _, out _, out _, conversationEnabled: true, conversation: conv);

        await svc.AskAsync("Yıllık izin kaç gün?");

        Assert.Equal(1, llm.CompleteCalls);
        Assert.True(conv.AppendCalled);             // tur geçmişe yazıldı
    }

    [Fact]
    public async Task Multiturn_gecmis_varsa_takip_sorusu_yeniden_yazilir()
    {
        // Geçmişte bir tur var → rewriter devreye girmeli (retrieval bağımsız soruyla).
        var conv = new FakeConversationStore(new ConversationTurn("Yıllık izin kaç gün?", "20 gün"));
        var rewriter = new FakeQueryRewriter();
        var svc = Build(out _, out _, out _, out _, conversationEnabled: true,
            conversation: conv, rewriter: rewriter);

        await svc.AskAsync("peki ya 5 yılını dolduranlar?");

        Assert.Equal("peki ya 5 yılını dolduranlar?", rewriter.LastRewrittenFrom); // rewrite çağrıldı
    }

    [Fact]
    public async Task Multiturn_refusal_cevabi_gecmise_yazilmaz()
    {
        // LLM refusal metni dönerse o tur geçmişe kaydedilmemeli (rewrite'ı zehirlememesi için).
        var conv = new FakeConversationStore();
        var llm = new FakeLlm { Response = RagPromptBuilder.RefusalText };
        var rag = Options.Create(new RagOptions
        {
            Conversation = new ConversationOptions { Enabled = true, HistoryWindow = 5 }
        });
        var demo = Options.Create(new DemoOptions { Enabled = true, SeedSessionId = "seed" });
        var svc = new RagQueryService(
            new FakePromptGuard(), new FakeEmbedding(), new FakeVectorStore(), new FakeReranker(),
            llm, new FakeFaithfulness(), new FakeCache(null), new FakeBudget(), new FakeSession("u"),
            conv, new FakeQueryRewriter(), new FakeUserLlmResolver(), rag, demo,
            NullLogger<RagQueryService>.Instance);

        await svc.AskAsync("alakasız soru");

        Assert.False(conv.AppendCalled);    // refusal → geçmişe yazılmadı
    }

    [Fact]
    public async Task Multiturn_gecmis_varsa_cache_atlanir()
    {
        // Cache hit olsa BİLE geçmiş varken cache atlanmalı (takip cevabı bağlama bağlı) → LLM çağrılır.
        var hit = new CachedAnswer("önbellek cevabı", [], 0.95);
        var conv = new FakeConversationStore(new ConversationTurn("önceki soru", "önceki cevap"));
        var svc = Build(out var llm, out _, out _, out _, cacheHit: hit,
            conversationEnabled: true, conversation: conv);

        var answer = await svc.AskAsync("takip sorusu");

        Assert.Equal(AnswerType.Normal, answer.Type);   // cache'ten DEĞİL
        Assert.Equal(1, llm.CompleteCalls);             // LLM gerçekten çağrıldı
    }
}
