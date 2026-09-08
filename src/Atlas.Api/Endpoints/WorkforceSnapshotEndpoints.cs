using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class WorkforceSnapshotEndpoints
{
    public static IEndpointRouteBuilder MapWorkforceSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients/{clientId:guid}/workforce-snapshots").RequireAuthorization();
        group.MapPost("/", async (Guid clientId, CaptureWorkforceRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            if (request.AsOf.Year is < 2000 or > 2100) return ApiInput.Error("asOf", "Use year 2000–2100.");
            if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 100) return ApiInput.Error("label", "Use 1–100 characters.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            var rows = await db.Contracts.AsNoTracking().Where(x => x.ClientId == clientId && x.Status != ContractStatus.Draft
                && x.StartDate <= request.AsOf && (x.EndDate == null || x.EndDate >= request.AsOf)).OrderBy(x => x.Id).Take(5001)
                .Select(x => new WorkforceSnapshotRow { ContractId = x.Id, WorkerId = x.WorkerId, WorkerName = x.Worker!.FullName,
                    CountryCode = x.CountryCode, JobTitle = x.JobTitle, StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync();
            if (rows.Count > 5000) return Results.Problem(statusCode: 409, detail: "A snapshot supports at most 5,000 contracts.");
            var snapshot = new WorkforceSnapshot { ClientId = clientId, CreatorApiUserId = InvoiceLedgerEndpoints.Actor(user), Label = request.Label.Trim(),
                AsOf = request.AsOf, CapturedAtUtc = clock.GetUtcNow(), ContractCount = rows.Count, WorkerCount = rows.Select(x => x.WorkerId).Distinct().Count(), Rows = rows };
            foreach (var row in rows) row.SnapshotId = snapshot.Id;
            db.WorkforceSnapshots.Add(snapshot); await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Created($"/api/clients/{clientId}/workforce-snapshots/{snapshot.Id}", Header(snapshot));
        });
        group.MapGet("/", async (Guid clientId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok((await db.WorkforceSnapshots.AsNoTracking().Where(x => x.ClientId == clientId).OrderByDescending(x => x.CapturedAtUtc).ThenBy(x => x.Id).ToPageAsync(http, paging)).Select(Header));
        });
        group.MapGet("/{id:guid}", async (Guid clientId, Guid id, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            var snapshot = await db.WorkforceSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ClientId == clientId);
            if (snapshot is null) return Results.NotFound();
            var rows = await db.Set<WorkforceSnapshotRow>().AsNoTracking().Where(x => x.SnapshotId == id).OrderBy(x => x.ContractId).ToPageAsync(http, paging);
            return Results.Ok(new { snapshot = Header(snapshot), rows });
        });
        group.MapPost("/compare", async (Guid clientId, CompareWorkforceRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (request.BaselineId == request.ComparisonId) return ApiInput.Error("comparisonId", "Choose distinct snapshots.");
            var snapshots = await db.WorkforceSnapshots.AsNoTracking().Include(x => x.Rows).Where(x => x.ClientId == clientId && (x.Id == request.BaselineId || x.Id == request.ComparisonId)).ToListAsync();
            if (snapshots.Count != 2) return Results.NotFound();
            var baseline = snapshots.Single(x => x.Id == request.BaselineId); var comparison = snapshots.Single(x => x.Id == request.ComparisonId);
            var left = baseline.Rows.ToDictionary(x => x.ContractId); var right = comparison.Rows.ToDictionary(x => x.ContractId);
            var rows = left.Keys.Union(right.Keys).OrderBy(x => x).Select(id =>
            {
                left.TryGetValue(id, out var before); right.TryGetValue(id, out var after);
                return new { contractId = id, classification = before is null ? "added" : after is null ? "removed" : "retained",
                    changedFields = before is not null && after is not null ? Changes(before, after) : [], before, after };
            });
            return Results.Ok(new { baseline = Header(baseline), comparison = Header(comparison), rows });
        });
        return app;
    }
    private static Task<bool> Visible(Guid id, ClaimsPrincipal user, AtlasDbContext db) => user.CanViewClient(id) ? db.Clients.AnyAsync(x => x.Id == id) : Task.FromResult(false);
    private static object Header(WorkforceSnapshot s) => new { s.Id, s.ClientId, s.CreatorApiUserId, s.Label, s.AsOf, s.CapturedAtUtc, s.ContractCount, s.WorkerCount, labels = "as-captured" };
    private static List<string> Changes(WorkforceSnapshotRow a, WorkforceSnapshotRow b)
    {
        List<string> result = [];
        if (a.WorkerId != b.WorkerId) result.Add("workerId");
        if (a.WorkerName != b.WorkerName) result.Add("workerName");
        if (a.CountryCode != b.CountryCode) result.Add("countryCode");
        if (a.JobTitle != b.JobTitle) result.Add("jobTitle");
        if (a.StartDate != b.StartDate) result.Add("startDate");
        if (a.EndDate != b.EndDate) result.Add("endDate");
        return result;
    }
}
public record CaptureWorkforceRequest(DateOnly AsOf, string? Label);
public record CompareWorkforceRequest(Guid BaselineId, Guid ComparisonId);
