using KurumsalRAG.Api.Health;
using KurumsalRAG.Infrastructure;
using KurumsalRAG.Infrastructure.Configuration;

// .env'i ortam değişkenlerine yükle (varsa). Secret'lar burada, appsettings'te değil.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// .env'den gelen secret'ları konfigürasyona bağla (appsettings'te sadece placeholder var).
builder.Configuration.AddEnvironmentVariables();
BindSecretsFromEnvironment(builder);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<RagHealthCheck>("rag");

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// --- Yardımcı: .env değişkenlerini tipli options bölümlerine köprüle ---
static void BindSecretsFromEnvironment(WebApplicationBuilder builder)
{
    var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrWhiteSpace(openAiKey))
        builder.Configuration[$"{OpenAiOptions.SectionName}:ApiKey"] = openAiKey;

    // Postgres bağlantı dizesini .env bileşenlerinden kur.
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
