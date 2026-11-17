using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Platform;

/// <summary>Turns unhandled exceptions into an RFC 9457 ProblemDetails 500 without leaking details.</summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            },
        });
    }
}
