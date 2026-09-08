# Payroll: calculate one person's pay before studying a whole run

**One objective:** distinguish the calculator, the orchestration service, and the saved result.

## Start with a fictional calculation

Assume gross pay is 1,000 currency units, the employer cost rate is 12%, and the employee deduction rate is 15%. Ignore benefits, expenses, final pay, and currency conversion for this first example.

| Amount | Calculation | Result |
| --- | --- | --- |
| Gross | Starting salary | 1,000 |
| Employer cost | 1,000 × 0.12 | 120 |
| Employee deductions | 1,000 × 0.15 | 150 |
| Net pay | 1,000 − 150 | 850 |
| Total employer cost | 1,000 + 120 | 1,120 |

Say it aloud: “The worker receives gross minus deductions. Employing the worker costs gross plus employer cost.” This example describes the base calculator, not all the additions in a complete payslip or invoice.

## See the same idea in code

From [PayrollCalculator.cs](../src/Atlas.Domain/Services/PayrollCalculator.cs):

```csharp
var gross = RoundMoney(grossMonthlySalary);
var employerCost = RoundMoney(gross * country.EmployerCostRate);
var deductions = RoundMoney(gross * country.EmployeeDeductionRate);
var net = gross - deductions;
var totalCost = gross + employerCost;
```

`RoundMoney` uses decimal arithmetic and rounds to two decimal places away from zero. The method rejects a nonpositive input salary. It returns `PayrollAmounts`; it does not read SQL, save a payslip, or return HTTP.

## Now zoom out one level

[PayrollService.CreateRunAsync](../src/Atlas.Infrastructure/PayrollService.cs) gathers payable contracts and their related data. It selects salary terms effective for the period, handles final-month pay when relevant, calls the calculator, incorporates expense and benefit amounts, constructs payslips, and saves the run.

This is orchestration: coordinating several steps. The calculator performs one calculation inside that larger job.

Completing the run is a separate operation. `CompleteRunAsync` creates invoices grouped by client, applies management fees and any required FX conversion, and saves. The API endpoint turns service results into HTTP responses.

## Read the nouns again

- `PayrollAmounts`: returned values from a calculation.
- `Payslip`: one worker's saved pay result for a run.
- `PayrollRun`: the country/month operation containing the payslips.
- `Invoice`: the bill to a client, produced when a run completes.

A run can hold several payslips. Creating a run and completing a run are different operations. The stored payslips are snapshots; later amendments do not automatically recalculate them.

## Checkpoint

For gross 2,000 with the same rates, predict employer cost, deductions, net, and total cost. Then read `Calculate_RoundNumbers_ProducesExpectedBreakdown` in [PayrollCalculatorTests.cs](../tests/Atlas.Tests/Domain/PayrollCalculatorTests.cs). Its numbers differ, but its relationships are the same. Answers are on [page 13](13-answers.md).
