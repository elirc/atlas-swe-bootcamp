using Atlas.Api.Endpoints;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Reporting;

/// <summary>
/// Read-only report queries. Dates are explicit inputs; transport validation and
/// authorization stay in endpoints. These read models belong to the API.
/// </summary>
public sealed class ReportService(AtlasDbContext db)
{
    public async Task<HeadcountReportResponse> HeadcountAsync(DateOnly date, CancellationToken cancellationToken, string? countryCode = null)
    {
        var employed = await db.Contracts.AsNoTracking()
            .Where(c => c.Status != ContractStatus.Draft && c.StartDate <= date
                        && (c.EndDate == null || c.EndDate >= date))
            .Where(c => countryCode == null || c.CountryCode == countryCode)
            .Select(c => new { c.CountryCode, c.ClientId })
            .ToListAsync(cancellationToken);
        var clientIds = employed.Select(c => c.ClientId).Distinct().ToList();
        var clientNames = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return new(date, employed.Count,
            employed.GroupBy(c => c.CountryCode)
                .Select(g => new HeadcountByCountryResponse(g.Key, g.Count()))
                .OrderBy(r => r.CountryCode).ToList(),
            employed.GroupBy(c => c.ClientId)
                .Select(g => new HeadcountByClientResponse(g.Key, clientNames[g.Key], g.Count()))
                .OrderBy(r => r.ClientName).ThenBy(r => r.ClientId).ToList());
    }

    public async Task<List<PayrollCostRowResponse>> PayrollCostsAsync(
        string? countryCode, Guid? clientId, int? fromPeriod, int? toPeriod, CancellationToken cancellationToken,
        PayrollRunStatus? runStatus = null)
    {
        var query = db.Payslips.AsNoTracking().AsQueryable();
        if (runStatus is not null) query = query.Where(p => p.PayrollRun!.Status == runStatus);
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var code = countryCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.PayrollRun!.CountryCode == code);
        }
        if (clientId is not null) query = query.Where(p => p.ClientId == clientId);
        if (fromPeriod is not null)
            query = query.Where(p => p.PayrollRun!.Year * 100 + p.PayrollRun.Month >= fromPeriod);
        if (toPeriod is not null)
            query = query.Where(p => p.PayrollRun!.Year * 100 + p.PayrollRun.Month <= toPeriod);

        // Filter and project in SQL, then aggregate decimal money in memory.
        // Keep decimal semantics explicit instead of converting money to double.
        var payslips = await query.Select(p => new
        {
            p.PayrollRun!.Year, p.PayrollRun.Month, p.CurrencyCode,
            p.GrossSalary, p.EmployerCost, p.Reimbursements, p.BenefitsEmployerCost,
            p.UnusedLeavePayout, p.TotalCost,
        }).ToListAsync(cancellationToken);
        return payslips.GroupBy(p => new { p.Year, p.Month, p.CurrencyCode })
            .Select(g => new PayrollCostRowResponse(g.Key.Year, g.Key.Month, g.Key.CurrencyCode,
                g.Count(), g.Sum(p => p.GrossSalary), g.Sum(p => p.EmployerCost),
                g.Sum(p => p.Reimbursements), g.Sum(p => p.BenefitsEmployerCost),
                g.Sum(p => p.UnusedLeavePayout), g.Sum(p => p.TotalCost)))
            .OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.CurrencyCode).ToList();
    }

    public async Task<List<ComplianceExpiryRowResponse>> ComplianceExpiriesAsync(
        DateOnly asOf, DateOnly cutoff, int window, CancellationToken cancellationToken, string? countryCode = null)
    {
        var documents = await db.CurrentDocumentsAt(asOf).Include(d => d.Worker)
            .Where(d => d.ExpiryDate != null && d.ExpiryDate <= cutoff)
            .Where(d => countryCode == null || d.Worker!.CountryCode == countryCode)
            .OrderBy(d => d.ExpiryDate).ThenBy(d => d.Id).ToListAsync(cancellationToken);
        return documents.Select(d => new ComplianceExpiryRowResponse(d.Id, d.WorkerId,
            d.Worker!.FullName, d.Worker.CountryCode, d.Type.ToString(), d.Name,
            d.ExpiryDate!.Value, d.ExpiryDate.Value.DayNumber - asOf.DayNumber,
            d.GetStatus(asOf, window).ToString())).ToList();
    }

    public async Task<InvoiceAgingReportResponse> InvoiceAgingAsync(
        Guid? clientId, DateOnly asOf, CancellationToken cancellationToken, bool creditsOnly = false)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(cancellationToken);
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (clientId is not null) query = query.Where(i => i.ClientId == clientId);
        // Inclusive UTC calendar day, including DateOnly.MaxValue without AddDays overflow.
        var endOfDay = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var invoices = await query.Where(i => i.IssuedAtUtc <= endOfDay)
            .Select(i => new { i.Id, i.IssuedAtUtc, i.BillingCurrencyCode, i.TotalInBillingCurrency })
            .ToListAsync(cancellationToken);
        var ids = invoices.Select(x => x.Id).ToList();
        var credits = await db.InvoiceCreditNotes.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && x.IssuedAtUtc <= endOfDay)
            .Select(x => new { x.InvoiceId, x.Amount }).ToListAsync(cancellationToken);
        var payments = creditsOnly ? [] : await db.InvoicePayments.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && x.PaidAtUtc <= endOfDay)
            .Select(x => new { x.Id, x.InvoiceId, x.Amount }).ToListAsync(cancellationToken);
        var reversed = creditsOnly ? [] : await db.InvoicePaymentReversals.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && x.RecordedAtUtc <= endOfDay)
            .Select(x => x.PaymentId).ToListAsync(cancellationToken);
        var creditTotals = credits.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var reversedIds = reversed.ToHashSet();
        var netPayments = payments.Where(x => !reversedIds.Contains(x.Id)).GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var balances = invoices.Select(i => new { i.IssuedAtUtc, i.BillingCurrencyCode,
            Outstanding = i.TotalInBillingCurrency - creditTotals.GetValueOrDefault(i.Id) - netPayments.GetValueOrDefault(i.Id) });
        var rows = balances.Where(i => i.Outstanding > 0).GroupBy(i => new
            {
                Bucket = AgeBucket(asOf.DayNumber - DateOnly.FromDateTime(i.IssuedAtUtc.UtcDateTime).DayNumber),
                Currency = i.BillingCurrencyCode,
            })
            .Select(g => new InvoiceAgingRowResponse(g.Key.Bucket, g.Key.Currency,
                g.Count(), g.Sum(i => i.Outstanding)))
            .OrderBy(r => BucketOrder(r.Bucket)).ThenBy(r => r.CurrencyCode).ToList();
        return new(asOf, rows);
    }

    private static string AgeBucket(int ageDays) => ageDays switch
    {
        <= 30 => "0-30", <= 60 => "31-60", <= 90 => "61-90", _ => "90+",
    };

    private static int BucketOrder(string bucket) => bucket switch
    {
        "0-30" => 0, "31-60" => 1, "61-90" => 2, _ => 3,
    };
}
