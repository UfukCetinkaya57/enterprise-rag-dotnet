using KurumsalRAG.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace KurumsalRAG.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentIngestionService ingestion, ILogger<DocumentsController> logger)
    {
        _ingestion = ingestion;
        _logger = logger;
    }

    /// <summary>Bir PDF yükler: chunk'lar, embed eder, pgvector'a yazar.</summary>
    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Dosya boş.");

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Sadece PDF desteklenir.");

        await using var stream = file.OpenReadStream();
        try
        {
            var result = await _ingestion.IngestPdfAsync(stream, file.FileName, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Ingestion başarısız: {File}", file.FileName);
            return UnprocessableEntity(ex.Message);
        }
    }
}
