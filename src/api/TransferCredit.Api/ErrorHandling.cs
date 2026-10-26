using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TransferCredit.Api.Services;

namespace TransferCredit.Api;

/// <summary>Maps domain exceptions to RFC 7807 problem responses.</summary>
public class AppExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Validation failed"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ForbiddenActionException => (StatusCodes.Status403Forbidden, "Not allowed"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            _ => (0, "")
        };
        if (status == 0) return false;

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = exception.Message }
        });
    }
}
