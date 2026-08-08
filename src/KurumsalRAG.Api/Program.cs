using System.Threading.RateLimiting;
using KurumsalRAG.Api.Health;
using KurumsalRAG.Api.Middleware;
using KurumsalRAG.Api.RateLimiting;
using KurumsalRAG.Api.Session;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

// .env'i ortam değişkenlerine yükle (varsa). Secret'lar burada, appsettings'te değil.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// .env'den gelen secret'ları konfigürasyona bağla (appsettings'te sadece placeholder var).
builder.Configuration.AddEnvironmentVariables();
BindSecretsFromEnvironment(builder);

var demo = builder.Configuration.GetSection(DemoOptions.SectionName).Get<DemoOptions>() ?? new DemoOptions();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        // Enum'lar JSON'da string olarak görünsün (AnswerType: "Cached" gibi).
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<RagHealthCheck>("rag");

// Session: HttpContext üzerinden ISessionAccessor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISessionAccessor, HttpSessionAccessor>();

// Global hata yönetimi (ProblemDetails, stack trace sızdırmaz).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Nginx arkasında gerçek istemci IP'si için ForwardedHeaders.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Demo: reverse proxy güvenilir kabul edilir (aksi halde X-Forwarded-For yok sayılırdı).
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// IP başına rate limiting (.NET yerleşik): sorgu + upload için ayrı günlük pencereler.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Queries, ctx =>
        IpFixedWindow(ctx, demo.Ip.QueriesPerDay));
    options.AddPolicy(RateLimitPolicies.Uploads, ctx =>
        IpFixedWindow(ctx, demo.Ip.UploadsPerDay));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Statik demo UI (wwwroot/index.html) kök yolda.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<SessionCookieMiddleware>();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// IP bazlı sabit-pencere (24 saat) limiter partisyonu.
static RateLimitPartition<string> IpFixedWindow(HttpContext ctx, int permitPerDay)
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitPerDay,
        Window = TimeSpan.FromDays(1),
        QueueLimit = 0
    });
}

// --- Yardımcı: .env değişkenlerini tipli options bölümlerine köprüle ---
static void BindSecretsFromEnvironment(WebApplicationBuilder builder)
{
    var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrWhiteSpace(openAiKey))
        builder.Configuration[$"{OpenAiOptions.SectionName}:ApiKey"] = openAiKey;

    var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    var db = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "kurumsalrag";
    var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "rag";
    var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "ragpass";
    builder.Configuration[$"{PostgresOptions.SectionName}:ConnectionString"] =
        $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
}

// Integration test'lerin erişebilmesi için partial.
public partial class Program;
