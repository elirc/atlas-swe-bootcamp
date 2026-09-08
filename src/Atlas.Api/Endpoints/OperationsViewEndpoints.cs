using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class OperationsViewEndpoints
{
    public static IEndpointRouteBuilder MapOperationsViewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clients/{clientId:guid}/expense-summary", async (Guid clientId, string? fromPeriod, string? toPeriod,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Results.NotFound();
            if (ApiInput.PeriodRange(fromPeriod, toPeriod, out var from, out var to) is { } problem) return problem;
            var rows = await db.ExpenseItems.AsNoTracking().Where(i => i.ExpenseClaim!.Contract!.ClientId == clientId
                && (i.ExpenseClaim.Status == ExpenseClaimStatus.Approved || i.ExpenseClaim.Status == ExpenseClaimStatus.Reimbursed)
                && i.IncurredDate >= from && i.IncurredDate <= to)
                .Select(i => new { i.Id, i.ExpenseClaimId, i.IncurredDate, i.Amount, i.ExpenseClaim!.CurrencyCode, i.ExpenseClaim.Status }).ToListAsync(ct);
            return Results.Ok(new
            {
                clientId, fromPeriod, toPeriod,
                groups = rows.GroupBy(i => new { i.IncurredDate.Year, i.IncurredDate.Month, i.CurrencyCode })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).ThenBy(g => g.Key.CurrencyCode)
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.CurrencyCode,
                        approvedAmount = g.Where(i => i.Status == ExpenseClaimStatus.Approved).Sum(i => i.Amount),
                        reimbursedAmount = g.Where(i => i.Status == ExpenseClaimStatus.Reimbursed).Sum(i => i.Amount),
                        itemCount = g.Count(), distinctClaimCount = g.Select(i => i.ExpenseClaimId).Distinct().Count() }),
            });
        }).RequireAuthorization();

        app.MapPost("/api/contracts/{contractId:guid}/benefit-quote", async (Guid contractId, BenefitQuoteRequest request,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (request.PlanIds is null || request.PlanIds.Count is < 1 or > 20 || request.PlanIds.Distinct().Count() != request.PlanIds.Count)
                return ApiInput.Error("planIds", "Select between 1 and 20 distinct plans.");
            if (contract.Status != ContractStatus.Active) return Results.Conflict(new { detail = "Benefit quotes require an active contract." });
            var plans = await db.BenefitPlans.AsNoTracking().Where(p => request.PlanIds.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync(ct);
            if (plans.Count != request.PlanIds.Count) return Results.NotFound();
            if (plans.Any(p => !p.IsActive || p.CountryCode != contract.CountryCode))
                return Results.Conflict(new { detail = "Select active plans in the contract country." });
            return Results.Ok(new
            {
                contractId, contract.CurrencyCode, estimateScope = "Selected plans at current full-month prices; excludes existing enrollments.",
                plans = plans.Select(p => new { p.Id, p.Name, p.MonthlyCost, p.EmployerShare, p.EmployeeShare }),
                monthlyCost = plans.Sum(p => p.MonthlyCost), employerShare = plans.Sum(p => p.EmployerShare), employeeShare = plans.Sum(p => p.EmployeeShare),
            });
        }).RequireAuthorization();

        app.MapGet("/api/contracts/{contractId:guid}/benefit-calendar", async (Guid contractId, string? fromPeriod, string? toPeriod,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (ApiInput.PeriodRange(fromPeriod, toPeriod, out var from, out var to) is { } problem) return problem;
            var enrollments = await db.BenefitEnrollments.AsNoTracking().Include(e => e.BenefitPlan)
                .Where(e => e.ContractId == contractId && e.StartDate <= to && (e.EndDate == null || e.EndDate >= from)).ToListAsync(ct);
            var months = new List<BenefitCalendarMonth>();
            for (var date = from; date <= to; date = date.AddMonths(1))
            {
                var covering = enrollments.Where(e => e.CoversMonth(date.Year, date.Month)).OrderBy(e => e.Id).ToList();
                months.Add(new(date.Year, date.Month, contract.CoversMonth(date.Year, date.Month),
                    covering.Select(e => new BenefitCoverage(e.Id, e.BenefitPlanId, e.BenefitPlan!.Name, e.StartDate, e.EndDate,
                        e.Status.ToString(), e.BenefitPlan.MonthlyCost, e.BenefitPlan.EmployerShare, e.BenefitPlan.EmployeeShare)).ToList(),
                    covering.Sum(e => e.BenefitPlan!.EmployerShare), covering.Sum(e => e.BenefitPlan!.EmployeeShare)));
            }
            return Results.Ok(new { contractId, contract.CurrencyCode,
                estimateScope = "Enrollment coverage at current plan prices; saved payslips contain actual charges.", months });
        }).RequireAuthorization();

        app.MapGet("/api/clients/{clientId:guid}/approval-queue", async (Guid clientId, int? limit,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Results.NotFound();
            var size = limit ?? 25;
            if (size is < 1 or > 100) return ApiInput.Error("limit", "Limit must be between 1 and 100.");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var leaves = db.LeaveRequests.AsNoTracking().Where(r => r.Contract!.ClientId == clientId && r.Status == LeaveRequestStatus.Pending);
            var expenses = db.ExpenseClaims.AsNoTracking().Where(r => r.Contract!.ClientId == clientId && r.Status == ExpenseClaimStatus.Pending);
            var amendments = db.ContractAmendments.AsNoTracking().Where(r => r.Contract!.ClientId == clientId && r.Status == AmendmentStatus.Pending);
            var terminations = db.TerminationRequests.AsNoTracking().Where(r => r.Contract!.ClientId == clientId && r.Status == TerminationRequestStatus.Pending);
            var counts = new { leave = await leaves.CountAsync(ct), expense = await expenses.CountAsync(ct),
                amendment = await amendments.CountAsync(ct), termination = await terminations.CountAsync(ct) };
            var items = new List<ApprovalQueueItem>();
            items.AddRange(await leaves.OrderByDescending(r => r.RequestedAtUtc).ThenBy(r => r.Id).Take(size + 1)
                .Select(r => new ApprovalQueueItem("leave", r.Id, r.ContractId, r.Contract!.WorkerId, r.RequestedAtUtc,
                    "/api/leave-requests/" + r.Id, new { type = r.Type.ToString(), r.StartDate, r.EndDate })).ToListAsync(ct));
            var expenseRows = await expenses.Include(r => r.Items).Include(r => r.Contract)
                .OrderByDescending(r => r.SubmittedAtUtc).ThenBy(r => r.Id).Take(size + 1).ToListAsync(ct);
            items.AddRange(expenseRows.Select(r => new ApprovalQueueItem("expense", r.Id, r.ContractId, r.Contract!.WorkerId, r.SubmittedAtUtc,
                "/api/expense-claims/" + r.Id, new { r.TotalAmount, r.CurrencyCode })));
            items.AddRange(await amendments.OrderByDescending(r => r.RequestedAtUtc).ThenBy(r => r.Id).Take(size + 1)
                .Select(r => new ApprovalQueueItem("amendment", r.Id, r.ContractId, r.Contract!.WorkerId, r.RequestedAtUtc,
                    "/api/contract-amendments/" + r.Id, new { r.NewMonthlySalary, r.NewJobTitle, r.EffectiveDate })).ToListAsync(ct));
            items.AddRange(await terminations.OrderByDescending(r => r.RequestedAtUtc).ThenBy(r => r.Id).Take(size + 1)
                .Select(r => new ApprovalQueueItem("termination", r.Id, r.ContractId, r.Contract!.WorkerId, r.RequestedAtUtc,
                    "/api/termination-requests/" + r.Id, new { r.ProposedEndDate })).ToListAsync(ct));
            await transaction.CommitAsync(ct);
            var total = counts.leave + counts.expense + counts.amendment + counts.termination;
            return Results.Ok(new { clientId, counts, totalPending = total, hasMore = total > size,
                items = items.OrderByDescending(i => i.SubmittedAtUtc).ThenBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.SourceId).Take(size) });
        }).RequireAuthorization();
        return app;
    }
}

public record BenefitQuoteRequest(List<Guid>? PlanIds);
public record BenefitCoverage(Guid Id, Guid BenefitPlanId, string Name, DateOnly StartDate, DateOnly? EndDate,
    string Status, decimal MonthlyCost, decimal EmployerShare, decimal EmployeeShare);
public record BenefitCalendarMonth(int Year, int Month, bool ContractCoversMonth, List<BenefitCoverage> Enrollments,
    decimal EstimatedEmployerCost, decimal EstimatedEmployeeCost);
public record ApprovalQueueItem(string Kind, Guid SourceId, Guid ContractId, Guid WorkerId, DateTimeOffset SubmittedAtUtc,
    string DetailPath, object Summary);
