using Atlas.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace Atlas.Api;

/// <summary>
/// Safety net: any DomainException that escapes an endpoint becomes a 409
/// ProblemDetails instead of a 500. Other exceptions fall through to the
/// default ProblemDetails handler.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        // Results.Problem uses the shared service, with a JSON fallback when
        // the client's Accept header is unsupported by its registered writers.
        await Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Domain rule violated",
            detail: domainException.Message,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
            }).ExecuteAsync(httpContext);
        return true;
    }
}
