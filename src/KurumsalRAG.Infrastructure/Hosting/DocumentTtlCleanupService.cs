using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Hosting;

/// <summary>
/// Saatte bir çalışan temizleyici: seed hariç, DocumentTtlHours'tan eski dokümanları
/// (ve cascade ile chunk'larını) siler; ayrıca CacheTtlHours'tan eski konuşma turlarını
/// temizler. Public demoda veri birikmesini önler.
/// </summary>
public sealed class DocumentTtlCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DemoOptions _options;
    private readonly ILogger<DocumentTtlCleanupService> _logger;

    public DocumentTtlCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<DemoOptions> options,
        ILogger<DocumentTtlCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup'ta bir kez, sonra saatte bir.
        while (!stoppingToken.IsCancellationRequested)
        {
            await PurgeOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.DocumentTtlHours);

            var deleted = await store.PurgeExpiredAsync(cutoff, _options.SeedSessionId, cancellationToken);
            if (deleted > 0)
                _logger.LogInformation("TTL temizliği: {Count} süresi dolmuş doküman silindi.", deleted);

            // Konuşma turları: cache TTL'inden eski olanları fiziksel olarak sil (demo veri birikimi).
            var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            var convCutoff = DateTimeOffset.UtcNow.AddHours(-_options.Cache.TtlHours);
            await using var cmd = dataSource.CreateCommand(
                "DELETE FROM conversation_turns WHERE created_at < @cutoff");
            cmd.Parameters.AddWithValue("cutoff", convCutoff);
            var convDeleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (convDeleted > 0)
                _logger.LogInformation("TTL temizliği: {Count} süresi dolmuş konuşma turu silindi.", convDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTL temizliği başarısız (bir sonraki turda tekrar denenecek).");
        }
    }
}
