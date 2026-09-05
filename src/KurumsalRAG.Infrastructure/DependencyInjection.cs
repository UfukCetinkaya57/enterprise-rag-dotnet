using System.Net;
using System.Net.Http.Headers;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Agents;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Hosting;
using KurumsalRAG.Infrastructure.Ingestion;
using KurumsalRAG.Infrastructure.Persistence;
using KurumsalRAG.Infrastructure.Providers.Gemini;
using KurumsalRAG.Infrastructure.Providers.OpenAi;
using KurumsalRAG.Infrastructure.Reranking;
using KurumsalRAG.Infrastructure.Security;
using KurumsalRAG.Infrastructure.Services;
using KurumsalRAG.Infrastructure.Services.Defaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.Extensions.Http;

namespace KurumsalRAG.Infrastructure;

/// <summary>
/// Infrastructure kompozisyon kökü. Port'ları somut adapter'lara bağlar.
/// Provider'ı değiştirmek = burada tek satır değiştirmek.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Konfigürasyon (appsettings + .env) ---
        services.Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        services.Configure<DemoOptions>(configuration.GetSection(DemoOptions.SectionName));

        var providerOptions = configuration.GetSection(AiProviderOptions.SectionName).Get<AiProviderOptions>()
            ?? new AiProviderOptions();

        // ChunkingOptions'ı doğrudan enjekte edilebilir yap (TextChunker için).
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RagOptions>>().Value.Chunking);

        // --- PostgreSQL + pgvector veri kaynağı ---
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

            // 'vector' tip eşlemesinin (UseVector) çalışması için extension DB'de VAR olmalı.
            // DatabaseInitializer'dan önce burada garantiye alıyoruz (data source kurulmadan).
            EnsureVectorExtension(pg.ConnectionString);

            var builder = new NpgsqlDataSourceBuilder(pg.ConnectionString);
            builder.UseVector(); // pgvector tip eşlemesi
            return builder.Build();
        });

        // --- AI sağlayıcısı: provider-agnostik seçim (appsettings "Ai:Provider"). ---
        // Sağlayıcıyı değiştirmek = tek config satırı; iş mantığı hiç değişmez.
        if (providerOptions.IsGemini)
        {
            // Gemini: auth header'lı + 429 retry'lı typed HttpClient.
            services.AddHttpClient<IEmbeddingProvider, GeminiEmbeddingProvider>(ConfigureGeminiClient)
                .AddPolicyHandler(RetryPolicy());
            services.AddHttpClient<ILlmProvider, GeminiLlmProvider>(ConfigureGeminiClient)
                .AddPolicyHandler(RetryPolicy());
            services.AddHttpClient<IProviderHealthProbe, GeminiHealthProbe>();
        }
        else
        {
            // OpenAI (varsayılan): auth header'lı + 429 retry'lı typed HttpClient.
            services.AddHttpClient<IEmbeddingProvider, OpenAiEmbeddingProvider>(ConfigureOpenAiClient)
                .AddPolicyHandler(RetryPolicy());
            services.AddHttpClient<ILlmProvider, OpenAiLlmProvider>(ConfigureOpenAiClient)
                .AddPolicyHandler(RetryPolicy());
            services.AddHttpClient<IProviderHealthProbe, OpenAiHealthProbe>();
        }

        // --- Ingestion bileşenleri ---
        services.AddSingleton<PdfTextExtractor>();
        services.AddSingleton<TextChunker>();

        // --- Adapter'lar (port -> somut) ---
        services.AddScoped<IVectorStore, PgVectorStore>();
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IRagQueryService, RagQueryService>();
        services.AddScoped<IRerankDiagnostics, RerankDiagnosticsService>();

        // --- Demo zırhı adapter'ları (hepsi pgvector deposunu paylaşır) ---
        services.AddScoped<IResponseCache, PgResponseCache>();
        services.AddScoped<ITokenBudgetGuard, PgTokenBudgetStore>();
        services.AddScoped<ISessionQuota, PgSessionQuota>();
        // IP rate limiter: restart-dayanıklı (DB). In-memory FixedWindow yerine.
        services.AddScoped<IIpRateLimiter, PgIpRateLimiter>();

        // --- Arka plan servisleri: şema init (İLK) → seed ingest → TTL temizliği ---
        // Şema, seçili sağlayıcının embedding boyutuyla (OpenAI=1536, Gemini=768) oluşur.
        services.AddHostedService<DatabaseInitializer>();
        services.AddHostedService<SeedDocumentInitializer>();
        services.AddHostedService<DocumentTtlCleanupService>();

        // --- Reranker: config'ten (Rag:Retrieval:RerankerType).
        // "Hybrid" (cosine+keyword, LLM çağrısı YOK — free-tier dostu) veya "Llm" (kaliteli).
        // Cross-encoder / Cohere Rerank'e geçişte yine bu port'un arkası değişir. ---
        var rerankerType = configuration
            .GetSection(RagOptions.SectionName).GetSection("Retrieval")["RerankerType"] ?? "Llm";
        if (string.Equals(rerankerType, "Hybrid", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IReranker, HybridReranker>();
        else
            services.AddScoped<IReranker, LlmReranker>();

        // --- Faz 4: kural tabanlı prompt injection guard (NoOp'un yerine).
        // İleride LLM-based classifier bu portun arkasına takılabilir. ---
        services.AddSingleton<IPromptGuard, RuleBasedPromptGuard>();

        // --- Faz 3: Semantic Kernel agentic katman ---
        // KernelFactory ILlmProvider (scoped/HttpClient) alır → scoped olmalı.
        services.AddScoped<KernelFactory>();
        // Her istek için taze bir Kernel (SK Kernel'i hafif; scoped uygun).
        services.AddScoped(sp => sp.GetRequiredService<KernelFactory>().Create());
        services.AddScoped<RetrievalAgent>();
        services.AddScoped<AnswerAgent>();
        services.AddScoped<RagOrchestrator>();
        // Gerçek faithfulness değerlendirici NoOp'un yerine (SK tabanlı critic).
        services.AddScoped<IFaithfulnessEvaluator, FaithfulnessCheckerAgent>();
        services.AddScoped<IFaithfulnessDiagnostics, FaithfulnessDiagnosticsService>();
        services.AddScoped<IEvaluationDiagnostics, EvaluationDiagnosticsService>();

        return services;
    }

    /// <summary>
    /// 'vector' extension'ını DB'de garantiler (UseVector tip eşlemesinden ÖNCE gerekli).
    /// Container yeni başladıysa DB birkaç saniye gecikebilir — kısa retry ile bekler.
    /// </summary>
    private static void EnsureVectorExtension(string connectionString)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
                cmd.ExecuteNonQuery();
                return;
            }
            catch (NpgsqlException) when (attempt < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(2)); // DB henüz hazır değil, bekle
            }
        }
    }

    private static void ConfigureOpenAiClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);
        client.Timeout = TimeSpan.FromMinutes(2);
    }

    private static void ConfigureGeminiClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
        // BaseAddress sonuna "/" — relative path'ler ("models/...:generateContent") doğru çözülsün.
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("x-goog-api-key", options.ApiKey);
        client.Timeout = TimeSpan.FromMinutes(2);
    }

    /// <summary>429 (rate limit) ve geçici hatalar için üstel geri çekilmeli retry.</summary>
    private static IAsyncPolicy<HttpResponseMessage> RetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
