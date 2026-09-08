using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class PayrollJobEndpoints
{
    public static IEndpointRouteBuilder MapPayrollJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payroll-jobs").RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapPost("/", async (CreatePayrollRunRequest request, ClaimsPrincipal user, AtlasDbContext db, DurableJobService jobs, CancellationToken ct) =>
        {
            if (PayrollLearningEndpoints.PeriodInput(request) is { } error) return error;
            var country = request.CountryCode!.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == country, ct)) return Results.NotFound();
            var result = await jobs.SubmitPayrollAsync(country, request.Year, request.Month, InvoiceLedgerEndpoints.Actor(user), ct);
            return result.Created ? Results.Accepted($"/api/payroll-jobs/{result.Job.Id}", Response(result.Job)) : Results.Ok(Response(result.Job));
        });
        group.MapGet("/{id:guid}", async (Guid id, AtlasDbContext db) =>
        {
            var job = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Kind == BackgroundJobKind.PayrollCreate);
            return job is null ? Results.NotFound() : Results.Ok(Response(job));
        });
        group.MapPost("/{id:guid}/retry", async (Guid id, ExpectedVersionRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            var job = await db.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == id && x.Kind == BackgroundJobKind.PayrollCreate);
            if (job is null) return Results.NotFound();
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            if (request.ExpectedVersion != job.Version) return Results.Problem(statusCode: 409, detail: "The job changed. Reload it.");
            job.Retry(clock.GetUtcNow()); await db.SaveChangesAsync();
            return Results.Accepted($"/api/payroll-jobs/{id}", Response(job));
        });
        return app;
    }
    internal static object Response(BackgroundJob job) => new { job.Id, kind = job.Kind.ToString(), state = job.State.ToString(), job.Version,
        job.TotalAttempts, job.CycleAttempts, job.ResultRunId, job.ErrorCode, job.CreatedAtUtc, job.UpdatedAtUtc, job.LeaseExpiresAtUtc };
}
