# 20 mid/senior feature stories: extend Atlas with clear system boundaries

**These are feature requirements and implementation plans; current implementation status is tracked in the [bootcamp register](bootcamp/progress.md).** This workbook adds 20 stories beyond the [junior stories](16-junior-user-stories.md) and [mid-level stories](17-midlevel-feature-stories.md). The original plans can be read independently of the earlier backlogs. Required dependencies below refer to stories in this workbook; the bootcamp also records integration work across workbooks.

The intended progression is from understanding a workflow to owning its behavior under change: two callers arrive together, a process restarts, prices change, a report is replayed, or a request succeeds but its response is lost. The features are bounded enough for one engineer to implement in small reviewable steps. The steps make the route through the work explicit; the underlying problems still deserve careful thought and review.

Read one story in three passes. First, explain its user benefit without mentioning classes. Second, trace the request through the named source files. Third, draw the successful commit and one failed attempt. Put differently: **what is promised, where is it enforced, and what evidence proves it?** Repeat that exercise after implementation using your actual code.

## Pick a route through the workbook

| Story | New capability | Main engineering lesson | Schema change | Required dependency |
| --- | --- | --- | --- | --- |
| [SS-01](#ss-01-full-payroll-simulation) | Full payroll simulation | Separate calculation from effects | No | None |
| [SS-02](#ss-02-payslip-calculation-evidence) | Payslip calculation evidence | Preserve inputs as well as outputs | Yes | SS-01 |
| [SS-03](#ss-03-payroll-period-variance-report) | Payroll period variance report | Explain differences without inventing causes | No | None |
| [SS-04](#ss-04-retry-safe-payroll-completion) | Retry-safe payroll completion | Atomic results and request identity | Yes | None |
| [SS-05](#ss-05-durable-background-payroll-creation) | Durable background payroll creation | Leases, fencing, and recovery | Yes | SS-01 |
| [SS-06](#ss-06-invoice-credit-note-ledger) | Invoice credit-note ledger | Compensating financial records | Yes | None |
| [SS-07](#ss-07-scheduled-billing-terms-and-due-dates) | Scheduled billing terms and due dates | Effective configuration and issued snapshots | Yes | None |
| [SS-08](#ss-08-scheduled-management-fee-rates) | Scheduled management fee rates | Period-based commercial policy | Yes | None |
| [SS-09](#ss-09-contract-cost-center-allocations) | Contract cost-center allocations | Exact allocation and snapshot capture | Yes | None |
| [SS-10](#ss-10-cost-center-payroll-report) | Cost-center payroll report | Reconciliation across historical dimensions | No | SS-09 |
| [SS-11](#ss-11-validate-and-commit-a-worker-import) | Validate and commit a worker import | Staging is not a reservation | Yes | None |
| [SS-12](#ss-12-compliance-document-renewal-history) | Compliance document renewal history | Supersession without erasing history | Yes | None |
| [SS-13](#ss-13-country-document-coverage-policies) | Country document coverage policies | Configuration-driven assessment | Yes | None |
| [SS-14](#ss-14-client-payroll-budgets) | Client payroll budgets | Explicit actuals and currency boundaries | Yes | None |
| [SS-15](#ss-15-typed-contract-custom-fields) | Typed contract custom fields | Evolving user-defined data safely | Yes | None |
| [SS-16](#ss-16-contract-decision-audit-feed) | Contract decision audit feed | Atomic business evidence | Yes | None |
| [SS-17](#ss-17-client-notification-inbox) | Client notification inbox | Durable delivery and deduplication | Yes | SS-16 |
| [SS-18](#ss-18-background-client-data-export) | Background client data export | Consistent scoped exports and expiry | Yes | SS-05 |
| [SS-19](#ss-19-frozen-workforce-snapshots-and-comparison) | Frozen workforce snapshots and comparison | Reproducible reads | Yes | None |
| [SS-20](#ss-20-two-approver-expense-policy) | Two-approver expense policy | Snapshot policy and aggregate decisions | Yes | None |

For an initial read-focused path, do SS-03, SS-01, then SS-19. For financial modeling, do SS-07, SS-08, SS-09, then SS-10. For background processing, do SS-01, SS-05, then SS-18. For workflow evidence, do SS-16 then SS-17. Do not start all four paths together.

## Shared working agreement

### Read the actual boundaries

Atlas currently has three production projects. [Domain](../src/Atlas.Domain/Atlas.Domain.csproj) contains entities and pure rules. [Infrastructure](../src/Atlas.Infrastructure/Atlas.Infrastructure.csproj) contains EF and workflow services. [API](../src/Atlas.Api/Atlas.Api.csproj) translates HTTP, handles caller permissions, and exposes read models. Start at [Program.cs](../src/Atlas.Api/Program.cs) to see registration and routing.

Read [AtlasDbContext](../src/Atlas.Infrastructure/AtlasDbContext.cs), [AuthorizationExtensions](../src/Atlas.Api/Auth/AuthorizationExtensions.cs), [AtlasApiFactory](../tests/Atlas.Tests/Integration/AtlasApiFactory.cs), and one existing test next to the workflow you choose. These are current files. New entity, service, DTO, and route names in the stories are proposed additions. Create a dedicated endpoint file when the feature would otherwise overwhelm an existing one, and map its group in Program.

Important current behavior to preserve unless a story explicitly changes it:

- Creating a payroll run calculates payslips **and marks selected expenses Reimbursed**. Completing the run issues invoices. Calling creation is never a harmless preview.
- Payroll chooses salary through `SalaryRecord.EffectiveForMonth`; the current Contract salary alone is not the historical payroll basis. Benefits charge a full premium for any covered part of a month.
- Payslips and invoices contain stored financial results. Later configuration changes do not justify rewriting them.
- API identities are API-user rows, not verified human identities. ClientAdmin and ClientViewer are scoped to one client; PlatformAdmin is not client-scoped.
- The database is SQLite. Existing decimal reports project filtered values then aggregate in memory. Never convert money to floating point just to make a query translate.
- `IVersioned` entities receive an increment when modified in SaveChanges. Child-only changes do not automatically cause their parent to be updated.

### Make each API promise explicit

For new client-owned routes, authorize the parent before returning children: missing or foreign parent is 404, an authenticated viewer's forbidden business write is 403, and absent authentication is 401. Platform-only routes require the existing platform policy. Scope collection queries before sorting, paging, counts, or sums. A worker shared by two clients does not grant either client access to the other's contracts or payslips.

Use 400 for invalid input, 409 for conflicting state/version, 201 for a new synchronously created resource, and 202 for an accepted background operation. Successful reads and the explicit replay cases return 200. Use 413 for request bodies beyond an explicitly stated byte limit. Map only recognized database uniqueness/concurrency failures to business conflicts. Other database failures must retain their actual failure meaning without exposing raw SQL or secrets.

All new money uses decimal. Normalize currency codes by trimming and invariant uppercase, then validate against the story's permitted currency set. Use DateOnly for calendar dates, UTC for instants, and injected TimeProvider for new clock decisions. New period inputs accept years 2000–2100 and months 1–12; validate before arithmetic. Unless stated otherwise, ranges are inclusive and date-time cutoffs represent UTC calendar days. Pass CancellationToken into asynchronous database operations.

Each story's **Acceptance criteria** are a completion checklist. Its **Test scenarios** provide minimum evidence, not a substitute for thinking about the implementation you actually wrote. Use explicit response DTOs and preserve existing fields. Additive fields require updating response constructors and affected tests.

### Use a deliberate transaction boundary

When child writes affect a parent invariant, load the versioned parent **before** reading the child values used to make the decision. Explicitly mark the parent's Version property modified when only children changed, and save both together. An expectedVersion input catches a stale caller; an EF concurrency token catches a change after your read. Database uniqueness protects duplicate creation. These are different protections.

An in-memory lock is not a substitute for a database rule. Every competing path must participate in the same invariant. If a workflow currently saves internally but a new feature needs an atomic companion row, separate preparation from persistence or use a correctly scoped explicit transaction. Do not let a receipt, audit event, or job status commit independently from the business effect it describes.

For conflict tests, use two contexts against the same test database and control the order of reads and saves. The current AtlasApiFactory shares one open SQLite connection: use a disposable database with separate connections for tests of overlapping transactions, leases, or read snapshots. Reuse its authentication/setup patterns, but do not mistake two contexts on one connection for two independent database sessions. For crash/recovery tests, invoke a worker's “process one item” method with a fixed clock and injected failure point. Do not test races by adding sleeps. SQLite lock contention is distinct from an optimistic version conflict; do not translate every lock or I/O failure into a misleading successful replay. These local exercises do not prove behavior on another database engine.

### Choose the schema lane before editing entities

Current development startup uses `EnsureCreated`. It creates a new database; it does **not** upgrade an existing one. Read [ADR 0001](../docs/adr/0001-sqlite-with-ensurecreated.md).

For the bounded learning implementation, list the new columns, keys, indexes, and delete behaviors first. Implement the model and use the test factory's fresh database. For manual testing, set `ConnectionStrings__Atlas` to a new, uniquely named SQLite file and run the existing Development startup. Preserve your original database. Check the new relationships and constraints by reloading from a fresh context. State in your PR that existing-database upgrade was not delivered.

For an existing populated installation, plan and verify migration adoption separately. Establish the initial migration baseline from the unchanged model before the feature edits, then add feature migrations and test on a disposable copy. An EnsureCreated database has no migration history to adopt automatically. Never present “restart the app” or “run the initial create migration over the old database” as an upgrade procedure. Where a story introduces history, legacy nulls must mean “not captured,” not invented historical facts.

### Finish one small vertical slice, then broaden

1. Write a concrete happy-path fixture and a failing behavior test.
2. Implement input/authorization, then the rule, then persistence, then response mapping.
3. Add the stated edge, conflict, retry, and no-write tests that apply. Read persisted state after failed writes.
4. Run the focused tests and the affected existing workflow tests. When finished, run the full suite once; investigate any regressions.
5. Add request/response, permissions, error, and schema notes to [the API reference](../docs/api-reference.md). Add an ADR only for a decision that future work needs to preserve.
6. Review the diff and explain one tradeoff out loud before submitting your PR.

Example verification commands, run from the repository root after you implement a story:

```powershell
dotnet test tests/Atlas.Tests/Atlas.Tests.csproj --filter "FullyQualifiedName~YourFeatureTests"
dotnet test tests/Atlas.Tests/Atlas.Tests.csproj -c Release
python scripts/learn.py check-docs
```

Replace YourFeatureTests with the class you added. Use the SDK selected by `global.json`; if dotnet is absent from PATH, use your installed executable. Consult [testing](../docs/testing.md) and [getting started](../docs/getting-started.md) for the existing setup. The commands above are learner instructions, not claims that these future tests already exist.

## SS-01: Full payroll simulation

**User story.** As a platform payroll operator, I want to simulate an entire country's payroll for a month so that I can inspect pay components and billing blockers before creating a run or consuming approved expense claims.

**Current gap and learning.** PayrollService combines data loading, calculations, object creation, reimbursement transitions, and saving. You will separate a repeatable assessment from its effects. This is broader than the earlier base final-pay preview: it includes every selected contract, expenses, benefits, deductions, and estimated invoices.

**Read first.** [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), [PayrollCalculator](../src/Atlas.Domain/Services/PayrollCalculator.cs), [FinalPayCalculator](../src/Atlas.Domain/Services/FinalPayCalculator.cs), [SalaryRecord](../src/Atlas.Domain/Entities/SalaryRecord.cs), and [PayrollEdgeCaseTests](../tests/Atlas.Tests/Integration/PayrollEdgeCaseTests.cs).

**Feature contract.** Add platform-only POST `/api/payroll-simulations` with countryCode/year/month. Return resolved period, assessedAtUtc, per-contract calculated components, per-client estimated billing, and separate `creationBlockers` and `completionBlockers`. An existing run blocks creation but does not prevent displaying a simulation of current inputs. Missing FX blocks an estimated invoice, not the local payslip calculation. Return 200 for a valid assessment with blockers, 404 for an unknown country, and 400 for malformed inputs. Cap selected contracts at 1000; reject an oversized assessment clearly. No schema change or dependency.

**Acceptance criteria.** Repeated simulation inserts nothing, changes no versions, and leaves expense statuses untouched. An immediate actual creation with unchanged relevant inputs produces the same payslip components. Contract coverage, effective salaries, final-month proration, annual leave reservation, and benefit overlap follow existing rules. If a final-pay leave policy is missing, preserve current payroll's zero leave payout and expose a warning. Billing estimates clearly use current fee/FX selection and are not issued invoices.

**Implementation plan.**

1. Build a fixture with two clients, an ordinary contract, a terminating contract, an approved expense, a benefit enrollment, and a future salary record.
2. Annotate each line of CreateRunAsync as read, pure calculation, entity mutation, or save. In particular, locate MarkReimbursed.
3. Introduce input/result records for a payroll assessment. Domain calculators accept plain data; Infrastructure loads the data; API maps the assessment.
4. Extract contract and component calculation without constructing tracked PayrollRun/Payslip entities or changing ExpenseClaims.
5. Extract invoice amount calculation from completion into reusable arithmetic while keeping FX/fee loading explicit. A missing rate produces a blocker with currency pair and client ID.
6. Load simulation inputs inside a consistent read transaction, apply the contract cap before loading large child sets, and calculate from that captured input set.
7. Make actual creation reuse the calculation path, then explicitly create snapshots, reimburse selected claims, and save through its existing write boundary. Preserve its business rejection behavior.
8. Add the simulation route and deterministic ordering by contract/client ID. Return no synthetic invoice numbers or promises of later approval.
9. Test component equality before creation, not after reimbursed expenses have been consumed. Verify simulation and actual creation have intentionally different side effects.
10. Document why a simulation can become stale and why completion estimates can differ if fees or FX change before actual completion.

**Test scenarios.** Simulate twice with a 50.00 approved claim: both include it and the claim remains Approved. Create a run afterward: the claim becomes Reimbursed once. Remove required FX: local pay remains available and estimated billing has a blocker. Add an already existing run: simulation works but `creationBlockers` reports it. Test no payable contracts, exact month boundaries, and 1000/1001 contracts.

**Explain it twice.** In code terms, identify the method that returns data without saving. In product terms, explain why an estimate is useful even though it does not reserve expenses or freeze prices.

## SS-02: Payslip calculation evidence

**User story.** As a client administrator, I want to inspect the inputs used for a saved payslip so that I can explain its numbers even after salaries, benefit prices, or country settings change.

**Current gap and learning.** Payslips preserve outputs, but they do not preserve every input or source choice. You will capture provenance at calculation time rather than attempt to reverse-engineer history from current tables.

**Read first.** Your SS-01 assessment records, [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), and [MoneyInvariantTests](../tests/Atlas.Tests/Domain/MoneyInvariantTests.cs). **Dependency:** SS-01. **Schema:** PayslipCalculationEvidence, unique PayslipId, with schemaVersion, capturedAtUtc, calculatorVersion, and a serialized explicit input/result document.

**Feature contract.** Add scoped GET `/api/payslips/{id}/calculation-evidence`. Authorize by Payslip.ClientId. New payroll creation captures selected salary record ID/date/amount or an explicit fallback flag, country rates, contract dates, annual policy/reserved days used for final pay, expense IDs and amounts, and enrollment/plan IDs with applied premium shares. Store the rounded output components too. Limit each document to 256 KiB and reject creation before saving if exceeded. For an existing payslip without evidence, return 200 with `available=false` and reason `not-captured`; never manufacture a snapshot from current values.

**Acceptance criteria.** Evidence and payslip commit together. Changing source data afterward changes neither response. No API key, receipt URL, free-text expense reason, or unrelated worker document appears. Replaying the stored inputs through the named calculator version reproduces stored outputs, or reports unsupported version explicitly. This is calculation evidence, not a digitally signed or tamper-proof audit record.

**Implementation plan.**

1. Write a version-1 evidence schema listing every numeric input and the formula stages. Include source IDs for navigation and copied values for reproducibility.
2. Keep calculatorVersion separate from JSON schemaVersion: the former identifies business math, the latter the storage shape.
3. Add the entity/index/foreign key and choose restricted deletion. Follow the common schema lane and retain legacy absence.
4. Build evidence from SS-01's already loaded assessment inputs. Do not run a second query after saving the payslip to reconstruct them.
5. Capture final-pay salary before proration and the payout separately, since GrossSalary already includes unused leave payout.
6. Serialize through an allowlisted DTO, enforce the byte bound, and save evidence with payslip/run/expense changes in one transaction.
7. Add an evidence reader that recognizes known schema/calculator versions and handles unknown versions without guessing.
8. Implement the scoped route and return a safe availability envelope for legacy rows.
9. Add replay tests using the saved evidence after deliberately changing source salary/rates/benefit values in the fixture.
10. Document that future calculator changes must retain the old replay implementation or explicitly mark it unsupported; they must not silently reinterpret old inputs.

**Test scenarios.** A payslip with 20.00 reimbursements and a 10.01 benefit split reproduces every saved component. Editing today's plan price afterward has no effect on evidence. Inject an evidence insert failure: no payslip or reimbursement transition commits. A foreign client receives 404 even when the same worker has an accessible contract. An old payslip returns `not-captured`.

**Explain it twice.** Show the source ID and copied source value side by side. Then explain to a payroll reviewer why keeping only an ID would not preserve the original calculation inputs.

## SS-03: Payroll period variance report

**User story.** As a platform payroll operator, I want to compare two saved runs for the same country so that I can identify new workers, departed contracts, and component changes before investigating an unexpected cost movement.

**Current gap and learning.** Existing reports total payroll by period but do not reconcile one run against another. You will build a full outer comparison over saved facts and avoid presenting a numeric difference as proof of its cause.

**Read first.** [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), [PayrollRun](../src/Atlas.Domain/Entities/PayrollRun.cs), [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), and [ReportEndpointTests](../tests/Atlas.Tests/Integration/ReportEndpointTests.cs).

**Feature contract.** Add platform GET `/api/reports/payroll-variance?baselineRunId=...&comparisonRunId=...`. Require different runs, the same country, and a later comparison period. Runs may be Draft or Completed; return both statuses. Union rows by ContractId and classify added, removed, or retained. Return baseline/comparison/delta for gross, employer cost, employee deductions, benefits, reimbursements, unused leave payout, net, and total cost. Missing-side numeric values are zero, accompanied by explicit presence flags. Group totals by currency; reject a retained contract whose two payslips have different currencies as a conflicting data state. No schema change or dependency.

**Acceptance criteria.** Sum of row TotalCost deltas equals comparison total minus baseline total for each currency. A contract replacement for the same worker is added/removed, not silently merged into one contract. UnusedLeavePayout is shown as a component already included in GrossSalary, not added again. The report says which components changed without asserting that a salary raise caused them.

**Implementation plan.**

1. Write a fixture with a retained contract, one only in the baseline, and one only in the comparison.
2. Validate IDs and load run metadata before payslip rows; handle missing runs and mismatched country/period explicitly.
3. Project only the required stored payslip fields, including ClientId and currency, using no tracking.
4. Create dictionaries keyed by ContractId and traverse their key union in stable order.
5. Construct presence/classification fields before applying zero defaults. A missing payslip and a real zero component must remain distinguishable.
6. Compute decimal component differences and per-currency totals. Assert the reconciliation identity in a pure helper test.
7. Keep this first route a country-run comparison without a clientId filter. Ensure response rows include ClientId so a platform reviewer can identify ownership without another lookup.
8. Add the report route and a response example explaining baseline direction, since reversing the two runs reverses every delta.
9. Test ties, empty sides, different contract IDs for one worker, and post-run changes to current salaries. Do not query current salary to fill missing history.
10. If SS-02 exists later, add links to evidence IDs as a separate enhancement; this story must work without it.

**Test scenarios.** Baseline costs A=100 and B=50; comparison A=120 and C=30 gives deltas A=20, B=-50, C=30 and total zero. A reimbursement decrease appears under reimbursements even if gross is unchanged. Compare Draft to Completed and verify the status labels. Unknown runs are 404; same ID, backwards period, or different countries are 400.

**Explain it twice.** Explain the union of dictionary keys to an engineer. Then tell a payroll operator why “cost increased by 20” does not establish why that increase happened.

## SS-04: Retry-safe payroll completion

**User story.** As a platform integration developer, I want to retry payroll completion after a timeout and receive the original successful result so that I do not have to guess whether invoices were already issued.

**Current gap and learning.** Completing an already Completed run currently conflicts. That is a valid state guard, but it cannot distinguish a retry of the same successful command. You will persist request identity and result in the same commit as completion.

**Read first.** `CompleteRunAsync` in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), [PayrollEndpoints](../src/Atlas.Api/Endpoints/PayrollEndpoints.cs), [ConcurrencyEdgeTests](../tests/Atlas.Tests/Integration/ConcurrencyEdgeTests.cs), and invoice unique indexes in DbContext.

**Feature contract.** Extend POST `/api/payroll-runs/{id}/complete` with optional `Idempotency-Key`, trimmed length 1–100 and case-sensitive. Requests without a key retain existing behavior. Key scope is authenticated ApiUserId plus operation name, so reuse for another run conflicts. Store a normalized command fingerprint and the exact versioned success response. Matching replay returns 200 with the original response; mismatched reuse returns 409. Retain receipts indefinitely in this learning slice. **Schema:** OperationReceipt with unique owner/operation/key and required response schema version; no secrets in receipt data.

**Acceptance criteria.** Completion, invoices, and receipt commit together or not at all. A failed attempt caused by missing FX consumes no key. Two equal simultaneous requests issue one invoice set and eventually return either the saved result or a retryable conflict while the winner is unresolved. Authorization is checked on every replay. A different key against an already completed run does not fabricate a successful receipt.

**Implementation plan.**

1. Define the fingerprint from normalized operation name and run ID, not raw JSON whitespace or a random request correlation ID.
2. Add receipt mapping and uniqueness. Store only successful results; no separately committed “started” receipt is needed for this synchronous slice.
3. Refactor completion preparation so its tracked run/invoices and receipt can share one SaveChanges or explicit transaction. Keep the unkeyed route's behavior intact.
4. Authorize, validate the key, and look up a receipt before attempting the state transition. Compare its fingerprint before replaying.
5. On first execution, calculate completion, project its final response, and attach the receipt before committing.
6. Ensure serialized response versions and generated IDs/timestamps correspond to the committed result. If saving changes response fields, serialize within the transaction after the business save and before transaction commit.
7. On recognized receipt uniqueness/run concurrency conflicts, discard the failed context and reload the receipt in a fresh one. Return an exact replay when available; otherwise return 409 with retry guidance.
8. Do not return raw stored responses before confirming the current caller is still authenticated and platform-authorized.
9. Test a simulated response-loss retry, concurrent keys, transaction failure, and fixing FX before retrying the same key.
10. Document that losing or changing the key loses replay identity; the command is not globally “exactly once” just because a header exists.

**Test scenarios.** Complete once with key `close-september`, discard the response, then retry: same invoice IDs and amounts, one receipt. Use that key for October: 409. Missing FX leaves no receipt and run Draft; adding FX permits the same key to succeed. Inject receipt serialization/save failure and verify that invoices and run completion roll back.

**Explain it twice.** Identify the unique constraint and atomic commit boundary. Then explain why remembering “this request succeeded” separately from issuing invoices would create another failure window.

## SS-05: Durable background payroll creation

**User story.** As a platform payroll operator, I want to submit payroll creation as a background job and check progress so that a long calculation can survive an HTTP timeout or application restart.

**Current gap and learning.** Creation currently runs entirely inside an HTTP request. You will add durable job state, recover expired work leases, and prevent an old worker from committing after another worker takes over. The slice creates a Draft run only; completion remains its existing operation.

**Read first.** Your SS-01 calculation separation, [PayrollEndpoints](../src/Atlas.Api/Endpoints/PayrollEndpoints.cs), [Program](../src/Atlas.Api/Program.cs), and [AtlasDbContext](../src/Atlas.Infrastructure/AtlasDbContext.cs). **Dependency:** SS-01. **Schema:** versioned BackgroundJob with operation kind, country/period operation key, requester ID, normalized payload, state, attempts, lease token/expiry, result run ID, and safe error code. Unique operation key for payroll creation.

**Feature contract.** Add platform POST `/api/payroll-jobs` and GET `/api/payroll-jobs/{id}`. States are Queued, Running, Succeeded, Failed. Duplicate submission for the same normalized country/period returns the existing job with 200, never a second job. First acceptance returns 202 with polling Location. Platform POST `/api/payroll-jobs/{id}/retry` moves Failed back to Queued using expectedVersion and returns 202; retrying another state conflicts. A hosted worker processes one job at a time per instance, with a 60-second renewable lease and a new random lease token per claim. PayrollRun's existing country/year/month uniqueness remains the final domain constraint.

**Acceptance criteria.** Restarting the API does not lose queued work. An expired Running job can be reclaimed. A stale worker cannot persist payroll after its lease has been replaced. Run, payslips, reimbursement transitions, and Succeeded/result ID commit together. A run created synchronously in the meantime yields `run-already-exists` with its ID, not a second run or a fake successful job. No job creates invoices.

**Implementation plan.**

1. Model job transitions as explicit methods and define which failures are terminal business failures versus transient infrastructure failures.
2. Add indexes for state/lease expiry and unique payroll operation key. Implement submission/polling/retry with platform authorization.
3. Expose a worker method that processes one job and can be invoked directly in tests; wrap it with a hosted-service loop using fresh DI scopes.
4. Claim Queued or expired Running work using a conditional database update/version check, increment attempts, assign a new lease token, and commit that short claim.
5. Calculate using a fresh context and SS-01's reusable logic. Renew the lease through separate short-lived contexts if needed; never run concurrent EF operations on one context.
6. At final persistence, start a transaction and conditionally verify/update the job using both current token and an unexpired lease. This database fence must participate in the same commit as payroll writes.
7. Revalidate business inputs as required by the write path, save the payroll snapshots/expense transitions and job result atomically, and roll back everything if the fence fails.
8. On a handled business failure, conditionally mark Failed only if still lease owner. For transient failures, use bounded retry: at most three claims, including expired-lease recoveries, per submission or explicit retry cycle before Failed. An explicit retry starts a new cycle while retaining total attempt metadata. Expose safe codes, not stack traces.
9. Test crash after claim, lease takeover during calculation, synchronous competing creation, and failure after payroll preparation but before commit.
10. Document that a lease alone does not stop an old process; the final fenced transaction is what prevents its effects. Add an operator example for polling and explicit retry after correcting data.

**Test scenarios.** Worker A claims, its lease expires, B reclaims, then A attempts final save: A persists no run or reimbursements. Crash before final commit and restart: the job can recover. Crash after commit but before responding: polling shows Succeeded and the same run ID. Two submissions produce one job. Missing payable contracts produces a readable business failure.

**Explain it twice.** Draw claim, calculate, fence, and commit as four separate steps. Then explain to an operator why Running does not guarantee the original process is still alive.

## SS-06: Invoice credit-note ledger

**User story.** As a platform billing operator, I want to issue a partial or full credit against an invoice so that a billing correction is visible without editing the original issued amounts.

**Current gap and learning.** Invoice totals are stored, but there is no correction ledger. This story is distinct from recording or reversing a payment: a credit changes the amount billed, while a payment records settlement. You will preserve that distinction in both data and wording.

**Read first.** [Invoice](../src/Atlas.Domain/Entities/Invoice.cs), [InvoiceEndpoints](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), and DbContext's versioning/index patterns. No dependency. **Schema:** versioned Invoice plus append-only InvoiceCreditNote with invoice ID, reference, billing-currency amount, reason, actor, and IssuedAtUtc; unique invoice/reference.

**Feature contract.** Add platform POST and scoped paged GET `/api/invoices/{id}/credit-notes`. POST accepts positive amount with at most two decimal places, currency matching BillingCurrencyCode, reason trimmed 1–1000, and reference trimmed 1–100, compared case-sensitively. Server time is the credit date; no backdating, reversal, or local-currency recalculation in this slice. Exact normalized replay returns the original note with 200; mismatched reuse conflicts. Scoped GET `/api/invoices/{id}/credit-summary?asOf=...` returns original billing total, credits, and `netBilledAmount`; a date before issuance returns 400. Add platform GET `/api/reports/credited-invoice-aging?asOf=...` using net billed amounts and existing issue-age buckets. Omitted asOf means UTC today. Never label this amount unpaid: payment tracking is not implemented by this story.

**Acceptance criteria.** Credits cannot exceed original TotalInBillingCurrency. Invoice financial fields and payslips remain unchanged. Concurrent credits cannot both consume the same remaining creditable amount. As-of summary/report includes only invoices and credits issued by the cutoff, keeps currencies separate, and excludes fully credited invoices from the adjusted aging totals. The existing invoice-aging route retains its original gross-issued semantics.

**Implementation plan.**

1. Write a ledger example distinguishing original amount, credited amount, and net billed amount from any hypothetical payment balance.
2. Add the entity, invoice concurrency token, reference uniqueness, and restricted foreign keys. Use the schema lane; do not modify old invoice totals.
3. Add request normalization and a pure remaining-creditable calculation using decimal values.
4. Authorize, load the tracked invoice before ledger rows, and check reference replay before testing the new amount against remaining capacity.
5. Insert the credit and touch invoice Version in the same save. On recognized conflicts, reload in a fresh context to distinguish replay from competing capacity use.
6. Add a detail route for the created note's Location, verifying invoice membership as well as caller scope.
7. Extract an explicit as-of net-billed projection. Read invoice/credit inputs consistently and do not clamp over-credit data silently to zero.
8. Implement the scoped summary and separate platform adjusted-aging route using that projection. Reuse age boundaries without changing their meaning to due-date aging.
9. Test ledger, replay, currency, concurrency, and cutoff cases; verify every original invoice field remains unchanged.
10. Document that if the earlier payment workbook is later implemented, credit-adjusted outstanding balances need a shared ledger policy for credits, payments, reversals, and overpayments before combining the features.

**Test scenarios.** A 100.00 invoice accepts 25.00 then 75.00 credit, producing net billed zero. A further 0.01 conflicts. Two 60.00 credits racing against 100.00 cannot both commit. A credit issued October 2 does not change September 30 adjusted aging. Changing the current FX table has no effect on the saved billing-currency credit.

**Explain it twice.** Show the atomic invoice-anchor update with its child insert. Then explain why “credited 25” and “received payment 25” are different events even when both affect a later balance calculation.

## SS-07: Scheduled billing terms and due dates

**User story.** As a platform billing operator, I want to schedule a client's payment terms and stamp a due date onto each newly issued invoice so that the agreed payment window remains clear after terms change.

**Current gap and learning.** Invoices have IssuedAtUtc but no due date. You will select effective configuration at issuance and preserve the selected values on the invoice. This is a product feature, not a rewrite of existing age-from-issuance reports.

**Read first.** [Client](../src/Atlas.Domain/Entities/Client.cs), [Invoice](../src/Atlas.Domain/Entities/Invoice.cs), [ClientEndpoints](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), and invoice construction in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs). No dependency. **Schema:** ClientBillingTerms history, unique client/effectiveDate, plus nullable invoice PaymentTermDays, DueDate, and BillingTermsId.

**Feature contract.** Add platform POST and scoped GET `/api/clients/{id}/billing-terms`. POST appends effectiveDate, termDays 0–120, and reason length 1–500; effectiveDate must be today or later. Entries cannot be edited or deleted. On invoice issuance, select the latest effective entry on or before the UTC issue day, otherwise use an explicit 30-day default. DueDate equals issue day plus selected termDays, counting calendar days. New invoices store the selected days and due date; existing invoices remain null/unknown. Add scoped `/api/invoices/{id}/due-status?asOf=...` with not-yet-due, due-today, past-due-date, or unknown-terms.

**Acceptance criteria.** A later terms change does not move an issued invoice's DueDate. Zero days means due on the issue day. A due-status result does not claim that an invoice remains unpaid. Legacy invoices return unknown-terms rather than receiving a guessed due date. Duplicate effective dates return 409. A future-effective entry is ignored for earlier issuance.

**Implementation plan.**

1. Define the history entity and invoice snapshot columns; make legacy invoice columns nullable intentionally.
2. Add append/list routes with platform writes, client-scoped reads, input normalization, and effective-date uniqueness.
3. Create a selector that accepts client ID and an explicit issue date. Test fallback and boundary selection independently.
4. Inject TimeProvider into invoice issuance and capture one now value for the completion operation. Use it for both IssuedAtUtc and due-date selection.
5. Begin an explicit completion transaction before reading its run and billing inputs, then load all needed clients' terms in a batch. Commit after invoice/run persistence; avoid a query per invoice and a mix of independently timed terms reads.
6. Stamp chosen term days, optional source ID, and DueDate onto each new invoice before saving. Validate date arithmetic rather than allowing an overflow exception.
7. Extend invoice response mapping with nullable terms fields and add the read-only due-status route.
8. For asOf before the invoice issue day, return 400 rather than inventing a not-yet-issued status; missing/foreign invoices remain 404.
9. Test an invoice issued before and after a scheduled change, then prove both remain stable after adding another entry.
10. Document calendar-day counting and keep existing issue-age report names unchanged. If a later collections feature needs unpaid status, it must integrate an actual settlement ledger.

**Test scenarios.** Issue on September 1 with 30 days: DueDate is October 1. As-of September 30 is not-yet-due, October 1 due-today, and October 2 past-due-date. Schedule 45 days effective October 1: September invoices retain 30. A legacy invoice returns unknown-terms. Two same-date inserts produce one history row.

**Explain it twice.** Name the mutable history table and immutable invoice snapshot fields. Then explain why a client's current terms cannot answer when an old invoice was originally due.

## SS-08: Scheduled management fee rates

**User story.** As a platform billing operator, I want to schedule management fee changes by payroll month so that late completion of an older payroll run uses the correct commercial rate for its period.

**Current gap and learning.** CompleteRunAsync currently reads Client.ManagementFeeRate at completion time. You will add effective-period selection without changing the stored amount on invoices that already exist.

**Read first.** [Client](../src/Atlas.Domain/Entities/Client.cs), invoice fee calculation in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), and [FxRate](../src/Atlas.Domain/Entities/FxRate.cs) as an example of period-based selection. No dependency. **Schema:** ClientFeeSchedule unique client/year/month, plus nullable Invoice.ManagementFeeRateApplied and FeeScheduleId.

**Feature contract.** Add platform POST and scoped GET `/api/clients/{id}/fee-schedule`. POST supplies effectiveYear/month, rate in [0,1), and reason length 1–500. Schedule only the current UTC month or a future month; rows are append-only and duplicate periods conflict. Completion selects the latest scheduled rate with effective period <= payroll run period. Before the first scheduled entry, use Client.ManagementFeeRate as the legacy fallback. Store the selected rate and optional schedule ID on each new invoice. A GET `/api/clients/{id}/fee-schedule/effective?year=...&month=...` explains selection without issuing anything.

**Acceptance criteria.** A September run completed in November uses September's applicable rate, not November's latest rate. Fee remains the existing rounded sum of GrossSalary times selected rate. Previously issued invoices are unchanged; legacy invoices expose null applied-rate evidence instead of reverse-calculating it from rounded totals. Existing client creation's default rate still works when there is no schedule.

**Implementation plan.**

1. Write examples where payroll period and completion date differ. Decide explicitly that selection uses the former.
2. Add schedule mapping, uniqueness, actor/timestamp metadata, and nullable invoice evidence columns.
3. Implement append/list/effective routes with date/rate normalization and stable period ordering.
4. Create a selector returning rate plus source ID/fallback indicator. Use numeric year-month ordering or DateOnly month starts consistently.
5. Change completion to load fee schedules in batches and use the selector instead of directly applying the client rate.
6. Retain the current gross-sum basis and PayrollCalculator.RoundMoney order; do not introduce per-payslip fee rounding.
7. Save selected rate/source alongside ManagementFee and invoice totals in the same transaction.
8. If SS-01 is present, update its billing estimate through the same selector. If it is absent, this story remains independently implementable.
9. Test late completion, exact effective month, fallback, duplicate writes, and stored evidence. Capture a coherent schedule read so a concurrent append supplies either a complete before or after input set.
10. Document that direct changes to the fallback client field, if introduced elsewhere later, would affect unscheduled historical periods; that separate edit path needs an explicit policy before it is added.

**Test scenarios.** Fallback rate 10%, October schedule 12%, gross sum 1000.00: September fee is 100.00 even if completed in November; October is 120.00. Two payslips with small fractional fee contributions reconcile using round-after-sum. Rate -0.01 or 1 fails. Adding November's schedule changes no issued invoice.

**Explain it twice.** Trace the selected schedule ID into the invoice row. Then explain why “rate effective in October” is different from “rate entered in October.”

## SS-09: Contract cost-center allocations

**User story.** As a client administrator, I want to allocate a contract's payroll cost across my company's cost centers so that shared roles can be charged to the departments that benefit from them.

**Current gap and learning.** Atlas knows client ownership but has no internal client accounting dimension. You will introduce effective allocations and reconcile integer-cent splits while capturing the result at payroll creation.

**Read first.** [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), and [MoneyInvariantTests](../tests/Atlas.Tests/Domain/MoneyInvariantTests.cs). No dependency. **Schema:** ClientCostCenter, ContractAllocationSchedule/items, and PayslipCostAllocation snapshots; unique client/normalized code, contract/effective period, and payslip/cost-center identity.

**Feature contract.** Add scoped cost-center GET/POST under `/api/clients/{id}/cost-centers`, with immutable code length 1–30 and name 1–100. Add scoped GET/POST `/api/contracts/{id}/cost-allocations`: append an effective year/month and 1–10 distinct same-client cost centers with integer basisPoints totaling 10000. Each weight is 1–10000. Current/future periods only; duplicate periods conflict. Payroll selects the latest schedule through its month and saves allocations. Without a schedule, save an explicit Unallocated bucket. No edit/delete of schedules or catalog entries in this slice.

**Acceptance criteria.** Allocation totals reproduce each saved payslip's components exactly. Use integer minor units for configured splits; if a source component has sub-cent precision, reject configured allocation with a clear 409 rather than silently altering payroll. Unallocated unsplit values remain exact. A later schedule does not rewrite past allocations. No client can allocate to another client's cost center.

**Implementation plan.**

1. Define catalog, schedule header/items, and snapshot fields, including copied code/name, basis points, source schedule ID, and currency. Give each snapshot a nonnull BucketKey (`unallocated` or the cost-center ID) and enforce unique PayslipId/BucketKey; a nullable foreign-key unique index alone would allow multiple Unallocated rows in SQLite.
2. Add uniqueness and restricted foreign keys; authorize the contract/client before loading requested cost-center IDs.
3. Implement normalized catalog creation and append-only schedule validation. Load one complete selected schedule at calculation time.
4. Build a pure largest-remainder allocator: convert a nonnegative cent amount to integer cents, calculate ideal shares, floor each, then distribute remaining cents by largest fractional remainder with cost-center ID as a deterministic tie-breaker.
5. Allocate base gross excluding unused leave, unused leave payout, employer cost, employee deductions, reimbursements, employer benefits, and employee benefits separately.
6. Derive allocated GrossSalary from allocated base plus unused payout, NetPay from allocated gross minus deductions/employee benefits plus reimbursements, and TotalCost from allocated gross plus employer cost/benefits/reimbursements. Do not independently split derived totals and break row-level formulas.
7. For no schedule, create one Unallocated row preserving every component. Represent that bucket explicitly rather than inserting a fake client cost center.
8. Integrate allocation capture into payroll creation's transaction with payslips and expenses. If SS-01 exists, share allocation calculation while keeping its simulation read-only.
9. Add snapshot reads under a scoped payslip allocation route. For legacy payslips without snapshots, report `not-captured`; do not infer old allocations from current schedules.
10. Test rounding, same-client membership, effective selection, and rollback. Document why fractional-cent configured inputs are blocked until a separate precision policy is designed.

**Test scenarios.** Split 0.01 at 50/50: the stable tie-breaker assigns one cent to one center and zero to the other; repeated runs of the helper agree. Split 100.00 at 3333/3333/3334 basis points and verify exact totals. A new schedule leaves an earlier payslip unchanged. Weights 9999 or 10001 in total fail; a foreign cost-center ID is rejected without exposing its name.

**Explain it twice.** Show where the residual cent goes. Then explain to a department manager why displayed shares can differ by a cent even when percentages look equal.

## SS-10: Cost-center payroll report

**User story.** As a client finance administrator, I want to see completed payroll spending by cost center and month so that department totals reconcile with the payroll already recorded for my company.

**Current gap and learning.** SS-09 captures allocations but does not provide a period report. You will aggregate historical dimensions and preserve visibility of unallocated or uncaptured data instead of losing it through an inner join.

**Read first.** Your SS-09 allocation snapshots, [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), [PayrollRun](../src/Atlas.Domain/Entities/PayrollRun.cs), and [ReportQueryValidation](../src/Atlas.Api/Reporting/ReportQueryValidation.cs). **Dependency:** SS-09. No additional schema change.

**Feature contract.** Add scoped GET `/api/clients/{id}/cost-center-payroll?fromYear=...&fromMonth=...&toYear=...&toMonth=...`, maximum 12 inclusive months. Include Completed runs only. Group by period, currency, and saved cost-center identity. Return component sums and saved-label variants if one identity has multiple labels in historical data. Keep Unallocated separate from `not-captured` legacy payslips. Return sourcePayslipTotal, allocatedTotal, legacyUncapturedTotal, and reconciliationDifference per month/currency. This is payroll cost, excluding invoice management fees and billing FX.

**Acceptance criteria.** Every selected payslip contributes to either captured allocations or the explicit uncaptured bucket. AllocatedTotal includes Unallocated. For valid data, allocatedTotal + legacyUncapturedTotal equals sourcePayslipTotal and difference is zero. Client/currency boundaries are preserved. Current schedule changes cannot change the report.

**Implementation plan.**

1. Create a fixture with a split payslip, an Unallocated payslip, a legacy payslip with no rows, and a Draft run to exclude.
2. Validate the complete period range and authorize the client before any aggregation.
3. Query matching completed payslips by their saved ClientId, projecting IDs, period, currency, and totals.
4. Load allocation snapshots for those IDs in a bounded query strategy; chunk ID lists where needed to avoid provider parameter limits.
5. Identify uncaptured payslips by absence of allocation rows. Do not assign today's schedule to them.
6. Group captured rows by saved identity/month/currency and sum decimal components. Return distinct saved labels sorted ordinally when an identity has multiple labels; do not substitute a live catalog name.
7. Compute reconciliation separately from display groups so missing or duplicate allocation data is detectable as a nonzero difference.
8. Add route/DTOs and ensure period ordering and bucket ordering are deterministic. Avoid a combined total across currencies.
9. Add tests for legacy rows, Draft exclusion, source changes, and a deliberately inconsistent allocation fixture.
10. Document why “Unallocated” means a captured decision while “not captured” means the system lacks historical evidence.

**Test scenarios.** Completed payroll totals 150.00: split captured 100.00, Unallocated 20.00, legacy uncaptured 30.00. Reconciliation is 120 + 30 - 150 = 0. Remove a 0.01 captured component in a corruption fixture and expose the discrepancy. Add a Draft 500.00 run and verify it contributes nothing. A foreign client's same code does not join the group.

**Explain it twice.** Describe the left-join/absence handling in your implementation. Then explain why hiding uncaptured payroll would make a department report appear more complete than it is.

## SS-11: Validate and commit a worker import

**User story.** As a platform administrator, I want to preview a small CSV worker import, inspect row errors, and commit a valid batch so that onboarding data can be entered efficiently without partially importing a broken file.

**Current gap and learning.** Workers are created individually. You will add a staged command with durable normalized input, an expiration window, and commit-time revalidation. Validation is useful feedback, but it does not reserve email addresses.

**Read first.** [WorkerEndpoints](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), [Worker](../src/Atlas.Domain/Entities/Worker.cs), the Worker.Email unique index in [AtlasDbContext](../src/Atlas.Infrastructure/AtlasDbContext.cs), and [ClientAndWorkerEndpointTests](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs). No dependency. **Schema:** versioned WorkerImportBatch and staged rows with source row number, normalized fields, validation errors, and optional created WorkerId.

**Feature contract.** Add platform POST `/api/worker-imports/validate` accepting a JSON `csv` string, maximum 2 MiB raw HTTP body, 1 MiB decoded UTF-8 CSV, and 100 data rows, with exact headers FullName,Email,CountryCode,DateOfBirth. Enforce both byte limits independently. Support quoted commas, doubled quotes, and quoted newlines. DateOfBirth is empty or ISO YYYY-MM-DD. Store an owner-scoped Validated or Invalid batch expiring 24 hours after creation; return row errors without creating workers. POST `/api/worker-imports/{id}/commit` requires expectedVersion and a Validated batch. Commit all workers or none; repeated successful commit returns the original worker-ID mapping with 200. An expired uncommitted batch returns 410. Invalid data requires a new validation batch, not editing staged rows.

**Acceptance criteria.** Validation and commit use the same worker normalization as the existing endpoint: trim names, lowercase trimmed emails, uppercase country codes. Duplicate normalized emails within the file or already in the database are reported with row numbers. Commit rechecks database conflicts and countries. Another platform API identity cannot retrieve/commit the batch; owner mismatch returns 404. Client roles cannot import.

**Implementation plan.**

1. Define the CSV grammar and exact header rules, then create parser fixtures before changing endpoints. Use a tested parser or a focused state-machine parser; never split records on commas/newlines blindly.
2. Extract existing worker input normalization/validation into a shared helper. Enforce model field bounds consistently in single creation and import, and document those validation additions.
3. Add batch/row entities and indexes. Store normalized data and row errors; avoid keeping an extra raw CSV copy or logging the file contents.
4. Implement byte/row limits before expensive processing. Treat malformed CSV structure as 400, while structurally valid CSV with row errors produces a readable Invalid batch.
5. Validate countries and emails in batched queries and persist only staging records, actor, expiry, and source-row mapping.
6. Add owner-scoped GET detail showing status, errors, expiry, and created IDs when committed.
7. On commit, authorize, check status/version/expiry, then revalidate uniqueness against current database state before constructing Workers.
8. Insert workers and set batch Committed with row-to-ID mappings in one transaction. The Worker.Email unique index and batch Version protect concurrent single creates and duplicate commits.
9. On a losing concurrent commit, reload the batch in a fresh context: return its committed mapping if available; otherwise a conflict with no partial workers.
10. Test parser cases, expiry, validation/commit races, and transaction failure. Document that correcting an Invalid batch means submitting corrected CSV for a new batch.

**Test scenarios.** `"Doe, Jane"` stays one name; a quoted newline stays within a field. `A@EXAMPLE.COM` and ` a@example.com ` are duplicates. Validate an available email, create it through the existing endpoint, then commit: zero imported workers. Commit a valid batch twice: same IDs, one set of workers. At exact expiry, an uncommitted batch rejects; an already committed batch can still return its saved result.

**Explain it twice.** Identify the staging rows and the commit boundary. Then explain why a green validation preview cannot promise that an email will still be available later.

## SS-12: Compliance document renewal history

**User story.** As a platform compliance operator, I want to record a renewed document as the successor of an older one so that users can follow its history and expiry reports stop treating superseded records as current documents.

**Current gap and learning.** ComplianceDocument stores metadata and expiry, but separate entries have no renewal relationship. You will preserve old records while introducing an explicit, nonbranching supersession chain. No file upload or legal verification is part of this feature.

**Read first.** [ComplianceDocument](../src/Atlas.Domain/Entities/ComplianceDocument.cs), [ComplianceEndpoints](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), and `ComplianceExpiriesAsync` in [ReportService](../src/Atlas.Api/Reporting/ReportService.cs). No dependency. **Schema:** nullable SupersedesDocumentId with a unique nonnull index and restricted self-referencing foreign key.

**Feature contract.** Add platform POST `/api/workers/{workerId}/documents/{documentId}/renew` accepting name length 1–200, required issuedDate, and optional expiryDate. Worker/type are inherited from the predecessor. Require issuedDate <= UTC today, expiry >= issuedDate, and any supplied expiry >= today. Only a chain's current leaf can be renewed. New records receive new IDs and server CreatedAtUtc; existing metadata never changes. Add scoped GET `/.../documents/{documentId}/renewal-history` returning the ordered chain and current ID. Worker readers use the current worker-visibility rule; writes remain platform-only.

**Acceptance criteria.** One predecessor has at most one successor, including under competing renewals. A chain cannot branch, switch worker/type, or cycle through a caller-selected ID. Worker document lists retain historical entries with relationship metadata. Both expiry endpoints exclude a superseded document for asOf dates on/after its successor was recorded. Historical selection first limits documents to CreatedAtUtc <= end of asOf day, then chooses leaves within that recorded set. This intentionally introduces recorded-date-aware report semantics; it is not a reconstruction from issue date alone.

**Implementation plan.**

1. Add the relationship/index; legacy records with null SupersedesDocumentId become independent roots without guessed links.
2. Implement normalized renewal validation and load the predecessor restricted to workerId. Check leaf status, then create only a fresh successor.
3. Rely on the unique predecessor index at save time to reject simultaneous renewals. A preliminary leaf check alone is insufficient.
4. Use injected TimeProvider for both existing document creation and new renewal timestamps, enabling deterministic asOf tests.
5. Build a shared current-document-at-cutoff query: include recorded documents, exclude those with an included successor, then apply expiry filtering.
6. Apply it to `/api/compliance/expiring` and the platform compliance-expiries report. Update tests and documentation for the deliberate historical-date behavior change.
7. Add relationship fields to worker document responses without hiding old entries from the history list.
8. Implement chain traversal with a visited-ID set and a 100-record bound; return an explicit data-state error on a cycle or excessive legacy chain rather than looping indefinitely.
9. Test worker/type inheritance, same-predecessor races, report cutoffs, and foreign worker access.
10. Document that independently uploaded same-type documents remain separate roots; the application must not guess which one supersedes another.

**Test scenarios.** Passport A is recorded September 1 and expires September 30; successor B is recorded September 10 and expires next year. September 9 expiry reporting can show A; September 10 onward selects B. Two renewals of A produce one successor. A request under a different worker path returns 404. A future-issued replacement is rejected rather than immediately hiding an otherwise current document.

**Explain it twice.** Show the self-reference and unique constraint. Then explain why keeping an old passport record is compatible with removing it from the current expiry workload.

## SS-13: Country document coverage policies

**User story.** As a platform compliance operator, I want to configure required document types for a country and assess a contract against that policy so that missing or near-expiry coverage is visible before someone reviews the case manually.

**Current gap and learning.** Expiry reporting answers which stored documents expire soon; it cannot identify a required document that was never added. You will model requirements independently from available records and return explainable assessment results.

**Read first.** [Country](../src/Atlas.Domain/Entities/Country.cs), [ComplianceDocument](../src/Atlas.Domain/Entities/ComplianceDocument.cs), [ComplianceEndpoints](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), and [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs). No dependency. **Schema:** versioned CountryDocumentPolicy header plus items unique by country/type.

**Feature contract.** Add platform PUT and authenticated GET `/api/countries/{code}/document-policy`. PUT replaces 1–5 requirements with distinct defined document types, minimumRemainingDays 0–365, and expiryRequired, using expectedVersion. Unconfigured GET returns configured=false, Version 0; first configuration starts at Version 1. Add scoped GET `/api/contracts/{id}/document-coverage?asOf=...`. For each requirement, report matching document IDs or blockers: missing-type, not-yet-issued, expiry-required, expired, or insufficient-remaining-days. A document qualifies if issued date is null or <= asOf and expiry is allowed-null or >= asOf plus the required remaining days. No automatic activation block is added.

**Acceptance criteria.** A required type with no records appears as missing, rather than disappearing from a document-driven query. No policy returns `assessment=not-configured`, not pass. Every requirement must have at least one qualifying document for `assessment=covered`. Use the contract's CountryCode, not a guessed country from the worker's latest profile. Apply the current policy to documents at the requested date and return its Version; this slice has no historical policy reconstruction. Assessment is read-only and labels this as metadata coverage, not verified legal compliance.

**Implementation plan.**

1. Write a two-requirement example with a missing WorkPermit and a Passport that expires too soon.
2. Add policy/header mappings, uniqueness, and atomic whole-policy replacement with a touched header Version.
3. Implement GET fallback and PUT input validation, including Enum.IsDefined rather than accepting undefined numeric enum values.
4. Add a pure requirement assessor that receives an explicit date and candidate metadata, keeping EF and HTTP outside the rule.
5. Authorize the contract and load its country's policy and worker documents as one coherent assessment input set.
6. If SS-12 is present, use its leaves-at-asOf selection. Otherwise use all recorded documents through the asOf cutoff; do not assume renewal relationships exist.
7. Start output rows from policy requirements, then attach qualifying IDs or sorted blocker details for candidates. Do not require every document of a type to qualify if one satisfies the requirement.
8. Return policy Version, assessed date, contract ID, and overall status. Changing a policy must not mutate contracts or onboarding items.
9. Test null dates, exact remaining-day boundary, multiple candidates, missing policy, and concurrent policy replacement.
10. Document that introducing an activation gate later would be a separate business change requiring revalidation in the activation write path.

**Test scenarios.** With asOf September 1 and minimumRemainingDays 30, expiry October 1 qualifies and September 30 does not. Null expiry qualifies only when expiryRequired=false. Two Passports, one expired and one sufficient, satisfy the type. A missing WorkPermit still blocks overall coverage. A foreign client cannot inspect the contract's assessment.

**Explain it twice.** Explain why iteration starts with requirements instead of documents. Then explain how an expiry dashboard can look empty while a worker still lacks a required document entirely.

## SS-14: Client payroll budgets

**User story.** As a client finance administrator, I want to set monthly payroll budgets by currency and compare them with completed payroll so that I can identify spending above our internal plan.

**Current gap and learning.** Payroll reports show costs without a target. You will define the measurement basis, keep currencies separate, and distinguish missing budgets from zero spending.

**Read first.** [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), [PayrollRun](../src/Atlas.Domain/Entities/PayrollRun.cs), and [ClientEndpoints](../src/Atlas.Api/Endpoints/ClientEndpoints.cs). No dependency. **Schema:** versioned ClientPayrollBudget unique client/year/month/currency, with amount and server modification metadata.

**Feature contract.** Add scoped PUT/GET `/api/clients/{id}/payroll-budgets/{year}/{month}/{currency}`. Managers set amount >0 and <=1,000,000,000 with at most two decimals, using expectedVersion; initial creation requires 0 and stores Version 1. Currency must match at least one configured country's CurrencyCode. Add scoped GET `/api/clients/{id}/payroll-budget-report?year=...`. Actuals are saved Payslip.TotalCost from Completed runs only. Return month/currency rows where a budget or actual exists, with budget nullable, actual, variance `actual - budget`, utilizationPercent, and status no-budget/within-budget/near-budget/at-budget/over-budget. Near means >=90% and <100%; compare exact amounts before rounding display percentages.

**Acceptance criteria.** No budget never means zero budget. A budget without payroll has actual zero. Draft runs and invoice fees/FX are excluded by definition. EUR and USD are never combined. Modifying a budget changes the planning comparison without changing payroll, invoices, or historical evidence. Concurrent budget edits reject stale versions.

**Implementation plan.**

1. Write the actuals definition and a table of threshold examples before building the report.
2. Add budget mapping, compound uniqueness, and versioning. Use initial Version 1 to distinguish missing-row expectedVersion 0.
3. Implement manager-only PUT on visible clients, validating exact decimal scale, currency membership, and expected version.
4. Add GET with 404 for an absent individual budget and stable response fields for amount/version.
5. Query completed payslips scoped by saved ClientId and requested year, projecting period/currency/TotalCost.
6. Load budgets for the same client/year and form the union of budget and actual month/currency keys.
7. Calculate exact decimal actuals, signed variance, and status; only then round utilization to two decimals. Leave variance/utilization null when no budget exists.
8. Read budgets and actuals consistently for one response and sort by month/currency. Do not use the current client billing currency to relabel local payroll.
9. Test source inclusion, thresholds, no-budget cases, currency isolation, and simultaneous updates.
10. Document that changing a budget revises the current plan; this slice does not keep budget revision history or send alerts externally.

**Test scenarios.** Budget 1000 and actual 899.99 is within-budget; 900 is near-budget; 1000 at-budget; 1000.01 over-budget. A value just below 90% that rounds to 90.00% stays within-budget. A Draft 500 cost is excluded. A currency with actuals but no budget appears with null planning fields, not an infinite percentage.

**Explain it twice.** Trace TotalCost from stored payslips through the grouping. Then explain why a rounded 90.00% display can still sit just below the exact warning threshold.

## SS-15: Typed contract custom fields

**User story.** As a client administrator, I want to define a few typed fields on my company's contracts so that internal attributes such as team code or equipment allowance can be stored without adding a database column for every client request.

**Current gap and learning.** Contracts have fixed fields only. You will add controlled extensibility while preventing arbitrary JSON from replacing validation, ownership, or concurrency rules.

**Read first.** [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), [ClientEndpoints](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), and DbContext's string/decimal mappings. No dependency. **Schema:** ClientContractFieldDefinition and ContractFieldValue, unique client/normalized key and contract/definition respectively.

**Feature contract.** Add scoped GET/POST `/api/clients/{id}/contract-field-definitions`. Define key matching lowercase ASCII `[a-z][a-z0-9_]{0,29}`, label length 1–100, and type text/number/choice. Choice has 1–20 distinct immutable option codes. Definitions, types, and option codes cannot be edited/deleted in this slice; no required fields or default backfill. Add scoped GET/PUT `/api/contracts/{id}/custom-fields`, using expectedContractVersion and complete replacement of at most 20 supplied values. Text is <=500 characters; number is a decimal JSON number within +/-1,000,000,000 and at most four decimal places; choice is one defined option code. Omitted values are removed, and null is rejected. No formula evaluation or impact on payroll.

**Acceptance criteria.** A contract can use only its client's definitions. Numeric values stay numeric and round-trip exactly, not through locale-dependent strings or doubles. Unknown keys/types, duplicate keys, and invalid choices return 400. A replacement is atomic and increments Contract.Version, including child-only changes. Existing contracts with no values remain valid. Client viewers may read but cannot define or replace fields.

**Implementation plan.**

1. Model definition kind and allowed options explicitly. Store values in separate nullable typed columns or a validated tagged value; for this slice, use TextValue/NumberValue/ChoiceCode with exactly one populated according to definition kind.
2. Add normalized key uniqueness and contract/definition relationships with restricted deletion. Do not store caller-supplied ClientId on value rows as a substitute for checking the parent.
3. Implement definition creation and a paged list; validate immutable key/type/options before save. Use the same 1–30 character key grammar for choice codes and compare them ordinally. Rely on uniqueness for competing same-key creation.
4. Add replacement DTOs and expose Contract.Version through the read response so callers can obtain the expected version.
5. Load tracked contract, authorize management, compare expectedVersion, and load all referenced same-client definitions.
6. Validate the complete replacement, including distinct keys and typed JSON values, before deleting or adding any value rows.
7. Replace the child collection, explicitly touch contract Version, and save once. An empty list deliberately removes all optional custom values.
8. Return definition metadata with typed values sorted by key; leave ordinary contract response size unchanged by using this dedicated route.
9. Test invalid mixed types, unknown/foreign definitions, precision, replacement removal, and two writers with the same contract version.
10. Document that changing definition types or introducing required fields later requires a data-evolution plan; immutability is the bounded first version, not an accidental limitation.

**Test scenarios.** Define `team` as choice with `ops` and `eng`; `sales` rejects. Store numeric 12.3456 exactly; 12.34567 fails rather than rounding. An invalid last field leaves all previous values untouched. Two replacements from one Version cannot silently overwrite one another. Removing all values does not change contract salary or status.

**Explain it twice.** Show how the definition constrains the stored value. Then explain why “custom field” means configurable data, not permission to store anything and decide its meaning later.

## SS-16: Contract decision audit feed

**User story.** As a client administrator, I want to see who activated, terminated, or amended a contract and what changed so that important employment decisions can be explained after the fact.

**Current gap and learning.** Contract timestamps and salary history describe selected outcomes but do not consistently capture the acting API identity and before/after values. This is a new durable decision feed, distinct from reconstructing a worker timeline from current entities.

**Read first.** [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), approval in [AmendmentEndpoints](../src/Atlas.Api/Endpoints/AmendmentEndpoints.cs), approval in [TerminationEndpoints](../src/Atlas.Api/Endpoints/TerminationEndpoints.cs), and [ApiKeyAuthenticationHandler](../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs). No dependency. **Schema:** append-only ContractDecisionEvent with database sequence, public ID, ClientId/ContractId, actor ID/role, occurred timestamp, event kind, source ID, resulting contract version, and versioned allowlisted payload.

**Feature contract.** Capture contract activation, direct termination, termination-request approval, and amendment approval. Add scoped GET `/api/clients/{id}/contract-decisions?afterSequence=0&limit=50` and a contract-specific feed. Limit is 1–100; sort ascending sequence for incremental reading. Events include old/new status, salary/title changes where applicable, relevant effective date, and source workflow ID. Exclude raw request bodies, API keys, receipt URLs, and free-text reasons. The feed begins when the feature is deployed; no inferred backfill.

**Acceptance criteria.** A successful listed action produces exactly one event in the same commit as its business changes. A failed or stale action produces none. Termination through either supported path appears with its actual source kind. Client readers see only their client, even for a shared worker. Event payload values remain unchanged if current entities later change. Sequence orders recorded events; it does not claim legal or effective-date ordering.

**Implementation plan.**

1. Define three event kinds—activated, terminated, amendment-applied—and source kinds distinguishing direct termination from request approval.
2. List the exact before/after fields allowed for each kind, with payload schema version 1 and a maximum serialized size of 8 KiB.
3. Add event mapping/indexes, restricted identity references, and uniqueness on contract/resulting-version/event-kind to guard duplicate event capture.
4. Extract a small event builder receiving actor and copied before/after values. Do not serialize tracked entities or HttpContext.
5. Wrap each affected workflow in an explicit transaction where needed. Capture before-values before invoking domain transitions.
6. Save business changes, obtain the actual new Contract.Version, append the event, then commit the transaction. If event creation or save fails, roll back the earlier business save too.
7. For amendment approval, include its SalaryRecord insertion in this same boundary. For termination-request approval, include the request decision and contract transition.
8. Implement scoped feed queries with validated afterSequence/limit. Scope before applying the sequence filter and row cap; return the last sequence as the next bookmark.
9. Add tests for all four entry points, concurrent losers, actor capture, unchanged old payloads, and feed paging on an unchanged dataset.
10. Document event coverage and its start date. Explain that direct database edits and unrelated endpoints are not audited by this bounded feature.

**Test scenarios.** Approve a salary amendment from 4000 to 4400: contract, SalaryRecord, and one event commit together. Inject event-save failure: all three remain unchanged/uninserted. Attempt activation twice: one activation event. Two clients share a worker: each feed includes only its own contract IDs. Request pages with a sequence bookmark and verify no duplicated event IDs.

**Explain it twice.** Point to the transaction containing both state and evidence. Then explain why a log line saying “approved” is weaker evidence if the database save can still fail afterward.

## SS-17: Client notification inbox

**User story.** As a client user, I want an in-app inbox of important contract changes and a personal read marker so that I can catch up after being away without relying on external email or a constantly open browser.

**Current gap and learning.** SS-16 records decisions but does not deliver a user-oriented inbox. You will use durable committed events as the delivery source and make repeated processing harmless. There is no external notification provider in this slice.

**Read first.** Your SS-16 event entity/writers, [Program](../src/Atlas.Api/Program.cs), [AuthorizationExtensions](../src/Atlas.Api/Auth/AuthorizationExtensions.cs), and [AtlasApiFactory](../tests/Atlas.Tests/Integration/AtlasApiFactory.cs). **Dependency:** SS-16. **Schema:** ClientNotification unique source-event/client, plus NotificationRead unique notification/API-user. Notifications store client ID, event sequence, kind, short snapshot title, local contract path, and created time.

**Feature contract.** A hosted dispatcher reads committed ContractDecisionEvents without notifications and inserts one notification for each. Add scoped GET `/api/clients/{id}/notifications?beforeSequence=...&unreadOnly=true&limit=25`, newest first, limit 1–100, plus unread count. POST `/api/clients/{id}/notifications/{notificationId}/read` marks that notification read for the current API identity, returning 200 on repeats. Client viewers may perform this personal read-marker write even though they cannot change contract business state. No mark-unread, push delivery, email, or per-user fan-out job is added.

**Acceptance criteria.** An event survives a dispatcher restart and eventually becomes visible after successful processing. Two dispatchers cannot create duplicate notifications. Marking read does not affect another identity's unread count. Foreign notifications return 404. Titles and links contain only the authorized client/contract context; links are constructed from known route templates. A failed business action from SS-16 creates no notification source.

**Implementation plan.**

1. Treat the committed SS-16 event table as the durable source. Query for missing notification rows instead of emitting transient in-memory messages from endpoint handlers.
2. Define short, deterministic title templates by event kind using captured event values. Do not include salaries or free-text reasons in inbox titles.
3. Add notification/read-marker mappings, uniqueness, and indexes for client/event sequence and reader lookups.
4. Implement `DispatchOnce` processing at most 100 oldest missing events. It must be callable without a background loop in tests.
5. Insert notification rows in short transactions. If another dispatcher wins uniqueness, discard that failed tracked insert and continue after a fresh lookup; do not classify unrelated failures as delivery success.
6. Add a hosted loop with scoped DbContexts and cancellation support. A crash before insert leaves the event eligible; a crash after commit leaves uniqueness protecting the retry.
7. Implement the inbox query and unread count using the current API-user ID and client scope. Use a consistent read when returning page and count together.
8. Implement read marking as an insert with a unique constraint, normalizing an already-existing marker to 200. Never accept reader ID from the body.
9. Test duplicate dispatch, recovery, per-identity markers, paging, and foreign IDs. Disable automatic dispatch in deterministic tests and invoke one pass explicitly.
10. Document that replacing an API identity creates a new personal read history unless a later identity-migration feature deliberately transfers it.

**Test scenarios.** Commit an event while the dispatcher is stopped, restart it, and observe one notification. Run two dispatch passes over the same event: still one row. User A marks it read, user B remains unread. A viewer can mark their own notification but still cannot activate the contract. A crash after notification commit does not create another row on restart.

**Explain it twice.** Explain uniqueness-based deduplication without describing it as exactly-once external delivery. Then explain why the durable event lets someone receive a notification even if the API process restarted immediately after the business action.

## SS-18: Background client data export

**User story.** As a client administrator, I want a downloadable export of my contracts and selected payroll history so that I can review that data offline without assembling many paginated API responses.

**Current gap and learning.** Individual routes expose records, but no consistent bounded bundle is available. You will extend the background-job protocol to a read operation, enforce authorization again at download, and expire the stored artifact.

**Read first.** Your SS-05 job/lease implementation, [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), [PayrollEndpoints](../src/Atlas.Api/Endpoints/PayrollEndpoints.cs), and [InvoiceEndpoints](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs). **Dependency:** SS-05. **Schema:** extend BackgroundJob with client-export kind/scope; add a database ExportArtifact with bytes, checksum, schemaVersion, generatedAtUtc, and expiresAtUtc, unique job ID.

**Feature contract.** Add manager POST `/api/clients/{id}/data-exports` with a maximum 12-month payroll range and reference length 1–100. Key jobs by owner API-user/client/reference; identical normalized requests replay the existing job, different payloads conflict. Export JSON schema version 1 containing all of that client's contracts, the IDs/names/emails of their referenced workers, and only that client's payslips/invoices for the selected payroll periods. Omit DOB, API users/keys, receipts, document metadata, and unrelated workflow records. Limit the bundle to 5000 total data rows and 5 MiB serialized UTF-8; oversized jobs fail explicitly. Artifacts expire 24 hours after generation. No filesystem path or external storage provider is used.

**Acceptance criteria.** Submission returns 202 and an owner-scoped polling Location. Worker reads occur only after rechecking that the requester is active and still a permitted manager of the stored client scope. The exported bundle represents one consistent database read, with generatedAtUtc separate from requested period. Poll/download rechecks current authentication, ownership, and management permission. An expired owned artifact returns 410; a foreign artifact returns 404. No authorization decision is based solely on a hard-to-guess job ID.

**Implementation plan.**

1. Extend the job operation-key format and dispatcher switch without weakening payroll's unique country/period operation key. Reject unsupported job kinds.
2. Add explicit export DTOs listing fields; do not serialize EF graphs or invoke every existing endpoint internally.
3. Implement scoped submission/poll/download routes and normalized reference fingerprint comparison, including completed-job replay behavior.
4. Claim work through SS-05's lease protocol, then reload requester role/active/client values before reading data. If access was revoked before processing starts, fail with a safe authorization code.
5. Open a read transaction for all exported collections, scope every query by client ownership, and load only workers referenced by that client's exported contracts.
6. Apply row-count checks while loading and byte limits during serialization. Produce deterministic row ordering and a checksum over the final bytes.
7. Close the read transaction, then save the artifact and Succeeded job state in one fenced transaction. A lost lease must leave no orphan artifact.
8. At download, check scope before expiry, return JSON with a fixed server filename, and deny new downloads at or after expiresAtUtc. Recheck permission even if the job succeeded earlier.
9. Add bounded cleanup that clears expired artifact bytes while retaining the artifact's expiry/checksum and job metadata. Make the byte field nullable for this cleared state. Download checks must enforce expiry even if cleanup has not run and still return 410 afterward. A new export needs a new reference after expiry.
10. Test consistency, tenant isolation, revocation, caps, expiry, and crash recovery. Document that a request already authorized/streaming cannot be retroactively withdrawn by a later key deactivation.

**Test scenarios.** A shared worker has contracts for A and B: A's bundle contains A contracts/payslips/invoices only. Modify data between collection reads in a controlled test and verify the transaction prevents a mixed snapshot. Deactivate the requesting key after generation: download fails authentication. At exact artifact expiry, download returns 410. Crash after artifact preparation but before fenced commit leaves neither artifact nor Succeeded state.

**Explain it twice.** Trace scope from submission into the worker query and back into download authorization. Then explain why “the job was allowed yesterday” is not sufficient permission to download its data today.

## SS-19: Frozen workforce snapshots and comparison

**User story.** As a client administrator, I want to capture a workforce roster and compare it with another captured roster so that a review meeting can use stable records even when live contract data changes afterward.

**Current gap and learning.** Headcount and contract reads are live queries. An asOf date controls employment coverage but does not freeze current names or titles. You will materialize a bounded read model and distinguish effective dates from capture time.

**Read first.** `HeadcountAsync` in [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), [Worker](../src/Atlas.Domain/Entities/Worker.cs), and [Pagination](../src/Atlas.Api/Pagination.cs). No dependency. **Schema:** WorkforceSnapshot header plus immutable rows, unique snapshot/contract, storing client/creator, asOf, capturedAtUtc, label, and copied roster fields.

**Feature contract.** Add manager POST and scoped paged GET `/api/clients/{id}/workforce-snapshots`. Capture accepts asOf and label length 1–100, maximum 5000 matching contracts. Membership follows non-Draft contracts whose inclusive employment interval covers asOf. Store contract ID, worker ID, copied worker name, contract country, copied current job title, and employment dates. Names/titles are explicitly “as captured,” not reconstructed historical values. Add paged snapshot detail and POST `/api/clients/{id}/workforce-snapshots/compare` with baseline/comparison IDs. Compare distinct snapshots of the same visible client by ContractId, returning added, removed, retained, and changed captured fields.

**Acceptance criteria.** Captured rows never change when live data changes. Header/counts/rows commit atomically from one consistent read. Same-worker different contracts remain distinct. Comparison direction is caller-selected and explicit; snapshots may share an asOf date but have different capture times. No salaries or historical salary assumptions are added. A foreign snapshot cannot be compared or read.

**Implementation plan.**

1. Define the header and row DTOs, including a description of effective membership versus current captured labels.
2. Add snapshot/row mapping, indexes, and restricted references. Copy required labels so later name changes do not alter rendering.
3. Implement authorization and input validation, then run the existing employment-coverage predicate under a consistent transaction.
4. Project only the required roster values, fetch at most 5001 rows, and reject overflow before persisting any header or row.
5. Capture one clock instant, construct header/counts and rows, and commit together. Distinct workerCount and contractCount must be named separately.
6. Add immutable list/detail routes with stable ordering and pagination. Do not rejoin live worker names when returning saved rows.
7. Implement comparison by the union of saved ContractIds. For retained rows, return changed field names and both captured values; do not assume a changed title indicates an approved amendment.
8. Include both snapshot asOf/capturedAt values in the comparison response so users can interpret equal-date captures.
9. Test stable rendering after live edits, comparison classification, duplicate worker identities, size cap, and atomic rollback.
10. Document that snapshots are purpose-specific report artifacts, not database backups or complete employment audit history.

**Test scenarios.** Capture A, change a live worker name, then read A: the original name remains. Capture B for the same date: the retained row reports a name-label change. A terminated contract covers its inclusive end date but not the next day. One worker with two qualifying fixture contracts yields workerCount 1 and contractCount 2. A failed row insert leaves no header-only snapshot.

**Explain it twice.** Show which fields are copied instead of joined live. Then explain why an asOf filter alone cannot make a report reproducible next week.

## SS-20: Two-approver expense policy

**User story.** As a client administrator, I want larger expense claims to require approval from two different API identities so that one ordinary approval cannot release a high-value claim into payroll by itself.

**Current gap and learning.** Expense approval currently moves Pending directly to Approved. You will snapshot policy at submission, accumulate approval evidence on the claim aggregate, and preserve payroll's Approved-only selection. Two API identities are not proof of two separate humans; that stronger identity model is outside this slice.

**Read first.** [ExpenseClaim](../src/Atlas.Domain/Entities/ExpenseClaim.cs), [ExpenseEndpoints](../src/Atlas.Api/Endpoints/ExpenseEndpoints.cs), expense reimbursement in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), and [AuthorizationMatrixTests](../tests/Atlas.Tests/Integration/AuthorizationMatrixTests.cs). No dependency. **Schema:** versioned client/currency ExpenseApprovalPolicy, ExpenseApproval rows unique claim/approver, and claim fields RequiredApprovals, PolicyVersionApplied, TwoApprovalThresholdApplied, and SubmittedByApiUserId. Legacy claims default to RequiredApprovals 1 with unknown submitter/policy.

**Feature contract.** Add manager PUT and scoped GET `/api/clients/{id}/expense-approval-policies/{currency}` with threshold >0 and <=1,000,000,000, at most two decimals, and expectedVersion. Currency must be a configured country currency; initial creation uses expectedVersion 0 and stores Version 1. New claims with total >= threshold require two approvals; others and unconfigured currencies require one. Snapshot the decision and submitter when creating the claim. Extend the existing approve operation to record one vote per authorized identity. For two-approval claims, the submitter cannot vote. Pending remains Pending after the first vote; the second changes it to Approved. Rejection while Pending ends the process and preserves prior votes as evidence.

**Acceptance criteria.** Existing one-approval claims retain their workflow. Policy changes do not alter submitted claims. Repeated voting by one identity returns 409 and never counts twice. A first vote alone does not make the claim reimbursable. Concurrent final approval/rejection cannot both commit. The response shows required approvals, recordedApprovalCount, voter IDs/timestamps, status, and Version without exposing credentials. A legacy Approved claim may have no recorded votes; preserve its status and label missing historical evidence rather than inventing approvers or downgrading it.

**Implementation plan.**

1. Write the threshold and state table: no policy/under threshold needs one; equal/over threshold needs two; first two-approval vote leaves Pending.
2. Add policy, claim snapshot, and vote mappings, uniqueness, and legacy defaults through the schema lane.
3. Implement versioned policy upsert with client management checks and normalized currency. Read one policy version when accepting a new claim.
4. During claim creation, compute TotalAmount from normalized items, capture submitter and required approvals, and persist the policy snapshot with the claim/items.
5. Replace direct route-to-Approve wiring with a service that loads claim/contract/items/votes, authorizes, requires Pending, and checks duplicate/submitter rules.
6. Insert the vote and explicitly touch claim Version. Only when the required count is reached call the existing domain approval transition; save vote, status, and version atomically.
7. Keep rejection Pending-only and on the same claim token. Preserve vote rows after rejection; they describe attempted approvals, not a final Approved status.
8. Audit payroll to retain explicit Approved-only selection. No polling job or intermediate Approved status is allowed after the first vote.
9. If the earlier pending-expense editing story exists on your branch, reject edits after any vote and re-evaluate/snapshot policy for edits before the first vote. Do not allow reviewed amounts to change under existing votes.
10. Test exact threshold, self-vote restriction, duplicate voters, policy changes, concurrency, and payroll inclusion. Document the API-identity limitation and absence of an administrator bypass in this first version.

**Test scenarios.** Threshold 100.00: claim 99.99 needs one, 100.00 needs two. Submitter A creates 150.00; A cannot vote, B's vote leaves Pending, C's vote makes Approved. B voting twice does not complete it. Change the threshold to 200 after submission: that claim still needs two. Race C's final approval against rejection and verify one final state and no contradictory persisted vote/status result.

**Explain it twice.** Identify the policy snapshot and aggregate version that protect the decision. Then explain why changing next month's approval policy must not silently reinterpret a claim already being reviewed.

## A reusable review exercise

After implementing one story, write a short PR that answers these questions in order:

1. What can a user do now, with one concrete before/after example?
2. Where is ownership checked, and what happens with a foreign ID?
3. Which values are current inputs, saved snapshots, or estimates?
4. What commits together, and what remains after a failed attempt?
5. What happens on a retry, competing write, or process restart where relevant?
6. Which tests did you actually run, and what limitation remains for existing databases or larger datasets?

Then explain the same story without technical vocabulary. For example: “We remember the numbers used when payroll was created, so a later price change cannot alter the explanation of that payslip.” Return to the code and point to the exact persisted fields supporting that sentence. Moving between a clear user promise and concrete implementation evidence is the mid/senior skill these exercises are designed to strengthen.
