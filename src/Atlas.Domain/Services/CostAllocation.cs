using System.Globalization;
using System.Numerics;
using Atlas.Domain.Entities;

namespace Atlas.Domain.Services;

public record AllocationTarget(Guid? CostCenterId, string Code, string Name, int BasisPoints);
public record CalculatedCostAllocation(AllocationTarget Target, PayrollMoney Amounts);

/// <summary>Apportion primitive amounts in cents, then derive totals so each row reconciles.</summary>
public static class CostAllocation
{
    public static List<CalculatedCostAllocation> Calculate(PayrollMoney money, IReadOnlyList<AllocationTarget> targets)
    {
        if (targets.Count == 0) return [new(new(null, "UNALLOCATED", "Unallocated", 10000), money)];
        if (targets.Count > 10 || targets.Any(x => x.BasisPoints <= 0 || x.BasisPoints > 10000 || x.CostCenterId is null)
            || targets.Sum(x => x.BasisPoints) != 10000 || targets.Select(x => x.CostCenterId).Distinct().Count() != targets.Count)
            throw new DomainException("Allocations require distinct cost centers and positive weights totaling 10,000 basis points.");
        var ordered = targets.OrderBy(x => x.CostCenterId).ToList();
        var basic = Split(money.GrossSalary - money.UnusedLeavePayout, ordered);
        var leave = Split(money.UnusedLeavePayout, ordered);
        var employer = Split(money.EmployerCost, ordered);
        var deduction = Split(money.EmployeeDeductions, ordered);
        var reimbursements = Split(money.Reimbursements, ordered);
        var benefitEmployer = Split(money.BenefitsEmployerCost, ordered);
        var benefitEmployee = Split(money.BenefitsEmployeeDeduction, ordered);
        return ordered.Select((target, i) =>
        {
            var gross = basic[i] + leave[i];
            return new CalculatedCostAllocation(target, new(gross, employer[i], deduction[i], reimbursements[i],
                benefitEmployer[i], benefitEmployee[i], leave[i], gross - deduction[i] - benefitEmployee[i] + reimbursements[i],
                gross + employer[i] + reimbursements[i] + benefitEmployer[i]));
        }).ToList();
    }

    private static decimal[] Split(decimal amount, List<AllocationTarget> targets)
    {
        if (amount < 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("Configured cost allocations require nonnegative primitive amounts with at most two decimal places.");
        // BigInteger avoids overflow from multiplying a valid decimal amount by 100 or by a weight.
        var cents = BigInteger.Parse(amount.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", ""), CultureInfo.InvariantCulture);
        var portions = targets.Select(x => BigInteger.DivRem(cents * x.BasisPoints, 10000, out _)).ToArray();
        var remainders = targets.Select(x => cents * x.BasisPoints % 10000).ToArray();
        var remaining = (int)(cents - portions.Aggregate(BigInteger.Zero, (a, b) => a + b));
        foreach (var index in Enumerable.Range(0, targets.Count).OrderByDescending(i => remainders[i]).ThenBy(i => targets[i].CostCenterId).Take(remaining)) portions[index]++;
        return portions.Select(x =>
        {
            var value = (decimal)(x / 100) + (decimal)(x % 100) / 100m;
            var represented = BigInteger.Parse(value.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", ""), CultureInfo.InvariantCulture);
            if (represented != x) throw new DomainException("A cost allocation exceeds decimal's exact cent precision. Use smaller payroll amounts.");
            return value;
        }).ToArray();
    }

    public static PayslipCostAllocation Snapshot(Guid payslipId, Guid? scheduleId, string currency, CalculatedCostAllocation row) => new()
    {
        PayslipId = payslipId, ScheduleId = scheduleId, CostCenterId = row.Target.CostCenterId,
        BucketKey = row.Target.CostCenterId?.ToString("D") ?? "unallocated", Code = row.Target.Code, Name = row.Target.Name,
        CurrencyCode = currency, BasisPoints = row.Target.BasisPoints, GrossSalary = row.Amounts.GrossSalary,
        EmployerCost = row.Amounts.EmployerCost, EmployeeDeductions = row.Amounts.EmployeeDeductions,
        Reimbursements = row.Amounts.Reimbursements, BenefitsEmployerCost = row.Amounts.BenefitsEmployerCost,
        BenefitsEmployeeDeduction = row.Amounts.BenefitsEmployeeDeduction, UnusedLeavePayout = row.Amounts.UnusedLeavePayout,
        NetPay = row.Amounts.NetPay, TotalCost = row.Amounts.TotalCost,
    };
}
