using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

/// <summary>
/// Orchestrates payroll: creating a run computes a payslip for every contract that
/// covers the month; completing a run issues one invoice per client involved.
/// </summary>
public class PayrollService
{
    private readonly AtlasDbContext _db;
    private readonly TimeProvider _clock;

    public PayrollService(AtlasDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates a draft payroll run for the country/month and computes its payslips.
    /// Contracts are paid for a full month when their employment period covers any
    /// part of that calendar month (simplified: no day-based proration). Approved,
    /// not-yet-reimbursed expense claims are paid out with the run and marked
    /// Reimbursed (added untaxed to net pay and billed on to the client).
    /// </summary>
    public async Task<PayrollRun> CreateRunAsync(string countryCode, int year, int month)
    {
        await using var transaction = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync() : null;
        PayrollAssessment assessment;
        try { assessment = await new PayrollAssessmentService(_db, _clock).AssessAsync(countryCode, year, month); }
        catch (KeyNotFoundException ex) { throw new DomainException(ex.Message); }
        var run = await PrepareRunAsync(assessment);
        await _db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        return run;
    }

    /// <summary>Stages snapshots and reimbursements. The caller must save within its transaction.</summary>
    public async Task<PayrollRun> PrepareRunAsync(PayrollAssessment assessment)
    {
        if (assessment.CreationBlockers.Count > 0) throw new DomainException(assessment.CreationBlockers[0].Message);
        if (await _db.PayrollRuns.AnyAsync(x => x.CountryCode == assessment.CountryCode && x.Year == assessment.Year && x.Month == assessment.Month))
            throw new DomainException("A payroll run for this country and period already exists.");
        var ids = assessment.Contracts.Select(x => x.Inputs.ContractId).ToList();
        var contracts = await _db.Contracts.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var expenseIds = assessment.Contracts.SelectMany(x => x.Inputs.Expenses).Select(x => x.ClaimId).ToList();
        var expenses = await _db.ExpenseClaims.Where(x => expenseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var now = _clock.GetUtcNow();
        var run = new PayrollRun { CountryCode = assessment.CountryCode, Year = assessment.Year, Month = assessment.Month, CreatedAtUtc = now };
        foreach (var calculated in assessment.Contracts)
        {
            var input = calculated.Inputs; var amount = calculated.Amounts;
            if (!contracts.TryGetValue(input.ContractId, out var contract) || contract.Version != input.ContractVersion)
                throw new DomainException("A contract changed after assessment. Reassess payroll.");
            foreach (var selected in input.Expenses)
            {
                if (!expenses.TryGetValue(selected.ClaimId, out var claim) || claim.Version != selected.Version || claim.Status != ExpenseClaimStatus.Approved)
                    throw new DomainException("An expense changed after assessment. Reassess payroll.");
                claim.MarkReimbursed(run.Id, now);
            }
            var slip = new Payslip { PayrollRunId = run.Id, ContractId = input.ContractId, ClientId = input.ClientId, WorkerId = input.WorkerId,
                CurrencyCode = input.CurrencyCode, GrossSalary = amount.GrossSalary, EmployerCost = amount.EmployerCost,
                EmployeeDeductions = amount.EmployeeDeductions, Reimbursements = amount.Reimbursements, BenefitsEmployerCost = amount.BenefitsEmployerCost,
                BenefitsEmployeeDeduction = amount.BenefitsEmployeeDeduction, UnusedLeavePayout = amount.UnusedLeavePayout, NetPay = amount.NetPay, TotalCost = amount.TotalCost };
            var document = new PayrollCalculationDocument(1, PayrollAssessmentCalculator.Version, input, amount);
            var json = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new DomainException("Calculation evidence exceeds 256 KiB.");
            run.Payslips.Add(slip);
            _db.PayslipCostAllocations.AddRange((calculated.Allocations ?? CostAllocation.Calculate(amount, []))
                .Select(x => CostAllocation.Snapshot(slip.Id, calculated.AllocationScheduleId, input.CurrencyCode, x)));
            _db.PayslipCalculationEvidence.Add(new PayslipCalculationEvidence { PayslipId = slip.Id, SchemaVersion = 1,
                CalculatorVersion = PayrollAssessmentCalculator.Version, EvidenceJson = json, CapturedAtUtc = now });
            _db.Entry(contract).Property(x => x.Version).IsModified = true;
        }
        _db.PayrollRuns.Add(run);
        return run;
    }

    /// <summary>
    /// Completes a draft run and issues one invoice per client: payroll subtotal
    /// (gross + employer costs + reimbursements) plus the client's management fee
    /// on gross, in the payroll country's currency, converted into the client's
    /// billing currency at the FX rate effective for the payroll period.
    /// </summary>
    public async Task<(PayrollRun Run, List<Invoice> Invoices)> CompleteRunAsync(Guid runId)
    {
        await using var transaction = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync() : null;
        var now = _clock.GetUtcNow();
        var issueDay = DateOnly.FromDateTime(now.UtcDateTime);
        var run = await _db.PayrollRuns
            .Include(r => r.Payslips)
            .SingleOrDefaultAsync(r => r.Id == runId)
            ?? throw new KeyNotFoundException($"Payroll run '{runId}' does not exist.");

        run.Complete(now); // throws DomainException unless Draft

        var clientIds = run.Payslips.Select(p => p.ClientId).Distinct().ToList();
        var clients = await _db.Clients.Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var terms = await _db.ClientBillingTerms.AsNoTracking().Where(x => clientIds.Contains(x.ClientId)).ToListAsync();
        var schedules = await _db.ClientFeeSchedules.AsNoTracking().Where(x => clientIds.Contains(x.ClientId)).ToListAsync();

        var localCurrency = run.Payslips[0].CurrencyCode;
        var billingCurrencies = clients.Values
            .Select(c => c.BillingCurrencyCode)
            .Where(code => code != localCurrency)
            .Distinct()
            .ToList();
        var rates = await _db.FxRates
            .Where(r => r.BaseCurrencyCode == localCurrency && billingCurrencies.Contains(r.QuoteCurrencyCode))
            .ToListAsync();

        var invoices = new List<Invoice>();
        var sequence = 1;
        foreach (var clientId in clientIds.OrderBy(id => clients[id].Name))
        {
            var client = clients[clientId];
            var slips = run.Payslips.Where(p => p.ClientId == clientId).ToList();
            var selectedFee = BillingConfiguration.Fee(client, schedules, run.Year, run.Month);
            var selectedTerms = BillingConfiguration.Terms(clientId, terms, issueDay);
            if (issueDay.DayNumber > DateOnly.MaxValue.DayNumber - selectedTerms.Days)
                throw new DomainException("The payment terms would produce a due date beyond the supported calendar.");

            var fxRate = 1m;
            if (client.BillingCurrencyCode != localCurrency)
            {
                var effective = FxRate.EffectiveForMonth(
                    rates.Where(r => r.QuoteCurrencyCode == client.BillingCurrencyCode), run.Year, run.Month)
                    ?? throw new DomainException(
                        $"No FX rate from {localCurrency} to {client.BillingCurrencyCode} is effective for {run.Year}-{run.Month:00}; " +
                        $"add one before completing the run.");
                fxRate = effective.Rate;
            }

            var billing = PayrollAssessmentCalculator.Bill(clientId, localCurrency, client.BillingCurrencyCode,
                slips.Select(PayrollMoney.From), selectedFee, fxRate);
            invoices.Add(new Invoice
            {
                InvoiceNumber = $"INV-{run.Year}{run.Month:00}-{run.CountryCode}-{sequence:000}",
                ClientId = clientId,
                PayrollRunId = run.Id,
                CurrencyCode = localCurrency,
                PayrollSubtotal = billing.PayrollSubtotal,
                ManagementFee = billing.ManagementFee,
                Total = billing.Total,
                BillingCurrencyCode = client.BillingCurrencyCode,
                FxRateApplied = fxRate,
                TotalInBillingCurrency = billing.TotalInBillingCurrency!.Value,
                IssuedAtUtc = now,
                PaymentTermDays = selectedTerms.Days,
                DueDate = issueDay.AddDays(selectedTerms.Days),
                BillingTermsId = selectedTerms.TermsId,
                ManagementFeeRateApplied = selectedFee.Rate,
                FeeScheduleId = selectedFee.ScheduleId,
            });
            sequence++;
        }

        _db.Invoices.AddRange(invoices);
        await _db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        return (run, invoices);
    }
}
