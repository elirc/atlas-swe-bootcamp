using System.Linq.Expressions;
using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class FinancialViewEndpoints
{
    private static readonly Expression<Func<Payslip, PayslipBrowserResponse>> Projection = p => new(
        p.Id, p.PayrollRunId, p.ContractId, p.ClientId, p.WorkerId, p.PayrollRun!.Year, p.PayrollRun.Month,
        p.PayrollRun.Status.ToString(), p.CurrencyCode, p.GrossSalary, p.EmployerCost, p.EmployeeDeductions,
        p.NetPay, p.TotalCost, p.Reimbursements, p.BenefitsEmployerCost, p.BenefitsEmployeeDeduction, p.UnusedLeavePayout);

    public static IEndpointRouteBuilder MapFinancialViewEndpoints(this IEndpointRouteBuilder app)
    {
        var slips = app.MapGroup("/api/payslips").RequireAuthorization();
        slips.MapGet("/", async (string? fromPeriod, string? toPeriod, Guid? clientId, Guid? workerId,
            int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (clientId is { } requested && !user.CanViewClient(requested)) return Results.NotFound();
            if (ApiInput.PeriodRange(fromPeriod, toPeriod, out var from, out var to) is { } rangeProblem) return rangeProblem;
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem) return problem;
            var query = SelectedPayslips(db, user, clientId, workerId, from, to);
            return Results.Ok(await query.Select(Projection).ToPageAsync(http, paging));
        });
        slips.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            var platform = user.IsPlatformAdmin(); var own = user.ClientIdOrNull();
            var row = await db.Payslips.AsNoTracking().Where(p => p.Id == id && (platform || p.ClientId == own))
                .Select(Projection).SingleOrDefaultAsync(ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });
        slips.MapGet("/export.csv", async (string? fromPeriod, string? toPeriod, Guid? clientId, Guid? workerId,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            if (clientId is { } requested && !user.CanViewClient(requested)) return Results.NotFound();
            if (ApiInput.PeriodRange(fromPeriod, toPeriod, out var from, out var to) is { } problem) return problem;
            var rows = await SelectedPayslips(db, user, clientId, workerId, from, to).Select(Projection).Take(1001).ToListAsync(ct);
            if (rows.Count > 1000) return ApiInput.Error("fromPeriod", "More than 1000 payslips match; narrow the filters.");
            var bytes = CsvExport.Write(["PayslipId", "PayrollRunId", "ContractId", "ClientId", "WorkerId", "Period", "RunStatus", "Currency",
                "GrossSalary", "EmployeeDeductions", "NetPay", "EmployerCost", "Reimbursements", "BenefitsEmployerCost", "BenefitsEmployeeDeduction", "UnusedLeavePayout", "TotalCost"],
                rows.Select(p => new object?[] { p.Id, p.PayrollRunId, p.ContractId, p.ClientId, p.WorkerId, $"{p.Year:0000}-{p.Month:00}", p.RunStatus,
                    p.CurrencyCode, p.GrossSalary, p.EmployeeDeductions, p.NetPay, p.EmployerCost, p.Reimbursements, p.BenefitsEmployerCost,
                    p.BenefitsEmployeeDeduction, p.UnusedLeavePayout, p.TotalCost }));
            return Results.File(bytes, "text/csv; charset=utf-8", "payslips.csv");
        });

        app.MapGet("/api/invoices/{id:guid}/breakdown", async (Guid id, ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null || !user.CanViewClient(invoice.ClientId)) return Results.NotFound();
            var rows = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == invoice.PayrollRunId && p.ClientId == invoice.ClientId)
                .OrderBy(p => p.Id).Select(Projection).ToListAsync(ct);
            var subtotal = rows.Sum(p => p.TotalCost);
            return Results.Ok(new { invoice = InvoiceEndpoints.ToResponse(invoice), payslips = rows,
                calculatedPayslipSubtotal = subtotal, subtotalDifference = invoice.PayrollSubtotal - subtotal });
        }).RequireAuthorization();

        app.MapGet("/api/invoices/export.csv", async (DateOnly issuedFrom, DateOnly issuedTo, Guid? clientId,
            ClaimsPrincipal user, AtlasDbContext db, CancellationToken ct) =>
        {
            if (clientId is { } requested && !user.CanViewClient(requested)) return Results.NotFound();
            if (ApiInput.DateRange(issuedFrom, issuedTo, 366) is { } problem) return problem;
            var start = new DateTimeOffset(issuedFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(issuedTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var platform = user.IsPlatformAdmin(); var own = user.ClientIdOrNull();
            var rows = await db.Invoices.AsNoTracking().Where(i => (platform || i.ClientId == own) && (clientId == null || i.ClientId == clientId)
                && i.IssuedAtUtc >= start && i.IssuedAtUtc < end).OrderBy(i => i.IssuedAtUtc).ThenBy(i => i.Id).Take(1001).ToListAsync(ct);
            if (rows.Count > 1000) return ApiInput.Error("issuedFrom", "More than 1000 invoices match; narrow the filters.");
            var bytes = CsvExport.Write(["InvoiceId", "InvoiceNumber", "ClientId", "PayrollRunId", "IssuedAtUtc", "Currency", "PayrollSubtotal",
                "ManagementFee", "Total", "BillingCurrency", "FxRateApplied", "TotalInBillingCurrency"],
                rows.Select(i => new object?[] { i.Id, i.InvoiceNumber, i.ClientId, i.PayrollRunId, i.IssuedAtUtc, i.CurrencyCode,
                    i.PayrollSubtotal, i.ManagementFee, i.Total, i.BillingCurrencyCode,
                    new CsvNumber(i.FxRateApplied, "0.############################"), i.TotalInBillingCurrency }));
            return Results.File(bytes, "text/csv; charset=utf-8", "invoices.csv");
        }).RequireAuthorization();
        return app;
    }

    private static IOrderedQueryable<Payslip> SelectedPayslips(AtlasDbContext db, ClaimsPrincipal user, Guid? clientId,
        Guid? workerId, DateOnly from, DateOnly to)
    {
        var platform = user.IsPlatformAdmin(); var own = user.ClientIdOrNull();
        var start = from.Year * 100 + from.Month; var end = to.Year * 100 + to.Month;
        return db.Payslips.AsNoTracking().Where(p => (platform || p.ClientId == own)
            && (clientId == null || p.ClientId == clientId) && (workerId == null || p.WorkerId == workerId)
            && p.PayrollRun!.Year * 100 + p.PayrollRun.Month >= start && p.PayrollRun.Year * 100 + p.PayrollRun.Month <= end)
            .OrderByDescending(p => p.PayrollRun!.Year).ThenByDescending(p => p.PayrollRun!.Month).ThenBy(p => p.Id);
    }
}

public record PayslipBrowserResponse(Guid Id, Guid PayrollRunId, Guid ContractId, Guid ClientId, Guid WorkerId,
    int Year, int Month, string RunStatus, string CurrencyCode, decimal GrossSalary, decimal EmployerCost,
    decimal EmployeeDeductions, decimal NetPay, decimal TotalCost, decimal Reimbursements,
    decimal BenefitsEmployerCost, decimal BenefitsEmployeeDeduction, decimal UnusedLeavePayout);
