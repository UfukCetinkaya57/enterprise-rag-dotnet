using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// Session başına doküman kotası bekçisi (ISessionQuota). Mevcut kullanım documents
/// tablosundan türetilir: doküman sayısı + toplam byte. Ayrı sayaç tablosu yok.
/// </summary>
public sealed class PgSessionQuota : ISessionQuota
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SessionQuotaOptions _options;

    public PgSessionQuota(NpgsqlDataSource dataSource, IOptions<DemoOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value.Session;
    }

    public async Task<QuotaCheck> CanUploadAsync(
        string sessionId, long newFileBytes, CancellationToken cancellationToken = default)
    {
        if (newFileBytes > _options.MaxTotalBytes)
            return QuotaCheck.Deny($"Dosya çok büyük. Session başına toplam sınır {_options.MaxTotalBytes / (1024 * 1024)} MB.");

        const string sql = """
            SELECT COUNT(*), COALESCE(SUM(file_bytes), 0)
            FROM documents WHERE session_id = @session
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var docCount = reader.GetInt64(0);
        var totalBytes = reader.GetInt64(1);

        if (docCount >= _options.MaxDocuments)
            return QuotaCheck.Deny($"Bu oturumda en fazla {_options.MaxDocuments} doküman yükleyebilirsiniz.");

        if (totalBytes + newFileBytes > _options.MaxTotalBytes)
            return QuotaCheck.Deny($"Oturum toplam boyut sınırı ({_options.MaxTotalBytes / (1024 * 1024)} MB) aşılıyor.");

        return QuotaCheck.Ok;
    }
}
