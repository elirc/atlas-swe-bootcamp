using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

/// <summary>Loads bounded inputs without mutations. The caller owns the read/write transaction.</summary>
public class PayrollAssessmentService(AtlasDbContext db, TimeProvider clock)
{
    public async Task<PayrollAssessment> AssessAsync(string countryCode, int year, int month, CancellationToken ct = default)
    {
        var country = await db.Countries.AsNoTracking().SingleOrDefaultAsync(x => x.Code == countryCode, ct)
            ?? throw new KeyNotFoundException($"Country '{countryCode}' is not supported.");
        var monthStart = new DateOnly(year, month, 1); var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var contracts = await db.Contracts.AsNoTracking().Where(x => x.CountryCode == countryCode && x.Status != ContractStatus.Draft
            && x.StartDate <= monthEnd && (x.EndDate == null || x.EndDate >= monthStart)).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (contracts.Count > 1000) throw new DomainException("Payroll assessment supports at most 1,000 payable contracts.");
        var creation = new List<PayrollAssessmentBlocker>(); var completion = new List<PayrollAssessmentBlocker>(); var warnings = new List<PayrollAssessmentBlocker>();
        if (await db.PayrollRuns.AnyAsync(x => x.CountryCode == countryCode && x.Year == year && x.Month == month, ct))
            creation.Add(new("run-already-exists", $"A payroll run for {countryCode} {year}-{month:00} already exists."));
        if (contracts.Count == 0) creation.Add(new("no-payable-contracts", $"No payable contracts in {countryCode} for {year}-{month:00}."));
        var ids = contracts.Select(x => x.Id).ToList(); var clientIds = contracts.Select(x => x.ClientId).Distinct().ToList();
        var salaries = await db.SalaryRecords.AsNoTracking().Where(x => ids.Contains(x.ContractId)).ToListAsync(ct);
        var expenses = await db.ExpenseClaims.AsNoTracking().Include(x => x.Items).Where(x => ids.Contains(x.ContractId) && x.Status == ExpenseClaimStatus.Approved).ToListAsync(ct);
        var enrollments = await db.BenefitEnrollments.AsNoTracking().Include(x => x.BenefitPlan).Where(x => ids.Contains(x.ContractId)).ToListAsync(ct);
        var policy = await db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.CountryCode == countryCode, ct);
        var leave = await db.LeaveRequests.AsNoTracking().Where(x => ids.Contains(x.ContractId) && x.Type == LeaveType.Annual && x.StartDate.Year == year
            && (x.Status == LeaveRequestStatus.Pending || x.Status == LeaveRequestStatus.Approved)).ToListAsync(ct);
        var adjustments = await db.LeaveAllowanceAdjustments.AsNoTracking().Where(x => ids.Contains(x.ContractId) && x.Year == year && x.Type == LeaveType.Annual).ToListAsync(ct);
        var clients = await db.Clients.AsNoTracking().Where(x => clientIds.Contains(x.Id)).ToListAsync(ct);
        var schedules = await db.ClientFeeSchedules.AsNoTracking().Where(x => clientIds.Contains(x.ClientId)).ToListAsync(ct);
        var currencies = clients.Select(x => x.BillingCurrencyCode).Distinct().ToList();
        var rates = await db.FxRates.AsNoTracking().Where(x => x.BaseCurrencyCode == country.CurrencyCode && currencies.Contains(x.QuoteCurrencyCode)).ToListAsync(ct);
        var calculated = new List<PayrollCalculatedContract>();
        var allocations = await db.ContractAllocationSchedules.AsNoTracking().Include(x => x.Items).Where(x => ids.Contains(x.ContractId)).ToListAsync(ct);
        var centers = await db.ClientCostCenters.AsNoTracking().Where(x => clientIds.Contains(x.ClientId)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var contract in contracts)
        {
            var selected = SalaryRecord.EffectiveForMonth(salaries.Where(x => x.ContractId == contract.Id), year, month);
            var ending = contract.EndDate is { } end && end.Year == year && end.Month == month;
            int? allowance = ending && policy is not null ? policy.AnnualLeaveDays + adjustments.Where(x => x.ContractId == contract.Id).Sum(x => x.DeltaDays) : null;
            if (ending && policy is null) warnings.Add(new("missing-leave-policy", $"Contract {contract.Id} has no configured annual allowance; unused-leave payout is zero.", contract.ClientId));
            var input = new PayrollCalculationInput(contract.Id, contract.Version, contract.ClientId, contract.WorkerId, contract.CountryCode, contract.CurrencyCode,
                year, month, contract.StartDate, contract.EndDate, selected?.Id, selected?.EffectiveDate, selected is null, selected?.MonthlySalary ?? contract.MonthlySalary,
                country.EmployerCostRate, country.EmployeeDeductionRate, allowance, ending ? leave.Where(x => x.ContractId == contract.Id).Sum(x => x.Days) : 0,
                expenses.Where(x => x.ContractId == contract.Id).OrderBy(x => x.Id).Select(x => new ExpenseCalculationInput(x.Id, x.Version, x.TotalAmount)).ToList(),
                enrollments.Where(x => x.ContractId == contract.Id && x.CoversMonth(year, month)).OrderBy(x => x.Id)
                    .Select(x => new BenefitCalculationInput(x.Id, x.BenefitPlanId, x.BenefitPlan!.MonthlyCost, x.BenefitPlan.EmployerContributionRate, x.BenefitPlan.EmployerShare, x.BenefitPlan.EmployeeShare)).ToList());
            var amounts = PayrollAssessmentCalculator.Calculate(input);
            var schedule = allocations.Where(x => x.ContractId == contract.Id && x.Year * 12 + x.Month <= year * 12 + month)
                .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
            var targets = schedule?.Items.Select(x => new AllocationTarget(x.CostCenterId, centers[x.CostCenterId].Code, centers[x.CostCenterId].Name, x.BasisPoints)).ToList() ?? [];
            calculated.Add(new(input, amounts, CostAllocation.Calculate(amounts, targets), schedule?.Id));
        }
        var billing = new List<EstimatedBilling>();
        foreach (var client in clients.OrderBy(x => x.Id))
        {
            decimal? fx = client.BillingCurrencyCode == country.CurrencyCode ? 1m
                : FxRate.EffectiveForMonth(rates.Where(x => x.QuoteCurrencyCode == client.BillingCurrencyCode), year, month)?.Rate;
            if (fx is null) completion.Add(new("missing-fx", $"No FX rate from {country.CurrencyCode} to {client.BillingCurrencyCode} is effective for {year}-{month:00}.", client.Id));
            billing.Add(PayrollAssessmentCalculator.Bill(client.Id, country.CurrencyCode, client.BillingCurrencyCode,
                calculated.Where(x => x.Inputs.ClientId == client.Id).Select(x => x.Amounts), BillingConfiguration.Fee(client, schedules, year, month), fx));
        }
        return new(countryCode, year, month, clock.GetUtcNow(), calculated, billing, creation, completion, warnings);
    }
}
