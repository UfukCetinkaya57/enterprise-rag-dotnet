using KurumsalRAG.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace KurumsalRAG.Api.Controllers;

/// <summary>Demo/gözlem uçları. Rerank'in retrieval kalitesine katkısını görünür kılar.</summary>
[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IRerankDiagnostics _diagnostics;

    public DiagnosticsController(IRerankDiagnostics diagnostics) => _diagnostics = diagnostics;

    /// <summary>Bir sorgu için rerank öncesi (cosine) ve sonrası sıralamayı yan yana döner.</summary>
    [HttpGet("rerank")]
    public async Task<IActionResult> Rerank([FromQuery] string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
            return BadRequest("Soru boş olamaz.");

        var comparison = await _diagnostics.CompareAsync(question, cancellationToken);
        return Ok(comparison);
    }
}
