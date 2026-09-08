using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class PayrollLearningEndpoints
{
    public static IEndpointRouteBuilder MapPayrollLearningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payroll-simulations", async (CreatePayrollRunRequest request, AtlasDbContext db, PayrollAssessmentService assessment, CancellationToken ct) =>
        {
            if (PeriodInput(request) is { } error) return error;
            await using var snapshot = await SqliteReadSnapshot.BeginAsync(db, ct);
            try { return Results.Ok(await assessment.AssessAsync(request.CountryCode!.Trim().ToUpperInvariant(), request.Year, request.Month, ct)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        app.MapGet("/api/payslips/{id:guid}/calculation-evidence", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var slip = await db.Payslips.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (slip is null || !user.CanViewClient(slip.ClientId)) return Results.NotFound();
            var saved = await db.PayslipCalculationEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.PayslipId == id);
            if (saved is null) return Results.Ok(new { available = false, reason = "not-captured" });
            if (saved.SchemaVersion != 1 || saved.CalculatorVersion != PayrollAssessmentCalculator.Version)
                return Results.Ok(new { available = true, supported = false, reason = "unsupported-version", saved.SchemaVersion, saved.CalculatorVersion, saved.CapturedAtUtc });
            PayrollCalculationDocument? document;
            try { document = JsonSerializer.Deserialize<PayrollCalculationDocument>(saved.EvidenceJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return Results.Problem(statusCode: 409, detail: "Stored calculation evidence is invalid."); }
            if (document?.Inputs?.Expenses is null || document.Inputs.Benefits is null || document.Outputs is null)
                return Results.Problem(statusCode: 409, detail: "Stored calculation evidence is incomplete.");
            if (document.SchemaVersion != 1 || document.CalculatorVersion != saved.CalculatorVersion)
                return Results.Ok(new { available = true, supported = false, reason = "unsupported-version" });
            var replayed = PayrollAssessmentCalculator.Calculate(document.Inputs);
            return Results.Ok(new { available = true, supported = true, saved.SchemaVersion, saved.CalculatorVersion, saved.CapturedAtUtc,
                document.Inputs, document.Outputs, replayedOutputs = replayed, matchesCapturedOutputs = replayed == document.Outputs,
                matchesSavedPayslip = replayed == PayrollMoney.From(slip) });
        }).RequireAuthorization();
        app.MapGet("/api/reports/payroll-variance", async (Guid baselineRunId, Guid comparisonRunId, AtlasDbContext db) =>
        {
            if (baselineRunId == comparisonRunId) return ApiInput.Error("comparisonRunId", "Choose two different payroll runs.");
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var baseline = await db.PayrollRuns.AsNoTracking().Include(x => x.Payslips).SingleOrDefaultAsync(x => x.Id == baselineRunId);
            var comparison = await db.PayrollRuns.AsNoTracking().Include(x => x.Payslips).SingleOrDefaultAsync(x => x.Id == comparisonRunId);
            if (baseline is null || comparison is null) return Results.NotFound();
            if (baseline.CountryCode != comparison.CountryCode || baseline.Year * 100 + baseline.Month >= comparison.Year * 100 + comparison.Month)
                return ApiInput.Error("comparisonRunId", "Choose a later period in the same country.");
            var left = baseline.Payslips.ToDictionary(x => x.ContractId); var right = comparison.Payslips.ToDictionary(x => x.ContractId);
            var rows = new List<PayrollVarianceRow>();
            foreach (var id in left.Keys.Union(right.Keys).Order())
            {
                left.TryGetValue(id, out var before); right.TryGetValue(id, out var after);
                if (before is not null && after is not null && before.CurrencyCode != after.CurrencyCode)
                    throw new DomainException("A retained contract has different currencies in the saved payslips.");
                var a = before is null ? PayrollMoney.Zero : PayrollMoney.From(before); var b = after is null ? PayrollMoney.Zero : PayrollMoney.From(after);
                rows.Add(new(id, (after ?? before)!.ClientId, (after ?? before)!.CurrencyCode, before is not null, after is not null,
                    before is null ? "added" : after is null ? "removed" : "retained", a, b, b - a));
            }
            var totals = rows.GroupBy(x => x.CurrencyCode).OrderBy(g => g.Key).Select(g => new { currencyCode = g.Key,
                baseline = g.Aggregate(PayrollMoney.Zero, (sum, x) => sum + x.Baseline), comparison = g.Aggregate(PayrollMoney.Zero, (sum, x) => sum + x.Comparison),
                delta = g.Aggregate(PayrollMoney.Zero, (sum, x) => sum + x.Delta) });
            return Results.Ok(new { countryCode = baseline.CountryCode,
                baseline = new { baseline.Id, baseline.Year, baseline.Month, status = baseline.Status.ToString() },
                comparison = new { comparison.Id, comparison.Year, comparison.Month, status = comparison.Status.ToString() }, rows, totals });
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        return app;
    }
    internal static IResult? PeriodInput(CreatePayrollRunRequest request) =>
        request.CountryCode is null || request.CountryCode.Trim().Length != 2 ? ApiInput.Error("countryCode", "Use a configured two-character country code.")
        : request.Year is < 2000 or > 2100 ? ApiInput.Error("year", "Year must be between 2000 and 2100.")
        : request.Month is < 1 or > 12 ? ApiInput.Error("month", "Month must be between 1 and 12.") : null;
}
public record PayrollVarianceRow(Guid ContractId, Guid ClientId, string CurrencyCode, bool BaselinePresent, bool ComparisonPresent,
    string Classification, PayrollMoney Baseline, PayrollMoney Comparison, PayrollMoney Delta);
