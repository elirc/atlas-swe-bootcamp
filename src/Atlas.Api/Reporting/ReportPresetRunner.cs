using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Domain.Entities;

namespace Atlas.Api.Reporting;

internal static class ReportPresetRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string KindName(SavedReportKind kind) => kind switch
    { SavedReportKind.Headcount => "headcount", SavedReportKind.PayrollCosts => "payroll-costs", SavedReportKind.ComplianceExpiries => "compliance-expiries", _ => "invoice-aging" };

    public static IResult? Validate(string? name, JsonElement input, TimeProvider clock, out SavedReportKind kind, out object? filters, out string canonical)
    {
        kind = default; filters = null; canonical = "";
        if (name is not ("headcount" or "payroll-costs" or "compliance-expiries" or "invoice-aging")) return ApiInput.Error("kind", "Choose headcount, payroll-costs, compliance-expiries, or invoice-aging.");
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 4000) return ApiInput.Error("filters", "Filters must be an object of at most 4,000 characters.");
        if (input.EnumerateObject().GroupBy(x => x.Name).Any(g => g.Count() > 1)) return ApiInput.Error("filters", "Duplicate filter fields are not allowed.");
        try
        {
            switch (name)
            {
                case "headcount":
                    kind = SavedReportKind.Headcount;
                    var head = input.Deserialize<HeadcountFilters>(Json)!;
                    if (ApiInput.CodeFilter(head.CountryCode, 2, "countryCode", out var headCountry) is { } h) return h;
                    filters = head with { CountryCode = headCountry }; break;
                case "payroll-costs":
                    kind = SavedReportKind.PayrollCosts;
                    var payroll = input.Deserialize<PayrollFilters>(Json)!;
                    var errors = ReportQueryValidation.PeriodErrors(payroll.FromYear, payroll.FromMonth, payroll.ToYear, payroll.ToMonth);
                    if (errors.Count > 0) return Results.ValidationProblem(errors);
                    if (ApiInput.CodeFilter(payroll.CountryCode, 2, "countryCode", out var payrollCountry) is { } p) return p;
                    string? status = null;
                    if (!string.IsNullOrWhiteSpace(payroll.RunStatus))
                    {
                        if (!ApiInput.DefinedEnum<PayrollRunStatus>(payroll.RunStatus, out var parsed)) return ApiInput.Error("runStatus", "RunStatus must be Draft or Completed.");
                        status = parsed.ToString();
                    }
                    filters = payroll with { CountryCode = payrollCountry, RunStatus = status }; break;
                case "compliance-expiries":
                    kind = SavedReportKind.ComplianceExpiries;
                    var compliance = input.Deserialize<ComplianceFilters>(Json)!;
                    if (ApiInput.CodeFilter(compliance.CountryCode, 2, "countryCode", out var complianceCountry) is { } c) return c;
                    if (!ReportQueryValidation.TryCutoff(compliance.AsOf ?? Today(clock), compliance.WithinDays ?? 60, out _)) return ReportQueryValidation.InvalidWindow();
                    filters = compliance with { CountryCode = complianceCountry }; break;
                case "invoice-aging": kind = SavedReportKind.InvoiceAging; filters = input.Deserialize<AgingFilters>(Json)!; break;
            }
            canonical = JsonSerializer.Serialize(filters, filters!.GetType(), Json);
            return canonical.Length > 4000 ? ApiInput.Error("filters", "Serialized filters exceed 4,000 characters.") : null;
        }
        catch (JsonException) { return ApiInput.Error("filters", "Filters contain an unknown field or a value with the wrong type or format."); }
    }

    public static async Task<object> ExecuteAsync(object filters, ReportService reports, TimeProvider clock, CancellationToken ct)
    {
        switch (filters)
        {
            case HeadcountFilters h:
                var head = h with { AsOf = h.AsOf ?? Today(clock) };
                return new { kind = "headcount", parameters = head, result = await reports.HeadcountAsync(head.AsOf!.Value, ct, head.CountryCode) };
            case PayrollFilters p:
                int? from = p.FromYear is null ? null : p.FromYear * 100 + (p.FromMonth ?? 1);
                int? to = p.ToYear is null ? null : p.ToYear * 100 + (p.ToMonth ?? 12);
                PayrollRunStatus? status = p.RunStatus is null ? null : Enum.Parse<PayrollRunStatus>(p.RunStatus);
                return new { kind = "payroll-costs", parameters = p, result = await reports.PayrollCostsAsync(p.CountryCode, p.ClientId, from, to, ct, status) };
            case ComplianceFilters c:
                var compliance = c with { AsOf = c.AsOf ?? Today(clock), WithinDays = c.WithinDays ?? 60 };
                ReportQueryValidation.TryCutoff(compliance.AsOf!.Value, compliance.WithinDays!.Value, out var cutoff);
                return new { kind = "compliance-expiries", parameters = compliance, result = await reports.ComplianceExpiriesAsync(compliance.AsOf.Value, cutoff, compliance.WithinDays.Value, ct, compliance.CountryCode) };
            case AgingFilters a:
                var aging = a with { AsOf = a.AsOf ?? Today(clock) };
                return new { kind = "invoice-aging", parameters = aging, result = await reports.InvoiceAgingAsync(aging.ClientId, aging.AsOf!.Value, ct) };
            default: throw new InvalidOperationException("Unsupported validated preset type.");
        }
    }
    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    internal record HeadcountFilters(DateOnly? AsOf, string? CountryCode);
    internal record PayrollFilters(string? CountryCode, Guid? ClientId, int? FromYear, int? FromMonth, int? ToYear, int? ToMonth, string? RunStatus);
    internal record ComplianceFilters(int? WithinDays, DateOnly? AsOf, string? CountryCode);
    internal record AgingFilters(Guid? ClientId, DateOnly? AsOf);
}
