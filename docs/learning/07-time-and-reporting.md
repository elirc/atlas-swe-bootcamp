# 07: Dates are inputs, and reports need semantics

**Outcome:** make a report reproducible without pretending it is a historical ledger. Read [ReportService.cs](../../src/Atlas.Api/Reporting/ReportService.cs), [ReportQueryValidation.cs](../../src/Atlas.Api/Reporting/ReportQueryValidation.cs), and the date tests in [ReportQueryTests.cs](../../tests/Atlas.Tests/Integration/ReportQueryTests.cs).

## “Today” is an external dependency

A report using the machine's current date can change when nothing in your code or database changes. Endpoints now accept `asOf` for invoice aging and both compliance-expiry routes. When omitted, the report routes obtain the UTC date from an injected `TimeProvider`. A test can replace that provider; [Microsoft documents custom clock implementations](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview).

This refactor is intentionally limited to report-date resolution and the operational expiry route. Entity creation timestamps and several other workflows still use system time. Do not assume that replacing the registered clock freezes the entire application.

## Define the boundary before writing arithmetic

An expiry report includes already expired documents and documents expiring on or before `asOf + withinDays`. A document expiring exactly on `asOf` is `ExpiringSoon`, not yet `Expired`. A null expiry never qualifies. A window of zero is valid.

`DateOnly.AddDays` can overflow. A nonnegative integer is not enough: `9999-12-31 + 1 day` is unrepresentable. Validation checks the number of days available before calling it. At the maximum date, explicitly supply `withinDays=0`; the normal default window does not fit.

Invoice aging uses the UTC issue date and includes the complete selected day. Age 30 belongs to `0-30`; 31 starts the next bucket. Age 90 belongs to `61-90`; the legacy label `90+` means strictly over 90. Future issues are excluded, and currencies remain separate.

## An as-of date is not time travel

The report uses rows currently stored in the database. A compliance document added today can appear when evaluating an earlier date. There is no record of what the system knew then. Invoice aging filters by issue time, but has no payment ledger and still treats every included invoice as outstanding.

Requirements such as “show what finance saw at last month's close” need snapshots or historical data and precise correction policies. A query parameter alone cannot supply those facts.

## Exercise: boundary examples

Calculate expected results for ages 0, 30, 31, 60, 61, 90, and 91. Add one issue at `2026-07-01T01:00:00+02:00` with `asOf=2026-06-30`; explain why it is included. Compare the result with the integration test.

Call a compliance report with `withinDays=2147483647`. Explain why the response is now a field-specific 400 instead of an unexpected server exception. Then write a design question for a future report that needs local-country dates instead of UTC dates.

Next: [concurrency and reliability](08-concurrency-and-reliability.md).
