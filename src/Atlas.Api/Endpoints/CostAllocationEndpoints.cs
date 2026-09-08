using System.Security.Claims;
using System.Text.RegularExpressions;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class CostAllocationEndpoints
{
    public static IEndpointRouteBuilder MapCostAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        var clients = app.MapGroup("/api/clients/{clientId:guid}").RequireAuthorization();
        clients.MapGet("/cost-centers", async (Guid clientId, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            return Results.Ok(await db.ClientCostCenters.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Code).ToListAsync());
        });
        clients.MapPost("/cost-centers", async (Guid clientId, CostCenterRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            if (request.Code is null || !Regex.IsMatch(request.Code.Trim(), "\\A[A-Za-z0-9_-]{1,30}\\z")) return ApiInput.Error("code", "Use 1–30 ASCII letters, digits, underscores or hyphens.");
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100) return ApiInput.Error("name", "Use 1–100 characters.");
            var center = new ClientCostCenter { ClientId = clientId, Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };
            db.ClientCostCenters.Add(center); await db.SaveChangesAsync(); return Results.Created($"/api/clients/{clientId}/cost-centers", center);
        });
        var contracts = app.MapGroup("/api/contracts/{id:guid}/cost-allocations").RequireAuthorization();
        contracts.MapGet("/", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            return Results.Ok(await db.ContractAllocationSchedules.AsNoTracking().Include(x => x.Items).Where(x => x.ContractId == id).OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync());
        });
        contracts.MapPost("/", async (Guid id, AllocationRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var contract = await db.Contracts.FindAsync(id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (request.Year is < 2000 or > 2100 || request.Month is < 1 or > 12 || request.Year * 12 + request.Month < today.Year * 12 + today.Month)
                return ApiInput.Error("year", "Choose the current or a future month, with year 2000–2100.");
            if (request.Items is not { Count: >= 1 and <= 10 } || request.Items.Any(x => x is null || x.BasisPoints is < 1 or > 10000)
                || request.Items.Sum(x => x.BasisPoints) != 10000 || request.Items.Select(x => x.CostCenterId).Distinct().Count() != request.Items.Count)
                return ApiInput.Error("items", "Supply 1–10 distinct centers with positive weights totaling 10,000 basis points.");
            var ids = request.Items.Select(x => x.CostCenterId).ToList();
            if (await db.ClientCostCenters.CountAsync(x => x.ClientId == contract.ClientId && ids.Contains(x.Id)) != ids.Count)
                return ApiInput.Error("items", "Every cost center must belong to this contract's client.");
            var schedule = new ContractAllocationSchedule { ContractId = id, Year = request.Year, Month = request.Month };
            schedule.Items = request.Items.Select(x => new ContractAllocationItem { ScheduleId = schedule.Id, CostCenterId = x.CostCenterId, BasisPoints = x.BasisPoints }).ToList();
            db.ContractAllocationSchedules.Add(schedule); db.Entry(contract).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync(); return Results.Created($"/api/contracts/{id}/cost-allocations", schedule);
        });
        app.MapGet("/api/payslips/{id:guid}/cost-allocations", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var slip = await db.Payslips.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (slip is null || !user.CanViewClient(slip.ClientId)) return Results.NotFound();
            var rows = await db.PayslipCostAllocations.AsNoTracking().Where(x => x.PayslipId == id).OrderBy(x => x.BucketKey).ToListAsync();
            return Results.Ok(new { payslipId = id, available = rows.Count > 0, reason = rows.Count == 0 ? "not-captured" : null, rows });
        }).RequireAuthorization();
        clients.MapGet("/cost-center-payroll", async (Guid clientId, int fromYear, int fromMonth, int toYear, int toMonth, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (fromYear is < 2000 or > 2100 || toYear is < 2000 or > 2100 || fromMonth is < 1 or > 12 || toMonth is < 1 or > 12)
                return ApiInput.Error("fromYear", "Use years 2000–2100 and months 1–12.");
            if (ApiInput.PeriodRange($"{fromYear}-{fromMonth:00}", $"{toYear}-{toMonth:00}", out var from, out var to) is { } error) return error;
            var start = from.Year * 12 + from.Month; var end = to.Year * 12 + to.Month;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var slips = await db.Payslips.AsNoTracking().Where(x => x.ClientId == clientId && x.PayrollRun!.Status == PayrollRunStatus.Completed
                && x.PayrollRun.Year * 12 + x.PayrollRun.Month >= start && x.PayrollRun.Year * 12 + x.PayrollRun.Month <= end)
                .Select(x => new { x.Id, x.PayrollRun!.Year, x.PayrollRun.Month, x.CurrencyCode, x.TotalCost, x.GrossSalary,
                    x.EmployerCost, x.EmployeeDeductions, x.Reimbursements, x.BenefitsEmployerCost, x.BenefitsEmployeeDeduction, x.UnusedLeavePayout, x.NetPay }).ToListAsync();
            var ids = slips.Select(x => x.Id).ToList();
            var allocations = new List<PayslipCostAllocation>();
            foreach (var chunk in ids.Chunk(500)) allocations.AddRange(await db.PayslipCostAllocations.AsNoTracking().Where(x => chunk.Contains(x.PayslipId)).ToListAsync());
            var captured = allocations.Select(x => x.PayslipId).ToHashSet();
            var periods = slips.GroupBy(x => new { x.Year, x.Month, x.CurrencyCode }).OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month).ThenBy(x => x.Key.CurrencyCode).Select(g =>
            {
                var periodIds = g.Select(x => x.Id).ToHashSet(); var rows = allocations.Where(x => periodIds.Contains(x.PayslipId)).ToList();
                var sourceTotal = g.Sum(x => x.TotalCost); var allocatedTotal = rows.Sum(x => x.TotalCost);
                var legacyUncapturedTotal = g.Where(x => !captured.Contains(x.Id)).Sum(x => x.TotalCost);
                var legacy = g.Where(x => !captured.Contains(x.Id)).Select(x => new PayslipCostAllocation { PayslipId = x.Id, BucketKey = "not-captured",
                    Code = "NOT-CAPTURED", Name = "Historical allocation not captured", CurrencyCode = x.CurrencyCode,
                    GrossSalary = x.GrossSalary, EmployerCost = x.EmployerCost, EmployeeDeductions = x.EmployeeDeductions, Reimbursements = x.Reimbursements,
                    BenefitsEmployerCost = x.BenefitsEmployerCost, BenefitsEmployeeDeduction = x.BenefitsEmployeeDeduction,
                    UnusedLeavePayout = x.UnusedLeavePayout, NetPay = x.NetPay, TotalCost = x.TotalCost });
                return new { g.Key.Year, g.Key.Month, g.Key.CurrencyCode, sourcePayslipTotal = sourceTotal, allocatedTotal, legacyUncapturedTotal,
                    reconciliationDifference = allocatedTotal + legacyUncapturedTotal - sourceTotal,
                    buckets = rows.Concat(legacy).GroupBy(x => x.BucketKey).OrderBy(x => x.Key, StringComparer.Ordinal).Select(bucket => new { bucketKey = bucket.Key,
                        labels = bucket.Select(x => new { x.Code, x.Name }).Distinct().OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal), totalCost = bucket.Sum(x => x.TotalCost),
                        grossSalary = bucket.Sum(x => x.GrossSalary), employerCost = bucket.Sum(x => x.EmployerCost), employeeDeductions = bucket.Sum(x => x.EmployeeDeductions),
                        reimbursements = bucket.Sum(x => x.Reimbursements), benefitsEmployerCost = bucket.Sum(x => x.BenefitsEmployerCost),
                        benefitsEmployeeDeduction = bucket.Sum(x => x.BenefitsEmployeeDeduction), unusedLeavePayout = bucket.Sum(x => x.UnusedLeavePayout), netPay = bucket.Sum(x => x.NetPay) }) };
            });
            return Results.Ok(new { clientId, fromYear, fromMonth, toYear, toMonth, basis = "completed-payslip-allocations", periods });
        });
        return app;
    }
}
public record CostCenterRequest(string? Code, string? Name);
public record AllocationRequest(int Year, int Month, List<AllocationItemRequest>? Items);
public record AllocationItemRequest(Guid CostCenterId, int BasisPoints);
