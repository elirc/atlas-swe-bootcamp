using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class WorkforceViewEndpoints
{
    public static IEndpointRouteBuilder MapWorkforceViewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clients/{clientId:guid}/workforce-summary", async (
            Guid clientId, DateOnly? asOf, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (ApiInput.DateRange(date, date, 1) is { } problem) return problem;
            var rows = await db.Contracts.AsNoTracking().Where(c => c.ClientId == clientId && c.Status != ContractStatus.Draft
                && c.StartDate <= date && (c.EndDate == null || c.EndDate >= date))
                .Select(c => new { c.Id, c.WorkerId, c.CountryCode }).ToListAsync(ct);
            return Results.Ok(new
            {
                clientId, asOf = date, workerCount = rows.Select(c => c.WorkerId).Distinct().Count(), contractCount = rows.Count,
                countries = rows.GroupBy(c => c.CountryCode).OrderBy(g => g.Key).Select(g => new
                { countryCode = g.Key, workerCount = g.Select(c => c.WorkerId).Distinct().Count(), contractCount = g.Count() }),
            });
        }).RequireAuthorization();

        app.MapGet("/api/workers/{workerId:guid}/employment-timeline", async (
            Guid workerId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db, CancellationToken ct) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem) return problem;
            var platform = user.IsPlatformAdmin();
            var own = user.ClientIdOrNull();
            var contracts = await db.Contracts.AsNoTracking().Where(c => c.WorkerId == workerId && (platform || c.ClientId == own)).ToListAsync(ct);
            if (contracts.Count == 0 && (!platform || !await db.Workers.AnyAsync(w => w.Id == workerId, ct))) return Results.NotFound();
            var ids = contracts.Select(c => c.Id).ToList();
            var records = await db.SalaryRecords.AsNoTracking().Where(r => ids.Contains(r.ContractId)).ToListAsync(ct);
            var events = new List<EmploymentTimelineEvent>();
            foreach (var c in contracts)
            {
                events.Add(new("contract-created", c.Id, c.Id, DateOnly.FromDateTime(c.CreatedAtUtc.UtcDateTime), c.CreatedAtUtc, c.JobTitle, null, null));
                if (c.ActivatedAtUtc is { } activated)
                    events.Add(new("contract-activated", c.Id, c.Id, DateOnly.FromDateTime(activated.UtcDateTime), activated, null, null, null));
                if (c.EndDate is { } ended)
                    events.Add(new("contract-ended", c.Id, c.Id, ended, c.TerminatedAtUtc, null, null, null));
            }
            events.AddRange(records.Select(r => new EmploymentTimelineEvent("salary-effective", r.Id, r.ContractId,
                r.EffectiveDate, r.CreatedAtUtc, r.JobTitle, r.MonthlySalary, contracts.Single(c => c.Id == r.ContractId).CurrencyCode)));
            var ordered = events.OrderByDescending(e => e.EffectiveDate).ThenBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.SourceId).ToList();
            http.Response.Headers["X-Total-Count"] = ordered.Count.ToString();
            http.Response.Headers["X-Page"] = paging.Page.ToString();
            http.Response.Headers["X-Page-Size"] = paging.PageSize.ToString();
            return Results.Ok(ordered.Skip(paging.Skip).Take(paging.PageSize));
        }).RequireAuthorization();

        app.MapGet("/api/contracts/{contractId:guid}/activation-readiness", async (
            Guid contractId, ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            var items = await db.OnboardingItems.AsNoTracking().Where(i => i.ContractId == contractId).ToListAsync(ct);
            var countryActive = await db.Countries.Where(x => x.Code == contract.CountryCode).Select(x => x.IsActive).SingleAsync(ct);
            var blockers = ContractReadiness.Assess(contract, items, countryActive);
            return Results.Ok(new { contractId, contract.Version, canActivate = blockers.Count == 0, blockers });
        }).RequireAuthorization();

        app.MapGet("/api/clients/{clientId:guid}/leave-calendar", async (
            Guid clientId, DateOnly from, DateOnly to, ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Results.NotFound();
            if (ApiInput.DateRange(from, to, 93) is { } problem) return problem;
            var rows = await db.LeaveRequests.AsNoTracking().Where(r => r.Contract!.ClientId == clientId
                && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                && r.StartDate <= to && r.EndDate >= from)
                .Select(r => new { r.Id, r.ContractId, r.Contract!.WorkerId, workerName = r.Contract.Worker!.FullName,
                    r.Type, r.Status, r.StartDate, r.EndDate, r.Days })
                .OrderBy(r => r.StartDate < from ? from : r.StartDate).ThenBy(r => r.WorkerId).ThenBy(r => r.Id).Take(1001).ToListAsync(ct);
            if (rows.Count > 1000) return ApiInput.Error("to", "More than 1000 requests match; narrow the date range.");
            return Results.Ok(rows.Select(r => new { r.Id, r.ContractId, r.WorkerId, r.workerName,
                type = r.Type.ToString(), status = r.Status.ToString(), r.StartDate, r.EndDate, r.Days,
                displayStart = r.StartDate < from ? from : r.StartDate, displayEnd = r.EndDate > to ? to : r.EndDate }));
        }).RequireAuthorization();
        return app;
    }
}

public record EmploymentTimelineEvent(string Kind, Guid SourceId, Guid ContractId, DateOnly EffectiveDate,
    DateTimeOffset? RecordedAtUtc, string? JobTitle, decimal? MonthlySalary, string? CurrencyCode);
