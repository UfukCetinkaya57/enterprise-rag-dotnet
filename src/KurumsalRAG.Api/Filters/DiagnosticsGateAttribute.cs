using KurumsalRAG.Application.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Api.Filters;

/// <summary>
/// Diagnostics uçlarını Demo:DiagnosticsEnabled=false iken kapatır: uç sanki YOKMUŞ gibi
/// 404 döner (varlığını sızdırmamak için 403 değil 404). Nginx deny'i ile çift katman.
/// </summary>
public sealed class DiagnosticsGateAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<DemoOptions>>().Value;

        if (!options.DiagnosticsEnabled)
            context.Result = new NotFoundResult();
    }
}
