using Atlas.Api.Auth;
using Atlas.Api.Reporting;
using Atlas.Domain.Entities;

namespace Atlas.Api.Endpoints;

/// <summary>Transport, authorization, and validation for platform reports.</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.PlatformAdmin);

        group.MapGet("/headcount", async (DateOnly? asOf, string? countryCode, TimeProvider clock,
            ReportService reports, CancellationToken cancellationToken) =>
        {
            if (ApiInput.CodeFilter(countryCode, 2, "countryCode", out var country) is { } problem) return problem;
            return Results.Ok(await reports.HeadcountAsync(asOf ?? Today(clock), cancellationToken, country));
        });

        group.MapGet("/payroll-costs", async (string? countryCode, Guid? clientId, string? runStatus,
            int? fromYear, int? fromMonth, int? toYear, int? toMonth,
            ReportService reports, CancellationToken cancellationToken) =>
        {
            var errors = ReportQueryValidation.PeriodErrors(fromYear, fromMonth, toYear, toMonth);
            if (ApiInput.CodeFilter(countryCode, 2, "countryCode", out var country) is { } countryError) return countryError;
            PayrollRunStatus? status = null;
            if (!string.IsNullOrWhiteSpace(runStatus))
            {
                if (!ApiInput.DefinedEnum<PayrollRunStatus>(runStatus, out var parsed))
                    errors["runStatus"] = ["RunStatus must be Draft or Completed."];
                else status = parsed;
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            int? from = fromYear is null ? null : fromYear * 100 + (fromMonth ?? 1);
            int? to = toYear is null ? null : toYear * 100 + (toMonth ?? 12);
            return Results.Ok(await reports.PayrollCostsAsync(country, clientId, from, to, cancellationToken, status));
        });

        group.MapGet("/compliance-expiries", async (int? withinDays, DateOnly? asOf, string? countryCode,
            TimeProvider clock, ReportService reports, CancellationToken cancellationToken) =>
        {
            var date = asOf ?? Today(clock);
            var window = withinDays ?? 60;
            if (ApiInput.CodeFilter(countryCode, 2, "countryCode", out var country) is { } problem) return problem;
            if (!ReportQueryValidation.TryCutoff(date, window, out var cutoff))
                return ReportQueryValidation.InvalidWindow();
            return Results.Ok(await reports.ComplianceExpiriesAsync(date, cutoff, window, cancellationToken, country));
        });

        group.MapGet("/invoice-aging", async (Guid? clientId, DateOnly? asOf,
            TimeProvider clock, ReportService reports, CancellationToken cancellationToken) =>
            Results.Ok(await reports.InvoiceAgingAsync(clientId, asOf ?? Today(clock), cancellationToken)));

        group.MapGet("/credited-invoice-aging", async (Guid? clientId, DateOnly? asOf,
            TimeProvider clock, ReportService reports, CancellationToken cancellationToken) =>
            Results.Ok(await reports.InvoiceAgingAsync(clientId, asOf ?? Today(clock), cancellationToken, creditsOnly: true)));
        return app;
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

public record HeadcountByCountryResponse(string CountryCode, int Count);

public record HeadcountByClientResponse(Guid ClientId, string ClientName, int Count);

public record HeadcountReportResponse(
    DateOnly AsOf,
    int Total,
    List<HeadcountByCountryResponse> ByCountry,
    List<HeadcountByClientResponse> ByClient);

public record PayrollCostRowResponse(
    int Year,
    int Month,
    string CurrencyCode,
    int PayslipCount,
    decimal TotalGross,
    decimal TotalEmployerCost,
    decimal TotalReimbursements,
    decimal TotalBenefitsEmployerCost,
    decimal TotalUnusedLeavePayout,
    decimal TotalCost);

public record ComplianceExpiryRowResponse(
    Guid DocumentId,
    Guid WorkerId,
    string WorkerName,
    string CountryCode,
    string Type,
    string Name,
    DateOnly ExpiryDate,
    int DaysUntilExpiry,
    string Status);

public record InvoiceAgingRowResponse(
    string Bucket,
    string CurrencyCode,
    int InvoiceCount,
    decimal Total);

public record InvoiceAgingReportResponse(
    DateOnly AsOf,
    List<InvoiceAgingRowResponse> Rows);
