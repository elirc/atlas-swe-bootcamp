using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public record LeaveBalance(LeaveType Type, int AllowanceDays, int ApprovedDays, int PendingDays, int RemainingDays,
    int BaseAllowanceDays = 0, int AdjustmentDays = 0);
public record LeaveBlocker(string Code, string Message);
public record LeaveAssessment(int RequestedWorkingDays, int? AllowanceDays, int? RemainingDays, List<LeaveBlocker> Blockers)
{
    public bool Eligible => Blockers.Count == 0;
}

/// <summary>Shared assessment for quotes and writes; a quote reserves nothing.</summary>
public class LeaveService(AtlasDbContext db, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<LeaveAssessment> AssessAsync(EmploymentContract contract, LeaveType type, DateOnly start, DateOnly end)
    {
        var blockers = new List<LeaveBlocker>();
        if (contract.Status != ContractStatus.Active) blockers.Add(new("contract-not-active", $"Leave can only be requested on active contracts; this contract is {contract.Status}."));
        if (start.Year != end.Year) blockers.Add(new("cross-year", "A leave request must fall within a single calendar year."));
        if (start < contract.StartDate)
            blockers.Add(new("outside-contract", "Leave cannot start before the contract start date."));
        if (contract.EndDate is { } ending && end > ending)
            blockers.Add(new("outside-contract", "Leave cannot end after the contract end date."));
        var holidays = await db.CountryHolidays.AsNoTracking().Where(x => x.CountryCode == contract.CountryCode && x.Date >= start && x.Date <= end)
            .Select(x => x.Date).ToListAsync();
        var days = LeaveCalculator.CountWorkingDays(start, end, holidays.ToHashSet());
        if (days == 0) blockers.Add(new("no-working-days", "The requested period contains no working days."));
        var policy = await db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.CountryCode == contract.CountryCode);
        var existing = await db.LeaveRequests.AsNoTracking().Where(x => x.ContractId == contract.Id
            && (x.Status == LeaveRequestStatus.Pending || x.Status == LeaveRequestStatus.Approved)).ToListAsync();
        var overlap = existing.FirstOrDefault(x => x.Overlaps(start, end));
        if (overlap is not null) blockers.Add(new("overlap", $"The requested period overlaps an existing {overlap.Status.ToString().ToLowerInvariant()} leave request ({overlap.StartDate:yyyy-MM-dd} to {overlap.EndDate:yyyy-MM-dd})."));
        int? allowance = null, remaining = null;
        if (policy is null) blockers.Add(new("missing-policy", $"No leave policy is configured for country '{contract.CountryCode}'."));
        else
        {
            var adjustments = await db.LeaveAllowanceAdjustments.AsNoTracking().Where(x => x.ContractId == contract.Id && x.Year == start.Year && x.Type == type)
                .Select(x => x.DeltaDays).ToListAsync();
            allowance = policy.AllowanceFor(type) + adjustments.Sum();
            remaining = allowance - existing.Where(x => x.Type == type && x.StartDate.Year == start.Year).Sum(x => x.Days);
            if (days > remaining) blockers.Add(new("insufficient-balance", $"Insufficient {type.ToString().ToLowerInvariant()} leave balance for {start.Year}: requested {days} day(s) but only {remaining} remain(s) of {allowance}."));
        }
        return new(days, allowance, remaining, blockers);
    }

    public async Task<LeaveRequest> CreateRequestAsync(EmploymentContract contract, LeaveType type, DateOnly startDate, DateOnly endDate, string? reason)
    {
        var assessment = await AssessAsync(contract, type, startDate, endDate);
        if (!assessment.Eligible) throw new DomainException(assessment.Blockers[0].Message);
        var request = new LeaveRequest { ContractId = contract.Id, Type = type, StartDate = startDate, EndDate = endDate,
            Days = assessment.RequestedWorkingDays, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), RequestedAtUtc = _clock.GetUtcNow() };
        db.LeaveRequests.Add(request);
        db.Entry(contract).Property(x => x.Version).IsModified = true;
        await db.SaveChangesAsync();
        return request;
    }

    public async Task<List<LeaveBalance>> GetBalancesAsync(EmploymentContract contract, int year)
    {
        var policy = await db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(p => p.CountryCode == contract.CountryCode)
            ?? throw new DomainException($"No leave policy is configured for country '{contract.CountryCode}'.");
        var requests = await db.LeaveRequests.AsNoTracking().Where(r => r.ContractId == contract.Id
            && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)).ToListAsync();
        var adjustments = await db.LeaveAllowanceAdjustments.AsNoTracking().Where(x => x.ContractId == contract.Id && x.Year == year).ToListAsync();
        return Enum.GetValues<LeaveType>().Select(type =>
        {
            var inYear = requests.Where(r => r.Type == type && r.StartDate.Year == year).ToList();
            var approved = inYear.Where(r => r.Status == LeaveRequestStatus.Approved).Sum(r => r.Days);
            var pending = inYear.Where(r => r.Status == LeaveRequestStatus.Pending).Sum(r => r.Days);
            var delta = adjustments.Where(x => x.Type == type).Sum(x => x.DeltaDays);
            var allowance = policy.AllowanceFor(type) + delta;
            return new LeaveBalance(type, allowance, approved, pending, allowance - approved - pending, policy.AllowanceFor(type), delta);
        }).ToList();
    }
}
