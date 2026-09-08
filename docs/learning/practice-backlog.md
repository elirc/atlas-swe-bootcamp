# Practice backlog: increasingly independent changes

These are exercises, not claims of implemented features. Work in small branches; choose one task at a time. Each task includes a concrete outcome, acceptance criteria, a starting point, and a review question.

## Guided tasks

### L01 — Make a misleading enum input fail clearly

**Outcome:** an undefined numeric compliance-document type is rejected.

- Inspect the `Enum.TryParse` check in [ComplianceEndpoints.cs](../../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs). Determine what happens for `"999"` before changing it.
- Require a supported enum value, preserve case-insensitive names, and return a field-specific 400.
- Test one valid name, one undefined numeric value, and whitespace. Confirm the rejected request creates no row.
- Review: should numeric values of defined members be accepted? Write a policy before implementing it.

### L02 — Put deterministic ordering under test

**Outcome:** paging through a list with duplicate display names does not repeat or skip existing rows in a stable dataset.

- Inspect [ClientEndpoints.cs](../../src/Atlas.Api/Endpoints/ClientEndpoints.cs) and its ordering.
- Seed equal sort values, add an explicit unique tie-breaker if needed, and test page membership.
- Review: why does this not guarantee a stable snapshot when rows are inserted between page requests?

### L03 — Extend the clock seam to one workflow

**Outcome:** one currently wall-clock-dependent workflow can be tested at a chosen instant.

- Inventory direct time reads in a single endpoint file; avoid a repository-wide replacement.
- Inject the clock at orchestration boundaries, pass time to existing domain methods, and cover the default behavior.
- Review: which timestamps represent an event instant, and which represent a calendar date?

### L04 — Improve an error message without weakening access control

**Outcome:** one validation failure tells a legitimate caller how to correct it.

- Find an ambiguous field error in the API reference and source.
- Demonstrate the bad request, improve the response, and verify cross-client requests retain their existing status.
- Review: can the additional message reveal a resource that should be invisible?

## Independent feature tasks

### M01 — Export a scoped invoice list

**Outcome:** a client can download its own invoices as CSV.

- Define columns and use invariant date/decimal formatting; correctly escape commas, quotes, and newlines.
- Reuse the existing tenant policy and define how spreadsheet formula-leading text is handled.
- Decide whether the export is bounded, paged, or streamed. Test another client's ID, an empty result, and cancellation.
- Review: does streaming reduce database memory usage if the entire query is already materialized?

### M02 — Give reports an explicit workload budget

**Outcome:** an oversized report request receives a clear response before expensive processing.

- Measure a representative query first. Propose a maximum period span or another concrete bound.
- Document defaults and exact-boundary behavior; preserve valid client/country filters.
- Test just inside and outside the chosen limit. Explain compatibility for callers currently requesting all history.
- Review: when would an asynchronous export be more appropriate than a larger timeout?

### M03 — Add a schema-aware readiness check

**Outcome:** readiness distinguishes database connectivity from presence of required application schema.

- Reproduce a reachable empty SQLite database that cannot serve a business query.
- Add the smallest useful read probe, keep liveness independent, and avoid exposing database exceptions publicly.
- Test a healthy database, a missing-schema database, and an unavailable database. Do not create schema in the check.
- Review: how often will it run, and what work does each probe add?

### M04 — Protect a cross-row leave invariant

**Outcome:** two competing reservations cannot overdraw the same allowance.

- Write a deterministic concurrency reproduction based on [LeaveService.cs](../../src/Atlas.Infrastructure/LeaveService.cs).
- Propose a shared concurrency boundary or transactional approach suited to the chosen database.
- Prove the final stored reservations stay within allowance; define the losing caller's response.
- Review: does the fix still work with two application instances?

### M05 — Preserve request diagnostics under unusual clients

**Outcome:** errors remain diagnosable when requests are cancelled or ask for unusual response media types.

- Characterize the current behavior before choosing policy.
- Cover a cancelled request and an `Accept` header the default problem writer does not support.
- Decide whether fallback errors remain JSON, use another representation, or have an empty body; document it.
- Review: what status can be truthfully logged if the connection ends after headers were sent?

## Capstones in design and ownership

### S01 — Add invoice payment records and historical balances

**Outcome:** partial payments reduce aging totals without erasing financial history.

- Define authorization, billing currency, overpayment, payment time, recording time, corrections, and idempotency.
- Write examples for 100 owed, 40 paid, a duplicate retry, a wrong-currency payment, and a reversal.
- Plan schema evolution before writing to an existing database. Implement one useful vertical slice, then historical aggregation.
- Prove current and as-of balances, client isolation, and retry behavior. Describe reconciliation and operational metrics.
- Review: can you reproduce what was known at an earlier close, or only what is effective at that date?

### S02 — Make payroll completion safely retryable

**Outcome:** a retried completion can recover the original successful outcome under an explicit idempotency contract.

- Define key scope, request fingerprint, concurrent first attempts, mismatch errors, expiration, and crash recovery.
- Store the coordination state durably; do not use a process-local dictionary as the guarantee.
- Test commit-before-response loss and concurrent duplicates. Preserve one invoice per client/run.
- Review: which operations are atomic together, and what happens if the process dies between them?

### S03 — Introduce migrations without losing existing data

**Outcome:** a documented upgrade path preserves contracts, payroll snapshots, invoices, and API users.

- Inventory the current `EnsureCreated` schema and decide how an existing database enters migration history.
- Work on a disposable copy, specify backup/restore verification, and test empty and populated upgrades.
- Include old/new application compatibility, a backfill policy, and a rollback limitation.
- Review: which steps are reversible, and what proof establishes data preservation?

### S04 — Design a secure API-key lifecycle

**Outcome:** move beyond stored plaintext keys while allowing deliberate rotation.

- Inspect [ApiUser.cs](../../src/Atlas.Domain/Entities/ApiUser.cs) and authentication lookup. Separate lookup identity from verifier material.
- Design storage, rotation overlap, revocation, migration, and auditing. Define how old development keys are handled.
- Test active/revoked/rotating credentials and prevent secret material appearing in logs or list responses.
- Review: what threat does each control address, and what happens if the database is disclosed?

### S05 — Design a durable report export job

**Outcome:** a large report can finish after the initiating HTTP request ends.

- Define job ownership, immutable input parameters, progress, cancellation, expiry, and download authorization.
- Specify what happens when a worker crashes, an execution repeats, or the source data changes during the job.
- Implement a bounded first slice after measuring synchronous report limits; test recovery and cross-client isolation.
- Review: what does “the report for June” mean if records change between enqueueing and execution?

For every task, include the original problem, examples, changed behavior, test evidence, and one limitation in your review note. A small completed task with clear reasoning is more useful evidence than a large unfinished rewrite.
