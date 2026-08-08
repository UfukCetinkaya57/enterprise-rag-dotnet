using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Hosting;

/// <summary>
/// Startup'ta bir kez örnek (seed) dokümanı ingest eder (session_id = 'seed').
/// Zaten yüklüyse atlar (idempotent). Böylece demo kullanıcısı hiçbir şey yüklemeden
/// hemen soru sorabilir.
/// </summary>
public sealed class SeedDocumentInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly DemoOptions _options;
    private readonly ILogger<SeedDocumentInitializer> _logger;

    public SeedDocumentInitializer(
        IServiceScopeFactory scopeFactory,
        NpgsqlDataSource dataSource,
        IOptions<DemoOptions> options,
        ILogger<SeedDocumentInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _dataSource = dataSource;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.SeedEnabled)
            return;

        try
        {
            if (await SeedExistsAsync(cancellationToken))
            {
                _logger.LogInformation("Seed doküman zaten mevcut; ingest atlandı.");
                return;
            }

            var path = Path.Combine(AppContext.BaseDirectory, _options.SeedFilePath);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Seed PDF bulunamadı: {Path}. Seed ingest atlandı.", path);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            await using var stream = new MemoryStream(bytes);

            using var scope = _scopeFactory.CreateScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<IDocumentIngestionService>();
            var result = await ingestion.IngestPdfAsync(
                stream, Path.GetFileName(path), _options.SeedSessionId, bytes.LongLength, cancellationToken);

            _logger.LogInformation("Seed doküman ingest edildi: {Chunks} chunk.", result.ChunkCount);
        }
        catch (Exception ex)
        {
            // Seed hatası uygulamayı düşürmesin (DB henüz hazır değilse vb.).
            _logger.LogError(ex, "Seed ingest başarısız; demo seed'siz devam ediyor.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> SeedExistsAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT COUNT(*) FROM chunks WHERE session_id = @seed");
        cmd.Parameters.AddWithValue("seed", _options.SeedSessionId);
        var count = await cmd.ExecuteScalarAsync(cancellationToken) as long? ?? 0L;
        return count > 0;
    }
}
