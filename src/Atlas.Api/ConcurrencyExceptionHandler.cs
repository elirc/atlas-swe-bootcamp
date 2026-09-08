using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Atlas.Infrastructure;

namespace Atlas.Api;

/// <summary>
/// Turns optimistic-concurrency failures (a stale Version token on save) into a
/// 409 ProblemDetails instead of a 500, telling the caller to reload and retry.
/// </summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!DatabaseConflicts.IsWriteConflict(exception))
        {
            return false;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Concurrency conflict",
            detail: "The resource was modified by another request. Reload it and retry.",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
            }).ExecuteAsync(httpContext);
        return true;
    }
}
