using System.Text;
using System.Text.Json;
using KurumsalRAG.Api.RateLimiting;
using KurumsalRAG.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KurumsalRAG.Api.Controllers;

[ApiController]
[Route("api/chat")]
[EnableRateLimiting(RateLimitPolicies.Queries)]
public sealed class ChatController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IRagQueryService _rag;

    public ChatController(IRagQueryService rag) => _rag = rag;

    /// <summary>Non-stream: soruya kaynak-atıflı tek JSON cevap döner (cached/limited dahil).</summary>
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Soru boş olamaz.");

        var answer = await _rag.AskAsync(request.Question, cancellationToken);
        return Ok(answer);
    }

    /// <summary>SSE: status → token'lar → meta (kaynak + groundedness) → done.</summary>
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

        await foreach (var chunk in _rag.StreamAsync(question, cancellationToken))
        {
            switch (chunk.Kind)
            {
                case "status":
                    await WriteEventAsync("status",
                        JsonSerializer.Serialize(new { type = chunk.Token }, Json), cancellationToken);
                    break;
                case "token":
                    await WriteEventAsync("token", chunk.Token ?? string.Empty, cancellationToken);
                    break;
                case "final" when chunk.Final is not null:
                    await WriteEventAsync("meta", JsonSerializer.Serialize(new
                    {
                        type = chunk.Final.Type.ToString(),
                        cached = chunk.Final.Cached,
                        limited = chunk.Final.Limited,
                        sources = chunk.Final.Sources,
                        faithfulnessScore = chunk.Final.Observability.FaithfulnessScore
                    }, Json), cancellationToken);
                    break;
            }
        }

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
