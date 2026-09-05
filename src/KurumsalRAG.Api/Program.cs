using KurumsalRAG.Api.Health;
using KurumsalRAG.Api.Middleware;
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
// ForwardLimit=1: yalnızca EN YAKIN proxy'ye (Nginx) güven — spoofing'i sınırlar.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    // Tek, bilinen reverse proxy (Nginx, container ağı) güvenilir kabul edilir.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Statik demo UI (wwwroot/index.html) kök yolda.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<SessionCookieMiddleware>();
// DB tabanlı (restart-dayanıklı) IP rate limiting — session'dan sonra, controller'dan önce.
app.UseMiddleware<IpRateLimitMiddleware>();

app.MapControllers();

// Health: sığ (DB ping) public; derin (provider dahil) yalnızca localhost + ?deep=true.
app.MapHealthEndpoint();

app.Run();

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
