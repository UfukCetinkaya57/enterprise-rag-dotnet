using KurumsalRAG.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Hosting;

/// <summary>
/// Startup'ta şemayı (idempotent) oluşturur. embedding kolonunun BOYUTU seçili sağlayıcının
/// embedding boyutundan gelir (OpenAI=1536, Gemini=768) — provider-agnostik mimari şema
/// katmanında da korunur. Bu, sabit vector(1536) içeren db/init.sql'in yerini alır; Docker
/// init script'ine bağımlılığı kaldırır (daha taşınabilir).
///
/// Not: Mevcut şema FARKLI bir embedding boyutuyla oluşturulmuşsa (ör. 1536 iken 768'e geçiş),
/// boyut uyuşmazlığı loglanır — temiz volume gerekir (deploy'da down -v).
/// </summary>
public sealed class DatabaseInitializer : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        NpgsqlDataSource dataSource,
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseInitializer> logger)
    {
        _dataSource = dataSource;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Seçili sağlayıcının embedding boyutunu al (DI'da hangi IEmbeddingProvider kayıtlıysa).
        using var scope = _scopeFactory.CreateScope();
        var dim = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>().Dimensions;

        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS documents (
                id          UUID PRIMARY KEY,
                session_id  TEXT NOT NULL,
                file_name   TEXT NOT NULL,
                file_bytes  BIGINT NOT NULL DEFAULT 0,
                uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                chunk_count INT NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_documents_session_id ON documents (session_id);

            CREATE TABLE IF NOT EXISTS chunks (
                id           UUID PRIMARY KEY,
                document_id  UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                session_id   TEXT NOT NULL,
                content      TEXT NOT NULL,
                chunk_index  INT NOT NULL,
                token_count  INT NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                embedding    vector({dim}) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw
                ON chunks USING hnsw (embedding vector_cosine_ops);
            CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks (document_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_session_id  ON chunks (session_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_created_at  ON chunks (created_at);

            CREATE TABLE IF NOT EXISTS response_cache (
                session_id    TEXT NOT NULL,
                question_hash TEXT NOT NULL,
                question      TEXT NOT NULL,
                answer_json   JSONB NOT NULL,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (session_id, question_hash)
            );
            CREATE INDEX IF NOT EXISTS idx_response_cache_created_at ON response_cache (created_at);

            CREATE TABLE IF NOT EXISTS daily_usage (
                usage_date  DATE PRIMARY KEY,
                tokens_used BIGINT NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ip_usage (
                usage_date DATE NOT NULL,
                ip         TEXT NOT NULL,
                scope      TEXT NOT NULL,
                hits       INT  NOT NULL DEFAULT 0,
                PRIMARY KEY (usage_date, ip, scope)
            );
            CREATE INDEX IF NOT EXISTS idx_ip_usage_date ON ip_usage (usage_date);
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Şema hazır (embedding boyutu = {Dim}).", dim);

            // KRİTİK: 'vector' extension bu init'te oluşturuldu. NpgsqlDataSource tip haritası
            // uygulama başlarken (extension yokken) kurulduğu için 'vector' tipini bilmiyor.
            // Yeniden yükle ki Pgvector.Vector parametreleri yazılabilsin.
            await using (var conn = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                await conn.ReloadTypesAsync(cancellationToken);
            }

            await WarnOnDimensionMismatchAsync(dim, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şema oluşturma başarısız.");
            throw; // şema yoksa uygulama anlamlı çalışamaz
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Mevcut chunks.embedding boyutu beklenenle uyuşmuyorsa net uyarı ver.</summary>
    private async Task WarnOnDimensionMismatchAsync(int expected, CancellationToken cancellationToken)
    {
        const string q = """
            SELECT a.atttypmod
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname = 'chunks' AND a.attname = 'embedding'
            """;
        await using var cmd = _dataSource.CreateCommand(q);
        var typmod = await cmd.ExecuteScalarAsync(cancellationToken) as int?;
        // pgvector boyutu doğrudan atttypmod'da tutulur.
        if (typmod is int actual && actual > 0 && actual != expected)
        {
            _logger.LogWarning(
                "UYUMSUZLUK: chunks.embedding boyutu {Actual}, seçili sağlayıcı {Expected} bekliyor. " +
                "Sağlayıcı değişmiş olabilir — temiz volume gerekir (down -v).", actual, expected);
        }
    }
}
