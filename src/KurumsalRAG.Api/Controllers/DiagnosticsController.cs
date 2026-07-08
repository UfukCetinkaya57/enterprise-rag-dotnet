using KurumsalRAG.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace KurumsalRAG.Api.Controllers;

/// <summary>Demo/gözlem uçları. Rerank'in retrieval kalitesine katkısını görünür kılar.</summary>
[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IRerankDiagnostics _rerankDiagnostics;
    private readonly IFaithfulnessDiagnostics _faithfulnessDiagnostics;

    public DiagnosticsController(
        IRerankDiagnostics rerankDiagnostics,
        IFaithfulnessDiagnostics faithfulnessDiagnostics)
    {
        _rerankDiagnostics = rerankDiagnostics;
        _faithfulnessDiagnostics = faithfulnessDiagnostics;
    }

    /// <summary>Bir sorgu için rerank öncesi (cosine) ve sonrası sıralamayı yan yana döner.</summary>
    [HttpGet("rerank")]
    public async Task<IActionResult> Rerank([FromQuery] string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
            return BadRequest("Soru boş olamaz.");

        var comparison = await _rerankDiagnostics.CompareAsync(question, cancellationToken);
        return Ok(comparison);
    }

    /// <summary>
    /// Tam agentic akışı (retrieve → answer → check → reflection) çalıştırır; cevabı,
    /// groundedness skorunu ve desteklenmeyen iddia listesini döner.
    /// </summary>
    [HttpGet("faithfulness")]
    public async Task<IActionResult> Faithfulness([FromQuery] string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
            return BadRequest("Soru boş olamaz.");

        var report = await _faithfulnessDiagnostics.InspectAsync(question, cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Denetçiyi izole test eder: elle kurgulanmış bir cevabı, sorunun context'ine karşı
    /// denetime sokar. Uydurma yakalamayı kanıtlamak için (ör. "çalışanlara şirket aracı verilir").
    /// </summary>
    [HttpPost("faithfulness/check")]
    public async Task<IActionResult> CheckClaim(
        [FromBody] CheckClaimRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest("Soru ve cevap boş olamaz.");

        var report = await _faithfulnessDiagnostics.CheckClaimAsync(
            request.Question, request.Answer, cancellationToken);
        return Ok(report);
    }
}

public sealed record CheckClaimRequest(string Question, string Answer);
