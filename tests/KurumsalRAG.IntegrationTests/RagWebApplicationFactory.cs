using System.Security.Cryptography;
using System.Text;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace KurumsalRAG.IntegrationTests;

/// <summary>
/// API'yi gerçek pgvector'e (Testcontainers) karşı, ama sahte LLM/embedding ile ayağa kaldırır.
/// Böylece retrieval/session-izolasyonu/cache GERÇEK Postgres'te test edilir; dış sağlayıcıya
/// (Gemini) hiç gidilmez (yavaş/kotalı/kırılgan). Sağlayıcı seçimi Gemini kalır (768 boyut).
/// </summary>
public sealed class RagWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("kurumsalrag_it")
        .WithUsername("rag")
        .WithPassword("ragpass")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production"); // diagnostics kapalı, gerçekçi prod davranışı

        // Bağlantı string'ini DOĞRUDAN config'e enjekte et (Program.cs'in env-okuma sırasına
        // güvenmeden). En son eklenen kaynak kazanır → test container'ının portu geçerli olur.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] =
                    $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};" +
                    "Database=kurumsalrag_it;Username=rag;Password=ragpass",
                ["Ai:Provider"] = "Gemini",     // embedding 768; gerçek çağrı fake ile değişecek
                ["Gemini:ApiKey"] = "test-key-fake"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Gerçek HTTP tabanlı sağlayıcıları sahtelerle değiştir (dışarı çağrı YOK).
            services.RemoveAll<IEmbeddingProvider>();
            services.RemoveAll<ILlmProvider>();
            services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
            services.AddSingleton<ILlmProvider, FakeLlmProvider>();
        });
    }
}

/// <summary>
/// Deterministik, dış-bağımlılıksız embedding. Aynı metin → aynı vektör; benzer metin → yakın vektör
/// (kelime hash'lerini 768 boyuta dağıtır). Retrieval'ın anlamlı çalışmasına yetecek kadar tutarlı.
/// </summary>
internal sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToArray());

    private static float[] Embed(string text)
    {
        // Kelime bazlı bag-of-words vektör: her kelimeyi bir boyuta hash'le. Ortak kelimeler →
        // örtüşen boyutlar → yüksek benzerlik. Sonra normalize (kosinüs için).
        var vec = new float[768];
        foreach (var word in text.ToLowerInvariant().Split(
            [' ', '\n', '\t', '.', ',', '?', '!', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(word)), 0);
            vec[hash % 768] += 1f;
        }
        var norm = MathF.Sqrt(vec.Sum(v => v * v));
        if (norm > 0)
            for (var i = 0; i < vec.Length; i++)
                vec[i] /= norm;
        return vec;
    }
}

/// <summary>
/// Sahte LLM: context'teki ilk chunk'ı özet gibi döndürür ([chunk:1] atıfıyla). Deterministik,
/// kotasız. Faithfulness prod'da kapalı olduğu için ek çağrı olmaz.
/// </summary>
internal sealed class FakeLlmProvider : ILlmProvider
{
    public Task<LlmCompletion> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        // Son user mesajı "SORU: ..." — context system mesajında. Basit, deterministik bir cevap üret.
        var content = "Test cevabı: dokümana göre yanıt [chunk:1].";
        return Task.FromResult(new LlmCompletion(content, new TokenUsage(50, 15)));
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Test ";
        yield return "cevabı [chunk:1].";
        await Task.CompletedTask;
    }
}
