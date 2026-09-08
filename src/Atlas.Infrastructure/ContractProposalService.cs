using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public record TerminationEligibility(DateOnly NoticeDate, DateOnly EarliestAllowedEnd);
public class ContractProposalService(AtlasDbContext db, TimeProvider clock)
{
    public async Task CheckAmendmentAsync(EmploymentContract contract, DateOnly effectiveDate)
    {
        if (contract.Status != ContractStatus.Active) throw new DomainException($"Only active contracts can be amended; this contract is {contract.Status}.");
        if (effectiveDate < contract.StartDate) throw new DomainException($"The effective date cannot be before the contract start date {contract.StartDate:yyyy-MM-dd}.");
        if (await db.ContractAmendments.AnyAsync(x => x.ContractId == contract.Id && x.Status == AmendmentStatus.Pending))
            throw new DomainException("This contract already has a pending amendment; decide or cancel it first.");
        if (contract.MonthlySalary <= 0) throw new DomainException("The current contract salary is invalid; correct its data before proposing a change.");
    }

    public async Task<TerminationEligibility> CheckTerminationAsync(EmploymentContract contract, DateOnly proposedEnd)
    {
        if (contract.Status != ContractStatus.Active) throw new DomainException($"Only active contracts can be terminated; this contract is {contract.Status}.");
        if (await db.TerminationRequests.AnyAsync(x => x.ContractId == contract.Id && x.Status == TerminationRequestStatus.Pending))
            throw new DomainException("This contract already has a pending termination request; decide or cancel it first.");
        var country = await db.Countries.FindAsync(contract.CountryCode) ?? throw new DomainException("The contract country is missing.");
        var notice = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (country.MinimumNoticeDays < 0 || country.MinimumNoticeDays > DateOnly.MaxValue.DayNumber - notice.DayNumber)
            throw new DomainException("The country's notice period exceeds supported date arithmetic.");
        var earliest = TerminationRequest.EarliestAllowedEndDate(notice, country.MinimumNoticeDays);
        if (proposedEnd < earliest) throw new DomainException($"{country.Name} requires {country.MinimumNoticeDays} day(s) of notice: with notice given {notice:yyyy-MM-dd}, the earliest end date is {earliest:yyyy-MM-dd}.");
        if (proposedEnd < contract.StartDate) throw new DomainException($"The end date cannot be before the contract start date {contract.StartDate:yyyy-MM-dd}.");
        return new(notice, earliest);
    }

    public async Task<object> TerminationPreviewAsync(EmploymentContract contract, DateOnly end)
    {
        var eligibility = await CheckTerminationAsync(contract, end);
        var history = await db.SalaryRecords.AsNoTracking().Where(x => x.ContractId == contract.Id).ToListAsync();
        var selected = SalaryRecord.EffectiveForMonth(history, end.Year, end.Month);
        var salary = selected?.MonthlySalary ?? contract.MonthlySalary;
        var policy = await db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.CountryCode == contract.CountryCode);
        var reservations = await db.LeaveRequests.AsNoTracking().Where(x => x.ContractId == contract.Id && x.Type == LeaveType.Annual
            && x.StartDate.Year == end.Year && (x.Status == LeaveRequestStatus.Pending || x.Status == LeaveRequestStatus.Approved)).Select(x => x.Days).ToListAsync();
        var adjustments = await db.LeaveAllowanceAdjustments.AsNoTracking().Where(x => x.ContractId == contract.Id && x.Year == end.Year && x.Type == LeaveType.Annual)
            .Select(x => x.DeltaDays).ToListAsync();
        int? allowance = policy is null ? null : policy.AnnualLeaveDays + adjustments.Sum();
        var reserved = reservations.Sum();
        var amounts = BaseFinalPay.Calculate(salary, end, allowance, reserved);
        return new { contractId = contract.Id, proposedEndDate = end, eligibility.NoticeDate, eligibility.EarliestAllowedEnd,
            contract.CurrencyCode, selectedMonthlySalary = salary, selectedEffectiveDate = selected?.EffectiveDate,
            amounts.ProratedBaseSalary, annualAllowance = allowance, reservedAnnualDays = reserved,
            amounts.UnusedAnnualDays, amounts.DailyRate, amounts.UnusedLeavePayout, amounts.EstimatedBaseTotal,
            leavePolicyConfigured = policy is not null, scope = "Prorated base salary and unused annual leave only; excludes deductions, employer contributions, expenses, benefits and invoice fees." };
    }
}
