using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KurumsalRAG.Api.Middleware;

/// <summary>
/// Yakalanmayan hataları kullanıcı-dostu ProblemDetails'e çevirir; stack trace SIZDIRMAZ.
/// InvalidOperationException (iş kuralı ihlali) 422; diğerleri 500 (jenerik mesaj).
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Ayrıntı yalnızca sunucu log'unda kalır — kullanıcıya gitmez.
        _logger.LogError(exception, "İşlenmeyen hata: {Path}", httpContext.Request.Path);

        var (status, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Geçersiz istek."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.")
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}"
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
