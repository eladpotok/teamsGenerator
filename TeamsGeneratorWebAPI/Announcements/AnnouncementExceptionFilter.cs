using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TeamsGeneratorWebAPI.Announcements;

public sealed class AnnouncementExceptionFilter(ILogger<AnnouncementExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var failure = context.Exception switch
        {
            AnnouncementException ex => (ex.Status, ex.Code),
            RequestFailedException or IOException or Newtonsoft.Json.JsonException => (503, "announcements_storage_unavailable"),
            _ => (0, "")
        };
        if (failure.Item1 == 0) return;
        if (failure.Item1 >= 500) logger.LogWarning("Account announcements unavailable: {Category}.", failure.Item2);
        context.Result = new ObjectResult(new { code = failure.Item2 }) { StatusCode = failure.Item1 };
        context.ExceptionHandled = true;
    }
}
