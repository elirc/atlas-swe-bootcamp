using Atlas.Domain.Entities;

namespace Atlas.Domain.Services;

public record PayrollMoney(decimal GrossSalary, decimal EmployerCost, decimal EmployeeDeductions, decimal Reimbursements,
    decimal BenefitsEmployerCost, decimal BenefitsEmployeeDeduction, decimal UnusedLeavePayout, decimal NetPay, decimal TotalCost)
{
    public static PayrollMoney Zero => new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    public static PayrollMoney From(Payslip p) => new(p.GrossSalary, p.EmployerCost, p.EmployeeDeductions, p.Reimbursements,
        p.BenefitsEmployerCost, p.BenefitsEmployeeDeduction, p.UnusedLeavePayout, p.NetPay, p.TotalCost);
    public static PayrollMoney operator +(PayrollMoney a, PayrollMoney b) => new(a.GrossSalary + b.GrossSalary, a.EmployerCost + b.EmployerCost,
        a.EmployeeDeductions + b.EmployeeDeductions, a.Reimbursements + b.Reimbursements, a.BenefitsEmployerCost + b.BenefitsEmployerCost,
        a.BenefitsEmployeeDeduction + b.BenefitsEmployeeDeduction, a.UnusedLeavePayout + b.UnusedLeavePayout, a.NetPay + b.NetPay, a.TotalCost + b.TotalCost);
    public static PayrollMoney operator -(PayrollMoney a, PayrollMoney b) => new(a.GrossSalary - b.GrossSalary, a.EmployerCost - b.EmployerCost,
        a.EmployeeDeductions - b.EmployeeDeductions, a.Reimbursements - b.Reimbursements, a.BenefitsEmployerCost - b.BenefitsEmployerCost,
        a.BenefitsEmployeeDeduction - b.BenefitsEmployeeDeduction, a.UnusedLeavePayout - b.UnusedLeavePayout, a.NetPay - b.NetPay, a.TotalCost - b.TotalCost);
}

public record ExpenseCalculationInput(Guid ClaimId, int Version, decimal Amount);
public record BenefitCalculationInput(Guid EnrollmentId, Guid PlanId, decimal MonthlyCost, decimal EmployerContributionRate,
    decimal EmployerShare, decimal EmployeeShare);
public record PayrollCalculationInput(Guid ContractId, int ContractVersion, Guid ClientId, Guid WorkerId,
    string CountryCode, string CurrencyCode, int Year, int Month, DateOnly StartDate, DateOnly? EndDate,
    Guid? SalaryRecordId, DateOnly? SalaryEffectiveDate, bool SalaryFallback, decimal MonthlySalary,
    decimal EmployerCostRate, decimal EmployeeDeductionRate, int? AnnualAllowance, int ReservedAnnualDays,
    List<ExpenseCalculationInput> Expenses, List<BenefitCalculationInput> Benefits);
public record PayrollCalculationDocument(int SchemaVersion, string CalculatorVersion, PayrollCalculationInput Inputs, PayrollMoney Outputs);
public record PayrollCalculatedContract(PayrollCalculationInput Inputs, PayrollMoney Amounts,
    List<CalculatedCostAllocation>? Allocations = null, Guid? AllocationScheduleId = null);
public record PayrollAssessmentBlocker(string Code, string Message, Guid? ClientId = null);
public record EstimatedBilling(Guid ClientId, string CurrencyCode, string BillingCurrencyCode, decimal PayrollSubtotal,
    decimal ManagementFeeRate, Guid? FeeScheduleId, decimal ManagementFee, decimal Total, decimal? FxRate, decimal? TotalInBillingCurrency);
public record PayrollAssessment(string CountryCode, int Year, int Month, DateTimeOffset AssessedAtUtc,
    List<PayrollCalculatedContract> Contracts, List<EstimatedBilling> EstimatedBilling,
    List<PayrollAssessmentBlocker> CreationBlockers, List<PayrollAssessmentBlocker> CompletionBlockers, List<PayrollAssessmentBlocker> Warnings);

/// <summary>Versioned pure arithmetic used for new calculations and saved-evidence replay.</summary>
public static class PayrollAssessmentCalculator
{
    public const string Version = "atlas-payroll-v1";
    public static PayrollMoney Calculate(PayrollCalculationInput input)
    {
        var salary = input.MonthlySalary; var payout = 0m;
        if (input.EndDate is { } end && end.Year == input.Year && end.Month == input.Month)
        {
            var final = BaseFinalPay.Calculate(salary, end, input.AnnualAllowance, input.ReservedAnnualDays);
            salary = final.EstimatedBaseTotal; payout = final.UnusedLeavePayout;
        }
        var country = new Country { Code = input.CountryCode, Name = input.CountryCode, CurrencyCode = input.CurrencyCode,
            EmployerCostRate = input.EmployerCostRate, EmployeeDeductionRate = input.EmployeeDeductionRate };
        var basePay = PayrollCalculator.Calculate(salary, country);
        var reimbursement = PayrollCalculator.RoundMoney(input.Expenses.Sum(x => x.Amount));
        var employerBenefits = input.Benefits.Sum(x => x.EmployerShare);
        var employeeBenefits = input.Benefits.Sum(x => x.EmployeeShare);
        return new(basePay.Gross, basePay.EmployerCost, basePay.EmployeeDeductions, reimbursement, employerBenefits,
            employeeBenefits, payout, basePay.NetPay - employeeBenefits + reimbursement, basePay.TotalCost + employerBenefits + reimbursement);
    }

    public static EstimatedBilling Bill(Guid clientId, string currency, string billingCurrency, IEnumerable<PayrollMoney> amounts,
        SelectedFee fee, decimal? fxRate)
    {
        var rows = amounts.ToList(); var subtotal = rows.Sum(x => x.TotalCost);
        var managementFee = PayrollCalculator.RoundMoney(rows.Sum(x => x.GrossSalary) * fee.Rate);
        var total = subtotal + managementFee;
        return new(clientId, currency, billingCurrency, subtotal, fee.Rate, fee.ScheduleId, managementFee, total, fxRate,
            fxRate is null ? null : PayrollCalculator.RoundMoney(total * fxRate.Value));
    }
}
