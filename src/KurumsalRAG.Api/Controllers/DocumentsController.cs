using KurumsalRAG.Api.Session;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Api.Controllers;

// IP upload kotası yol bazlı IpRateLimitMiddleware ile uygulanır (DB, restart-dayanıklı).
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    // PDF magic byte: "%PDF-"
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly IDocumentIngestionService _ingestion;
    private readonly ISessionQuota _quota;
    private readonly ISessionAccessor _session;
    private readonly UploadOptions _upload;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentIngestionService ingestion,
        ISessionQuota quota,
        ISessionAccessor session,
        IOptions<DemoOptions> demo,
        ILogger<DocumentsController> logger)
    {
        _ingestion = ingestion;
        _quota = quota;
        _session = session;
        _upload = demo.Value.Upload;
        _logger = logger;
    }

    /// <summary>Bir PDF yükler: sertleştirme → kota → chunk + embed + store (session'a bağlı).</summary>
    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)] // 12 MB hard cap (config 10 MB + pay)
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        // 1) Temel varlık + boyut
        if (file is null || file.Length == 0)
            return BadRequest("Dosya boş.");

        if (file.Length > _upload.MaxBytes)
            return BadRequest($"Dosya çok büyük. En fazla {_upload.MaxBytes / (1024 * 1024)} MB.");

        // 2) Content-type + uzantı
        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Sadece PDF (application/pdf) desteklenir.");

        await using var stream = file.OpenReadStream();

        // 3) Magic byte kontrolü (%PDF-) — uzantı yalanını engeller
        if (!await HasPdfMagicAsync(stream, cancellationToken))
            return BadRequest("Dosya geçerli bir PDF değil (imza doğrulanamadı).");

        // 4) Session kotası (doküman sayısı + toplam boyut)
        var quota = await _quota.CanUploadAsync(_session.SessionId, file.Length, cancellationToken);
        if (!quota.Allowed)
            return StatusCode(StatusCodes.Status409Conflict, quota.Reason);

        // 5) Ingest (sayfa sınırı ingestion içinde uygulanır)
        try
        {
            var result = await _ingestion.IngestPdfAsync(
                stream, file.FileName, _session.SessionId, file.Length, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Kullanıcı-dostu mesaj (sayfa sınırı, metin çıkmadı vb.) — stack trace yok.
            _logger.LogWarning(ex, "Ingestion reddedildi: {File}", file.FileName);
            return UnprocessableEntity(ex.Message);
        }
    }

    private static async Task<bool> HasPdfMagicAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[PdfMagic.Length];
        var read = await stream.ReadAsync(header, cancellationToken);
        stream.Position = 0; // ingestion baştan okusun
        return read == PdfMagic.Length && header.AsSpan().SequenceEqual(PdfMagic);
    }
}
