# 03: Model rules before endpoints

**Outcome:** express an invariant and protect it through a state transition. Read [EmploymentContract.cs](../../src/Atlas.Domain/Entities/EmploymentContract.cs), [LeaveRequest.cs](../../src/Atlas.Domain/Entities/LeaveRequest.cs), and [PayrollCalculatorTests.cs](../../tests/Atlas.Tests/Domain/PayrollCalculatorTests.cs).

An invariant is a condition that must remain true for valid business state. “This request has a salary field” is a shape check. “Net plus deductions equals gross under the calculator's rounding policy” is a monetary invariant. “Only a pending request can be approved” is a transition rule.

## Write the state machine

For contracts, draw `Draft → Active → Terminated`. Beside each arrow write the prerequisites and effects. Activation needs completed required onboarding; termination records an end date and reason. Ask which rules need database knowledge and which can be checked on one object.

A domain object can reject double activation without knowing HTTP. The endpoint maps failures to responses; the persistence layer saves changes. Keeping business rules free of HTTP makes them easier to exercise directly.

Some invariants span rows: one non-terminated contract per worker or non-overlapping leave reservations. A method on one entity cannot, by itself, establish a guarantee over all rows. Those rules require orchestration and a concurrency strategy. See [lesson 08](08-concurrency-and-reliability.md).

## Money has meaning as well as a type

Two values of `100m` in different currencies cannot be summed into a meaningful single total. `ReportService` groups costs and invoice aging by currency. Invoices preserve the FX rate applied when the run completed. Recomputing old invoices using a new rate would change their meaning.

Likewise, do not blindly sum a displayed breakdown. `UnusedLeavePayout` is already part of taxable gross in final pay; adding it again to gross would double count it. Trace how each field is constructed before aggregating it.

## Exercise: transition table

Choose `LeaveRequest`. Make a table with initial state, action, expected state, and expected failure. Cover approve, reject, and cancel from pending and from at least one decided state. Compare with [LifecycleTransitionMatrixTests.cs](../../tests/Atlas.Tests/Domain/LifecycleTransitionMatrixTests.cs).

Then propose a new rule in plain language, such as a rejection-note requirement. Determine whether it already exists before changing code. Identify its correct owner, a happy-path example, and a failing example. Avoid duplicating an existing test just to increase a count.

**Completion check:** explain a rule that belongs on one entity and a rule that needs a query. Explain what callers could bypass through public setters, and what database enforcement could and could not protect.

Next: [SQL and EF Core](04-sql-and-ef-core.md).
