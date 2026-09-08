# 05: Make a defect reproducible

**Outcome:** turn “this looks wrong” into a small, repeatable failing example. Read [testing.md](../testing.md), [AtlasApiFactory.cs](../../tests/Atlas.Tests/Integration/AtlasApiFactory.cs), and [ReportQueryTests.cs](../../tests/Atlas.Tests/Integration/ReportQueryTests.cs).

## Choose the boundary that can expose the failure

Use a domain test for arithmetic or an object's state transition. Use an integration test when the risk involves HTTP binding, authorization, EF translation, database constraints, or middleware ordering. Mocking the database cannot demonstrate that SQLite translates a date comparison correctly.

The factory hosts the real API against a held-open in-memory SQLite connection. The connection lifetime matters: closing the last connection destroys that database. Tests seed their own data and test keys; they do not rely on your local `atlas.db`.

Some existing classes share a factory through `IClassFixture`, so rows can survive between methods in that class. New report-boundary examples create their own factories to avoid cross-test report totals. Isolation has startup cost; use it deliberately.

## A debugging loop you can repeat

1. State the observation and the expected result separately.
2. Reduce the input to the smallest example that still fails.
3. Predict the failing layer before opening more files.
4. Inspect actual values at that boundary.
5. Write a regression that fails for the observed reason.
6. Make the smallest adequate fix, run the focused test, then affected suites.

For the old report bug, `toMonth=13` identified its error as `fromMonth`. A status-only assertion would miss that contract defect. `InvalidPeriods_ReturnFieldSpecific400` checks the field as well as 400 and the problem content type.

## Tests should discriminate

An empty report can prove that a date binds successfully. It cannot prove that invoices after the chosen date are excluded. The aging test seeds neighboring bucket boundaries, a future invoice, a second currency, and a timestamp whose local date differs from UTC. Each input distinguishes a plausible wrong implementation.

Avoid sleep-based tests for calendar behavior. Fix the date or inject a clock. When testing a race, explicitly arrange which contexts loaded which version before either save.

## Exercise: challenge a test

On an experiment branch, change one aging boundary from `<= 30` to `< 30`. Predict the exact assertion that should fail. Run:

```powershell
dotnet test Atlas.slnx --filter FullyQualifiedName~ReportQueryTests
```

Restore your experiment and verify the test passes. If no test fails, improve the test data rather than adding an assertion copied from the implementation. Do not use a broad reset that discards unrelated work.

**Completion check:** explain one defect your test catches and one it cannot catch. Why is 100% coverage insufficient? When is a new test unnecessary for a reversible documentation or naming change?

Next: [safe refactoring](06-safe-refactoring.md).
