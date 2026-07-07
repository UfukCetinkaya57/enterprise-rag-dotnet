using System.Text;
using KurumsalRAG.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace KurumsalRAG.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IRagQueryService _rag;

    public ChatController(IRagQueryService rag) => _rag = rag;

    /// <summary>Non-stream: soruya kaynak-atıflı tek JSON cevap döner.</summary>
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Soru boş olamaz.");

        var answer = await _rag.AskAsync(request.Question, cancellationToken);
        return Ok(answer);
    }

    /// <summary>SSE: cevabı token token stream eder (text/event-stream).</summary>
    [HttpGet("stream")]
    public async Task Stream([FromQuery] string question, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        if (string.IsNullOrWhiteSpace(question))
        {
            await WriteEventAsync("error", "Soru boş olamaz.", cancellationToken);
            return;
        }

        await foreach (var token in _rag.StreamAsync(question, cancellationToken))
            await WriteEventAsync("token", token, cancellationToken);

        await WriteEventAsync("done", "[DONE]", cancellationToken);
    }

    private async Task WriteEventAsync(string @event, string data, CancellationToken cancellationToken)
    {
        // SSE'de çok satırlı veriyi bozmamak için her satırı ayrı "data:" ile yaz.
        var sb = new StringBuilder();
        sb.Append("event: ").Append(@event).Append('\n');
        foreach (var line in data.Split('\n'))
            sb.Append("data: ").Append(line).Append('\n');
        sb.Append('\n');

        await Response.WriteAsync(sb.ToString(), cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}

public sealed record ChatRequest(string Question);
