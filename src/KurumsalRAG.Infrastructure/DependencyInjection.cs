using System.Net;
using System.Net.Http.Headers;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Agents;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Ingestion;
using KurumsalRAG.Infrastructure.Persistence;
using KurumsalRAG.Infrastructure.Providers.OpenAi;
using KurumsalRAG.Infrastructure.Reranking;
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
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));

        // ChunkingOptions'ı doğrudan enjekte edilebilir yap (TextChunker için).
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RagOptions>>().Value.Chunking);

        // --- PostgreSQL + pgvector veri kaynağı ---
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            var builder = new NpgsqlDataSourceBuilder(pg.ConnectionString);
            builder.UseVector(); // pgvector tip eşlemesi
            return builder.Build();
        });

        // --- OpenAI provider'ları: auth header'lı + 429 retry'lı typed HttpClient ---
        services.AddHttpClient<IEmbeddingProvider, OpenAiEmbeddingProvider>(ConfigureOpenAiClient)
            .AddPolicyHandler(RetryPolicy());
        services.AddHttpClient<ILlmProvider, OpenAiLlmProvider>(ConfigureOpenAiClient)
            .AddPolicyHandler(RetryPolicy());

        // --- Ingestion bileşenleri ---
        services.AddSingleton<PdfTextExtractor>();
        services.AddSingleton<TextChunker>();

        // --- Adapter'lar (port -> somut) ---
        services.AddScoped<IVectorStore, PgVectorStore>();
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IRagQueryService, RagQueryService>();
        services.AddScoped<IRerankDiagnostics, RerankDiagnosticsService>();

        // --- Reranker: Faz 2'de LLM tabanlı (PassThroughReranker yerine).
        // Cross-encoder / Cohere Rerank'e geçişte sadece bu satır değişir. ---
        services.AddScoped<IReranker, LlmReranker>();

        // --- Faz 1 varsayılanı (Faz 4'te RuleBasedPromptGuard ile değişecek) ---
        services.AddSingleton<IPromptGuard, NoOpPromptGuard>();

        // --- Faz 3: Semantic Kernel agentic katman ---
        services.AddSingleton<KernelFactory>();
        // Her istek için taze bir Kernel (SK Kernel'i hafif; scoped uygun).
        services.AddScoped(sp => sp.GetRequiredService<KernelFactory>().Create());
        services.AddScoped<RetrievalAgent>();
        services.AddScoped<AnswerAgent>();
        services.AddScoped<RagOrchestrator>();
        // Gerçek faithfulness değerlendirici NoOp'un yerine (SK tabanlı critic).
        services.AddScoped<IFaithfulnessEvaluator, FaithfulnessCheckerAgent>();
        services.AddScoped<IFaithfulnessDiagnostics, FaithfulnessDiagnosticsService>();

        return services;
    }

    private static void ConfigureOpenAiClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);
        client.Timeout = TimeSpan.FromMinutes(2);
    }

    /// <summary>429 (rate limit) ve geçici hatalar için üstel geri çekilmeli retry.</summary>
    private static IAsyncPolicy<HttpResponseMessage> RetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
