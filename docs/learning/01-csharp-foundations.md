# 01: C# through real backend code

**Outcome:** explain the language constructs you will use in a small feature. Read [Payslip.cs](../../src/Atlas.Domain/Entities/Payslip.cs), [PayrollCalculator.cs](../../src/Atlas.Domain/Services/PayrollCalculator.cs), and [ReportService.cs](../../src/Atlas.Api/Reporting/ReportService.cs).

## Types describe promises

`Guid` is an identifier, not proof that a row exists or that the caller owns it. `string?` allows null; a nullable annotation warns the compiler and reader but does not validate incoming JSON. `required` asks C# callers to initialize a member; it does not prove that a name is nonblank or a salary positive. Runtime rules still matter.

`decimal` represents the monetary quantities in this project. The `m` in `100.01m` makes the literal decimal. Rounding is a business decision: read the calculator to see exactly where it happens. `DateOnly` represents a calendar date. `DateTimeOffset` represents an instant together with an offset. Do not treat them as interchangeable strings.

Response `record` types collect named values and have value-based equality. Entity classes have identity and are tracked by EF. Neither records nor classes automatically make nested lists immutable. In particular, public entity setters allow callers to bypass a method's guard; recognize that limitation when reading the model.

## Follow a collection pipeline

In `ReportService.PayrollCostsAsync`, `Where` narrows rows and `Select` chooses values. Before `ToListAsync`, the query is an expression EF can translate to SQL. After it, `GroupBy` and `Sum` operate on an in-memory collection. Ask where each operation executes before reasoning about performance.

`async`/`await` lets a request await I/O without synchronously blocking that request's thread. It does not make one database query inherently faster. A `CancellationToken` lets downstream work observe cancellation. It does not guarantee that a write never reached the database.

## Exercise: explain, then change

1. Find one nullable value checked before use and one null-forgiving `!`. Explain the evidence that makes the latter safe, or identify what assumption it relies on.
2. Write down the type of the value returned by `ToListAsync` in the report query. Explain why it cannot be used as a completed list before `await`.
3. In a disposable experiment, calculate `Math.Round(1.005m, 2, MidpointRounding.AwayFromZero)` and predict the result first. Compare to `ToEven`. Explain why an explicit policy belongs in payroll code.
4. Follow an existing payroll test's Arrange/Act/Assert shape to cover a nearby rounding boundary. Keep the test if it protects a distinct business outcome.

**Completion check:** explain why a non-null `Guid`, a `required string`, and a passing compiler are each insufficient to validate a request. Explain the difference between missing data, invalid data, and an unauthorized caller.

Next: [HTTP and authorization](02-http-and-authorization.md).
