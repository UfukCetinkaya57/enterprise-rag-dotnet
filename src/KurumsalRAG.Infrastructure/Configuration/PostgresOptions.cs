namespace KurumsalRAG.Infrastructure.Configuration;

/// <summary>PostgreSQL bağlantı konfigürasyonu. Bağlantı bileşenleri .env'den gelir.</summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string ConnectionString { get; set; } = string.Empty;
}
