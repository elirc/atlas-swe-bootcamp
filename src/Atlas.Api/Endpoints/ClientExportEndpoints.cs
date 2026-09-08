using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ClientExportEndpoints
{
    public static IEndpointRouteBuilder MapClientExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients/{clientId:guid}/data-exports").RequireAuthorization();
        group.MapPost("/", async (Guid clientId, CreateExportRequest request, ClaimsPrincipal user, AtlasDbContext db, DurableJobService jobs) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            if (ApiInput.PeriodRange(request.FromPeriod, request.ToPeriod, out var from, out var to) is { } error) return error;
            if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length > 100) return ApiInput.Error("reference", "Use 1–100 characters.");
            var (job, created) = await jobs.SubmitExportAsync(new(clientId, InvoiceLedgerEndpoints.Actor(user), from.Year, from.Month, to.Year, to.Month), request.Reference.Trim());
            return created ? Results.Accepted($"/api/clients/{clientId}/data-exports/{job.Id}", Summary(job)) : Results.Ok(Summary(job));
        });
        group.MapGet("/{id:guid}", async (Guid clientId, Guid id, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var job = await Owned(clientId, id, user, db);
            if (job is null) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            var artifact = await db.ExportArtifacts.AsNoTracking().Where(x => x.JobId == id).Select(x => new { x.Checksum, x.SchemaVersion, x.GeneratedAtUtc, x.ExpiresAtUtc }).SingleOrDefaultAsync();
            if (artifact is not null && clock.GetUtcNow() >= artifact.ExpiresAtUtc) return Results.Problem(statusCode: 410, detail: "Export expired. Submit with a new reference.");
            return Results.Ok(new { job = Summary(job), artifact, downloadPath = artifact is null ? null : $"/api/clients/{clientId}/data-exports/{id}/download" });
        });
        group.MapGet("/{id:guid}/download", async (Guid clientId, Guid id, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var job = await Owned(clientId, id, user, db);
            if (job is null) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            var artifact = await db.ExportArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.JobId == id);
            if (artifact is null) return Results.Problem(statusCode: 409, detail: "The export is not ready.");
            if (clock.GetUtcNow() >= artifact.ExpiresAtUtc || artifact.Bytes is null) return Results.Problem(statusCode: 410, detail: "Export expired. Submit with a new reference.");
            return Results.File(artifact.Bytes, "application/json", "atlas-client-export.json");
        });
        return app;
    }
    private static async Task<BackgroundJob?> Owned(Guid clientId, Guid id, ClaimsPrincipal user, AtlasDbContext db)
    {
        if (!user.CanViewClient(clientId)) return null;
        var owner = InvoiceLedgerEndpoints.Actor(user);
        return await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Kind == BackgroundJobKind.ClientExport && x.ClientId == clientId && x.RequesterApiUserId == owner);
    }
    private static object Summary(BackgroundJob j) => new { j.Id, state = j.State.ToString(), j.Version, j.TotalAttempts, j.ErrorCode, j.CreatedAtUtc, j.UpdatedAtUtc };
}
public record CreateExportRequest(string? FromPeriod, string? ToPeriod, string? Reference);
