# 08: Reason about two requests, not just one

**Outcome:** explain a stale write, a duplicate retry, and a cross-row invariant as different problems. Read [IVersioned.cs](../../src/Atlas.Domain/IVersioned.cs), the version bump in [AtlasDbContext.cs](../../src/Atlas.Infrastructure/AtlasDbContext.cs), and [ConcurrencyEdgeTests.cs](../../tests/Atlas.Tests/Integration/ConcurrencyEdgeTests.cs).

## Draw the timeline

Suppose contexts A and B both load a pending request at version 3. A approves it and saves version 4. B cancels its stale copy. EF's update checks the originally loaded version; B's update matches no row and raises a concurrency exception. Atlas converts it to 409.

The domain's state guard catches sequential double decisions. The version check catches a decision made on stale loaded state. Neither implies that all possible races are solved.

Two new leave requests can both read “five days remaining,” reserve four days each, and insert different rows. Their own version tokens cannot conflict because they update no shared existing row. A stronger guarantee may require a shared balance/version row, suitable transaction isolation, or another database-backed coordination policy. Investigate the exact service before choosing one.

## Transactions and retries answer different questions

A transaction groups changes that should commit or roll back together. Inspect [PayrollService.cs](../../src/Atlas.Infrastructure/PayrollService.cs): run creation, expense reimbursement state, completion, and invoices have business relationships. Identify each `SaveChangesAsync` and describe what would exist if failure occurred just before it.

A caller may time out after a successful commit. Retrying can produce a duplicate operation unless the server has a durable identity for it. A 409 from a unique payroll-period constraint is useful, but it is not automatically a replay of the original successful response.

An idempotency design must define the key's scope, storage lifetime, request fingerprint, concurrent duplicates, response replay, and mismatched reuse. Blindly retrying every exception can duplicate work or repeatedly attempt an invalid transition.

## Exercise: write three failure stories

1. Two contexts update the same request. Identify the winning persisted state and losing response.
2. Two requests create overlapping reservations after reading the same balance. Identify why row versions alone may be insufficient.
3. Payroll commits, but the client never sees the response. Define how the client can discover the outcome before retrying.

For each, specify a test schedule. Use explicit load/save ordering or synchronization rather than sleeps. Do not “fix” a race with a process-local lock without explaining multiple application instances.

**Completion check:** can cancellation prove a write did not commit? Which constraint protects duplicate payroll periods? What evidence would you need before retrying a failed transaction?

Next: [operations and diagnostics](09-operations-and-diagnostics.md).
