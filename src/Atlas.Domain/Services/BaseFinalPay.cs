namespace Atlas.Domain.Services;

public record BaseFinalPayAmounts(decimal ProratedBaseSalary, decimal DailyRate, int? UnusedAnnualDays, decimal UnusedLeavePayout, decimal EstimatedBaseTotal);
public static class BaseFinalPay
{
    public static BaseFinalPayAmounts Calculate(decimal salary, DateOnly end, int? annualAllowance, int reservedAnnualDays)
    {
        var prorated = FinalPayCalculator.ProrateFinalMonth(salary, end);
        var daily = FinalPayCalculator.DailyRate(salary);
        int? unused = annualAllowance is null ? null : Math.Max(0, annualAllowance.Value - reservedAnnualDays);
        var payout = unused is null ? 0m : FinalPayCalculator.UnusedLeavePayout(salary, unused.Value);
        return new(prorated, daily, unused, payout, prorated + payout);
    }
}
