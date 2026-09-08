# 30 mid-level feature stories: learn Atlas by extending it

This workbook preserves the original requirements for 30 features now being implemented through the [bootcamp](bootcamp/README.md). **Use the [progress register](bootcamp/progress.md) to distinguish planned, implemented, and verified work.** These stories extend the API and are separate from the [25 junior stories](16-junior-user-stories.md). You do not need to finish that entire workbook first.

Each story gives you a small product decision to own, a route through the current code, an explicit implementation sequence, and evidence to collect. The mid-level challenge is connecting several parts of the system without breaking their promises: authorization, stored financial snapshots, workflow states, dates, and concurrent updates. Work on one story at a time. Finishing three carefully is more useful than opening thirty branches.

## How to use this workbook

Read a story three times, with a different question each time:

1. **Product pass:** What can the user do afterward? Write one example with actual dates and amounts.
2. **Code pass:** Which endpoint receives the request, which rule decides the outcome, and which rows are read or written?
3. **Proof pass:** What test would fail if another client's data leaked, the request ran twice, or a date crossed a boundary?

In other words: understand the promise, trace the path, then prove the promise. You do not have to hold the entire repository in your head. Keep a note containing the route, the owning entity, and the next failing test.

The proposed routes and DTO fields in this file are design decisions for the exercises; they do not describe currently available endpoints. Names of **new files** are suggestions and appear as code, not links. Existing source links are relative to this document.

## Choose a story

“Schema” means new tables or columns; the schema workflow below applies. Dependencies are required only where listed. Optional combinations are explained inside the stories.

| ID | New user-facing capability | Main learning | Schema | Depends on |
| --- | --- | --- | --- | --- |
| [MS-01](#ms-01-client-workforce-dashboard) | Client workforce dashboard | Scoped aggregates | No | None |
| [MS-02](#ms-02-worker-employment-timeline) | Worker employment timeline | Combining authorized histories | No | None |
| [MS-03](#ms-03-contract-activation-readiness) | Activation readiness explanation | Shared business rules | No | None |
| [MS-04](#ms-04-complete-onboarding-items-in-one-request) | Atomic checklist completion | Transactions and concurrency | Yes | None |
| [MS-05](#ms-05-contract-notes) | Contract notes | Append-only records and authorship | Yes | None |
| [MS-06](#ms-06-country-onboarding-templates) | Country onboarding templates | Configuration versus snapshots | Yes | None |
| [MS-07](#ms-07-client-payslip-browser) | Client payslip browser | Financial authorization | No | None |
| [MS-08](#ms-08-payslip-csv-export) | Payslip CSV export | Reusing queries and safe CSV | No | MS-07 |
| [MS-09](#ms-09-invoice-cost-breakdown) | Invoice cost breakdown | Reconciliation | No | None |
| [MS-10](#ms-10-invoice-csv-export) | Invoice CSV export | Bounded financial exports | No | None |
| [MS-11](#ms-11-record-invoice-payments) | Record invoice payments | Idempotency and balance invariants | Yes | None |
| [MS-12](#ms-12-reverse-an-invoice-payment) | Reverse a recorded payment | Compensating ledger entries | Yes | MS-11 |
| [MS-13](#ms-13-outstanding-invoice-balances-and-aging) | Outstanding balances and aging | Historical ledger projections | No | MS-11, MS-12 |
| [MS-14](#ms-14-client-leave-calendar) | Client leave calendar | Interval overlap | No | None |
| [MS-15](#ms-15-leave-request-quote) | Leave request quote | Read-only workflow previews | No | None |
| [MS-16](#ms-16-cancel-approved-future-leave) | Cancel approved future leave | State transitions and reservations | No | None |
| [MS-17](#ms-17-country-holiday-calendar) | Country holiday calendar | Calendar rules and saved facts | Yes | None |
| [MS-18](#ms-18-leave-allowance-adjustments) | Leave allowance adjustments | Shared invariants across services | Yes | None |
| [MS-19](#ms-19-edit-a-pending-expense-claim) | Edit a pending expense claim | Aggregate updates | No | None |
| [MS-20](#ms-20-withdraw-a-pending-expense-claim) | Withdraw a pending expense claim | Explicit lifecycle extension | No | MS-19 |
| [MS-21](#ms-21-monthly-client-expense-summary) | Monthly expense summary | Grouping at the correct granularity | No | None |
| [MS-22](#ms-22-benefit-selection-quote) | Benefit selection quote | Pure money calculations | No | None |
| [MS-23](#ms-23-contract-benefit-calendar) | Contract benefit calendar | Historical coverage semantics | No | None |
| [MS-24](#ms-24-termination-base-final-pay-preview) | Base final-pay preview | Effective salary and estimates | No | None |
| [MS-25](#ms-25-amendment-comparison-preview) | Amendment comparison preview | Current terms versus history | No | None |
| [MS-26](#ms-26-api-key-expiration) | API key expiration | Authentication and time | Yes | None |
| [MS-27](#ms-27-replace-an-api-key-immediately) | Immediate API key replacement | Atomic credential lifecycle | Yes | MS-26 |
| [MS-28](#ms-28-saved-report-presets) | Saved report presets | Typed configuration and ownership | Yes | None |
| [MS-29](#ms-29-client-approval-work-queue) | Client approval work queue | Combining bounded queries | No | None |
| [MS-30](#ms-30-pause-and-resume-country-hiring) | Pause and resume country hiring | Concurrency across entry points | Yes | None |

Suggested progression: MS-01, MS-07, MS-09, MS-14, MS-15, MS-19, then a story with a schema change. The payment sequence MS-11 through MS-13 is a separate three-part project. You may stop after any completed story; avoid starting dependent work against imaginary entities.

## Shared implementation recipe

### Navigate before editing

Start with [Program.cs](../src/Atlas.Api/Program.cs), the relevant endpoint file, its domain entity, and [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs). API code handles HTTP and caller permissions. Domain code describes business rules without depending on HTTP or EF. Infrastructure loads and saves records. A read-only report can follow the existing API [ReportService](../src/Atlas.Api/Reporting/ReportService.cs) pattern; do not make Domain depend on API response records.

Use [AtlasApiFactory](../tests/Atlas.Tests/Integration/AtlasApiFactory.cs) and a nearby endpoint test as your starting examples. Reuse their actual setup helpers after reading them; helper names in your own tests need not match this workbook. For every story, introduce a focused test class named after the feature, alongside the existing integration tests. Pure arithmetic or state transitions also deserve domain tests. Read [the testing guide](../docs/testing.md) when the setup is unfamiliar.

### Apply these HTTP and data rules to every story

- Require authentication. For a new client-owned detail route, a missing or foreign parent returns **404**; a viewer attempting to change a visible parent gets **403**. Own-client admins and platform admins may write unless a story explicitly makes the action platform-only. Own-client viewers may read. Scope collections in the database query before paging or aggregation. Consult [AuthorizationExtensions](../src/Atlas.Api/Auth/AuthorizationExtensions.cs).
- Use **400** with field errors for malformed or out-of-range input, **409** for an otherwise valid request blocked by state or a stale version, **201** for a newly created resource, and **200** for successful reads or state changes. Specific replay behavior is stated where relevant. Do not expose database exception text.
- Use explicit response DTOs, cancellation tokens for database calls, stable ordering, and [Pagination](../src/Atlas.Api/Pagination.cs) where a story says “paged.” Register any new endpoint group in Program. Do not serialize tracked entity graphs.
- Use `decimal` for money and keep currencies separate. Use the existing rounding conventions. Dates representing a day use `DateOnly`; timestamps use UTC. New “today” or “now” decisions use injected `TimeProvider`, with a fixed clock in tests. Unless an existing route has a different established limit, new calendar requests accept years 2000 through 2100 and validate ranges before arithmetic.
- A preview is a read: it must not insert rows, reserve balances, or increment versions. A stored payslip or invoice is a historical financial snapshot: do not recalculate it from today's salary, benefit price, or client fee.
- Preserve existing routes and response fields. Adding a `Version` field is additive, but update every response constructor and related assertions. A story that intentionally changes a business rule must test its old entry points too.

### Handle schema changes honestly

The current development startup uses `EnsureCreated`, not a migration chain. **Restarting against an existing database will not add your new columns.** Read [ADR 0001](../docs/adr/0001-sqlite-with-ensurecreated.md) before any story marked “Yes.”

For the learning implementation, use this bounded path:

1. Record which new tables, columns, foreign keys, indexes, and delete behaviors the story needs before changing the model.
2. Add entity/configuration changes. Let the test factory create a fresh database, as it already does. Add persistence tests for required constraints, not just object constructors.
3. For manual development, choose a **new, uniquely named SQLite file** through the connection-string setting already used by Program. Keep the original database. Start in Development so the current creation/seeding path can initialize the new file.
4. Verify both seeded examples and an empty valid collection. Record the new database filename in your local notes, not as a committed secret or binary database.
5. State in your PR that validation used a new database. An upgrade of an existing populated installation is a separate required delivery task; this exercise does not silently provide it.

If you choose to deliver an existing-database upgrade as well, establish and verify an EF migration baseline from the **unchanged model before the feature changes**, then add the feature migration. Test the upgrade on a disposable copy and check data and constraints afterward. A database created by `EnsureCreated` does not already have migration history: do not blindly run an initial create migration against it. Baseline adoption needs an explicitly reviewed procedure. That larger rollout is not needed to learn the bounded feature on a fresh database.

### Handle competing writes deliberately

Read [IVersioned](../src/Atlas.Domain/IVersioned.cs), the version bumping in DbContext, and [ConcurrencyEdgeTests](../tests/Atlas.Tests/Integration/ConcurrencyEdgeTests.cs). An `expectedVersion` check detects an already stale client; EF's concurrency token detects changes after your read. You need both where a story requests a versioned write.

When a rule spans child rows, choose the parent named in the story as a shared concurrency anchor. Load it before checking the rule, explicitly mark its `Version` property modified when only children change, and save the parent and children atomically. Assigning a property its existing value alone may leave EF considering it unchanged. Let DbContext perform its normal version increment. **Every competing writer must participate**, not just the new endpoint. Add a real unique index when uniqueness is part of the rule; a preliminary `AnyAsync` check is not a uniqueness guarantee.

Use two contexts against the same test database and deliberately arrange read/read/write/write to test conflicts. Avoid sleeps as synchronization. A failed write must leave no partial child rows. Only translate recognized uniqueness/concurrency failures to 409; do not turn every database failure into a business conflict. SQLite lock contention is a separate database outcome; these exercises do not prove distributed guarantees on a different database engine.

### Finish each story in the same way

1. Add a failing behavior test, implement the smallest slice, then add the named edge cases. Check the persisted result using a fresh context after write requests.
2. Run the focused test class, then the existing related endpoint/domain tests. After the feature is finished, run the full suite once and investigate regressions.
3. Update [the API reference](../docs/api-reference.md) with one request, response, permissions, and failure example. Add a short “what I learned” note in your own branch.
4. Check links with `python scripts/learn.py check-docs`. Run .NET commands from the repository root with the SDK selected by `global.json`:

   ```powershell
   dotnet test tests/Atlas.Tests/Atlas.Tests.csproj --filter "FullyQualifiedName~YourFeatureTests"
   dotnet test tests/Atlas.Tests/Atlas.Tests.csproj -c Release
   ```

   Replace `YourFeatureTests` with the class you actually created. If `dotnet` is not on PATH, use your installed executable; on the workspace where this guide was written it was `C:\Users\E\.dotnet\dotnet.exe`. Do not assume that path exists on another computer.

5. In the PR, state the observable behavior, constraints, tests actually run, and any schema rollout limitation. “Existing tests pass” alone does not demonstrate the new capability.

## MS-01: Client workforce dashboard

**User story.** As a client administrator, I want a small workforce overview for a chosen day so that I can see how many people we employ and where they work without downloading contracts and counting them myself.

**Current gap and learning.** Contracts and a platform headcount report exist, but there is no client dashboard. You will learn to define a metric before writing a query, reuse employment-date semantics, and apply tenant isolation before aggregation.

**Read first.** [ClientEndpoints](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), and [ReportService](../src/Atlas.Api/Reporting/ReportService.cs). Trace the existing headcount predicate; a terminated contract can still cover a historical day.

**Feature contract.** Add `GET /api/clients/{clientId}/workforce-summary?asOf=2026-09-01`. Omitted `asOf` means UTC today. Return `clientId`, `asOf`, `workerCount`, `contractCount`, and country groups with both counts. Include non-Draft contracts whose inclusive employment interval covers the day. Count distinct WorkerIds for people, contract IDs for contracts. This is reconstructed employment coverage from current records, not an audit of what the database knew on that day. No schema change or dependency.

**Acceptance criteria.** Own-client readers and platform admins can view it; another client's ID produces 404. Empty data returns zero and an empty country list. Start and end dates count inclusively. Multiple covering contracts for one worker do not inflate the overall worker count; country counts are independently distinct within each country.

**Implementation plan.**

1. Write a fixture with two clients, two countries, Draft/Active/Terminated contracts, and one worker represented twice. Write expected counts on paper.
2. Add a response record with distinct names for people and contracts; avoid the ambiguous field `total`.
3. Validate the date, load the visible client, then start a no-tracking contract query filtered by that client.
4. Apply the coverage predicate and project only contract ID, WorkerId, and CountryCode. Keep Draft excluded even if its dates overlap.
5. Aggregate the bounded projected data or use translatable database grouping. Do not load onboarding, salaries, or full workers to count IDs.
6. Compute the overall distinct worker count separately from country totals. Sort country groups by code.
7. Map the route in ClientEndpoints or a dedicated dashboard group. Pass the resolved date into the query logic so tests do not depend on the wall clock.
8. Add the authorization, boundary, and empty-result tests, then document the meaning of each metric.

**Test recipe.** A contract ending September 1 appears on September 1 and disappears September 2. A Draft never counts. A worker with two covering contracts yields `workerCount=1`, `contractCount=2`. Adding ten foreign-client contracts changes none of the client's results. Compare the no-date request to an explicit date using a fixed clock.

**Explain before marking done.** Why can adding country worker counts overcount the overall number of people? Show exactly where client scope enters your query.

## MS-02: Worker employment timeline

**User story.** As a client administrator, I want one chronological timeline for a worker's employment with my company so that I can understand contract starts, salary changes, and endings without visiting several endpoints.

**Current gap and learning.** Atlas stores contract lifecycle timestamps and salary records separately. A worker can be visible through one client while also having records associated with another. This story teaches authorization across several related queries and explains the limits of reconstructing history.

**Read first.** [WorkerEndpoints](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), and [SalaryRecord](../src/Atlas.Domain/Entities/SalaryRecord.cs).

**Feature contract.** Add `GET /api/workers/{workerId}/employment-timeline` with existing page/pageSize conventions. Events are `contract-created`, `contract-activated`, `salary-effective`, and `contract-ended`. Return event kind, source ID, contract ID, effective date, nullable recorded timestamp, and a typed summary containing only relevant fields. Use salary EffectiveDate and contract EndDate for effective events; derive creation/activation calendar dates from their UTC timestamps. Sort descending effective date, then kind, then source ID. No new audit table and no schema change.

**Acceptance criteria.** A caller sees only events belonging to contracts they can view. An invisible worker returns 404. Platform admins see all of that worker's contracts. The response does not claim every edit or approval was historically captured. Salary changes on the same date have deterministic ordering.

**Implementation plan.**

1. Create a shared worker fixture with a contract for client A and a separate contract for client B.
2. Inspect worker visibility, then build the authorized contract-ID query for this endpoint. Do not stop authorization after confirming that the worker is visible.
3. Project the visible contracts' lifecycle data and only their SalaryRecords. Exclude API keys, decision notes, and unrelated worker documents.
4. Introduce an internal event model with a stable composite identity such as kind plus source ID. A contract contributes several different event kinds.
5. Convert each source into events according to the explicit date rules. Skip activation/end events when their required date is absent.
6. Merge, sort, and page the events; calculate pagination metadata over the merged authorized set. Explain that this simple implementation suits one worker's bounded history, not an organization-wide audit feed.
7. Return explicit DTOs and keep absent timestamps null instead of inventing them.
8. Add tests and an API example that shows a future-effective salary record and a historical end date.

**Test recipe.** Client A sees none of B's salary amounts, titles, contract IDs, or events. A contract created and activated on the same day yields two distinct events. Adjacent pages with tied dates neither duplicate nor omit events on an unchanged dataset. A Draft contributes creation and salary-record events if present, but no fabricated activation.

**Explain before marking done.** What is the difference between an event's effective date and when somebody recorded it? Why is worker-level authorization insufficient here?

## MS-03: Contract activation readiness

**User story.** As a client administrator, I want an explanation of everything currently blocking activation so that I can finish onboarding without repeatedly trying an action that fails one reason at a time.

**Current gap and learning.** The checklist exposes completion, while contract activation enforces rules in another handler. You will create a read-only explanation that shares rules with the write path instead of drifting into a second definition of “ready.”

**Read first.** [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), [OnboardingEndpoints](../src/Atlas.Api/Endpoints/OnboardingEndpoints.cs), and [OnboardingItem](../src/Atlas.Domain/Entities/OnboardingItem.cs).

**Feature contract.** Add `GET /api/contracts/{contractId}/activation-readiness`. Return contract ID, current Version, `canActivate`, and blockers with stable codes `not-draft` and `required-onboarding-incomplete`; incomplete items include their IDs and titles. Optional items never block activation. This story reports current activation rules; add a `country-paused` blocker only when MS-30 is implemented and the activation path also enforces it. No schema change.

**Acceptance criteria.** Every missing required item is reported in stable Type/Id order. An already Active or Terminated contract has `canActivate=false`. A ready Draft yields no blockers. Reading readiness changes no rows or versions. Activation checks readiness again and never trusts a previous GET response.

**Implementation plan.**

1. Read the complete existing activation handler and list its actual business checks separately from authorization checks.
2. Add a small assessment model containing structured blocker codes. Keep HTTP status codes out of the assessment.
3. Extract the state/checklist assessment into shared logic. Preserve the existing activation error contract when adapting structured blockers back to its error response.
4. Load the visible contract and relevant checklist once for the GET. Do not return foreign checklist IDs in an error.
5. Map the assessment to `canActivate` and blocker DTOs; include Version as a clue that this is a point-in-time assessment.
6. Call the same assessment from activation immediately before the existing transition. Keep the domain transition's own state guard.
7. Add a test that fetches “ready,” changes the contract state, and then attempts activation. The stale earlier assessment must grant no permission.
8. Verify existing activation tests still pass and document the optional MS-30 integration point.

**Test recipe.** Four incomplete required items produce four item references; completing one removes only that item. An incomplete optional TaxForms item does not block. Repeated GETs leave Version and completion timestamps identical. A viewer may read readiness but cannot activate. A foreign caller gets 404.

**Explain before marking done.** Why is readiness useful even though it cannot guarantee a later activation will succeed? Identify which shared rule prevents the read and write endpoints from disagreeing.

## MS-04: Complete onboarding items in one request

**User story.** As a client administrator, I want to complete several checklist items together so that one reviewed onboarding submission either succeeds fully or leaves the checklist untouched.

**Current gap and learning.** Items currently complete individually and have no concurrency token. You will practice validating a batch before mutation and enforcing all-or-nothing persistence when single-item and batch requests compete.

**Read first.** [OnboardingEndpoints](../src/Atlas.Api/Endpoints/OnboardingEndpoints.cs), [OnboardingItem](../src/Atlas.Domain/Entities/OnboardingItem.cs), and DbContext's `IVersioned` configuration.

**Feature contract.** Add `POST /api/contracts/{contractId}/onboarding/complete-batch` accepting `items: [{ itemId, expectedVersion, notes }]`, with 1–20 distinct IDs. Notes are optional, trimmed, and at most 1000 characters. Every item must belong to the visible contract and be incomplete. A missing/nonmember item returns 404; duplicates or bad notes return 400; completed/stale items return 409. Success returns the updated checklist. **Schema:** add Version and implement IVersioned on OnboardingItem; expose Version in existing checklist responses. No dependency.

**Acceptance criteria.** One invalid member prevents every completion. All successful items share one clock timestamp. The existing single-item route still works and participates in item concurrency. Repeating a successful batch returns 409 without replacing notes or timestamps.

**Implementation plan.**

1. Follow the schema recipe for the new item Version column and prepare a fixture with three incomplete items.
2. Extend item responses with Version; update constructor usages. Let the existing single completion use the new EF token automatically.
3. Add the batch DTO and validate count, duplicate IDs, nonnegative expected versions, and note lengths before changing any entity.
4. Authorize the parent contract, then load only requested items using both ContractId and item IDs.
5. Check membership count, all states, and every supplied version. Collect enough detail for a useful safe error; do not mutate while still checking membership.
6. Capture the clock once. Call the existing `Complete` domain method for each validated item.
7. Save all changes in one SaveChanges call. Let recognized concurrency failures become 409 through the existing handling; a failed save must not commit a subset.
8. Reload/project the checklist after success. Test a two-context single-versus-batch conflict and confirm the failed batch's other item stayed incomplete.
9. Update examples to show retrieving versions before submitting a batch.

**Test recipe.** Submit two valid items and one foreign-contract ID: zero completions persist. Race a batch covering A/B against a single completion of A: the losing batch must not complete B. Test 0, 1, 20, and 21 items, duplicate IDs, stale versions, and exactly 1000 versus 1001 note characters.

**Explain before marking done.** Why does one SaveChanges matter? Why is checking `IsCompleted` before the save insufficient without a concurrency token?

## MS-05: Contract notes

**User story.** As a client administrator, I want to append explanatory notes to a contract so that my team can understand operational decisions without changing the contract's employment terms.

**Current gap and learning.** Decision notes belong to individual workflows, but a contract has no general discussion record. This story teaches author identity, foreign keys, bounded input, and the difference between append-only application behavior and immutable infrastructure.

**Read first.** [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), [ApiKeyAuthenticationHandler](../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs), and [ApiUser](../src/Atlas.Domain/Entities/ApiUser.cs).

**Feature contract.** Add GET and POST at `/api/contracts/{contractId}/notes`. POST accepts only `body`, trimmed length 1–2000. Return note ID, contract ID, body, author API-user ID, and CreatedAtUtc. GET is paged, newest timestamp then ID first. Notes are visible to all users who can view the contract, including client viewers; they are not a private HR channel. **Schema:** new ContractNote table with required ContractId/AuthorApiUserId foreign keys and a ContractId/CreatedAtUtc/Id index. Restrict deletion of referenced parents. No edits or deletes in this slice.

**Acceptance criteria.** Caller identity and timestamp come from the server. A body cannot nominate a different author. Own-client admins and platform admins can append notes on any contract state. Viewers can read but not append. Adding a note leaves salary, lifecycle state, and contract Version unchanged.

**Implementation plan.**

1. Add the new entity and mapping following the schema recipe. Keep the body as plain text; there is no HTML rendering or attachment upload.
2. Inspect the authentication handler's NameIdentifier claim and parse the current API-user ID through a small helper if needed.
3. Create request/response DTOs that exclude client-supplied author, timestamp, and contract fields.
4. Authorize the route's contract before reading or inserting child rows. Validate the normalized body length.
5. Construct one note with the injected clock and authenticated ID; save once and return 201.
6. Add scoped GET `/api/contracts/{contractId}/notes/{noteId}` and use that path in the Location header. Check that the note belongs to the visible route contract, not just that its ID exists.
7. Implement the list query with no tracking, stable ordering, and existing pagination metadata.
8. Test permissions, persisted authorship, parent integrity, and the absence of accidental contract mutations. Document append-only as the API policy, not a tamper-proof audit claim.

**Test recipe.** Submit whitespace, 2000 characters, and 2001 characters. Create notes with equal timestamps and verify stable pages. Deactivate an author after creation: the note remains readable with the same author ID. A request under client B cannot read or create notes for A's contract.

**Explain before marking done.** Why is authorship derived from the principal? What prevents API editing, and what would still allow a database operator to change a note?

## MS-06: Country onboarding templates

**User story.** As a platform administrator, I want to configure the checklist used for new contracts in each country so that onboarding requirements can vary without changing source code for every country.

**Current gap and learning.** Contract creation currently generates the default checklist. You will separate mutable configuration from the checklist snapshot already attached to a contract, while keeping the feature limited to existing onboarding types.

**Read first.** [OnboardingItem](../src/Atlas.Domain/Entities/OnboardingItem.cs), contract creation in [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), and [CountryEndpoints](../src/Atlas.Api/Endpoints/CountryEndpoints.cs).

**Feature contract.** Add platform-only GET and PUT `/api/countries/{code}/onboarding-template`. PUT replaces the whole template with 1–5 distinct existing enum types, title length 1–200, and IsRequired. Require at least one required item. Use `expectedVersion`; an absent template is returned as `configured=false`, Version 0, and the effective default items. First configuration requires expectedVersion 0. **Schema:** a versioned country template header and child items unique by header/type. New contracts copy the effective template into independent OnboardingItems. Existing contracts never change.

**Acceptance criteria.** An unconfigured country keeps today's default behavior. A configured country creates exactly its template items with fresh IDs and incomplete state. Updating a template does not modify any existing checklist. Undefined numeric enum values, duplicates, empty templates, and all-optional templates produce 400. Concurrent replacements cannot silently overwrite one another.

**Implementation plan.**

1. Extract the current default specification into a reusable factory/value list, preserving its titles and required flags.
2. Add header and item entities, a unique country/header relationship, and child-type uniqueness. Follow the schema recipe.
3. Implement GET so both fallback and configured cases have the same response shape; distinguish them with `configured`.
4. Validate and normalize the entire PUT body, then check the expected header version. Set a newly configured header's Version to 1, reserving response Version 0 for the absent fallback. For first creation, rely on country uniqueness to reject a competing first writer.
5. Replace child rows and touch the header Version in one save. Do not perform independent delete/save/insert/save operations.
6. Change contract creation to load the template and construct new checklist entities from its values, using fallback when absent.
7. Read header/items as one coherent snapshot, using one projected query or an explicit read transaction. If contract creation races an update, using either complete template version is acceptable; mixing versions is not.
8. Save the new contract and its copied checklist through the existing atomic creation path.
9. Test fallback, replacement, two writers, and contracts created before/after a template change. Document the snapshot policy with a concrete example.

**Test recipe.** Create contract A with a required IdentityDocument item, replace the template title, then create B for another worker. A retains its old title; B receives the new one. Two PUTs using one version yield one success and one conflict. A missing country returns 404.

**Explain before marking done.** Why copy template values instead of making each contract read the live template forever? What does a first-write unique constraint protect that expectedVersion 0 cannot?

## MS-07: Client payslip browser

**User story.** As a client administrator, I want to browse my company's payslips by payroll period and inspect one payslip so that I can answer payroll questions without access to platform-wide payroll administration.

**Current gap and learning.** Payslips are available through payroll workflows, but there is no dedicated client-scoped browser. This is practice in authorizing financial records by their owner, rather than assuming that visibility of a worker grants access to all of that worker's pay.

**Read first.** [PayrollEndpoints](../src/Atlas.Api/Endpoints/PayrollEndpoints.cs), [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), and [Pagination](../src/Atlas.Api/Pagination.cs).

**Feature contract.** Add GET `/api/payslips` and `/api/payslips/{id}`. Lists require inclusive `fromPeriod` and `toPeriod` in `YYYY-MM`, spanning at most 12 months, with optional clientId and workerId and standard pagination. Client callers are always scoped to their own client; an explicitly foreign clientId returns 404. Platform callers may omit clientId. Include Draft and Completed run snapshots and label the run status. Return saved money components, period, currency, and associated IDs. No schema change.

**Acceptance criteria.** A payslip is authorized using its stored ClientId. Sharing a worker with another client never reveals the other client's payslip. Invalid periods return 400. Sort by period descending, then payslip ID. Empty valid filters return an empty page; a missing or foreign detail returns 404.

**Implementation plan.**

1. Inspect the payroll response mapping and list which Payslip fields are already stored, including reimbursements, benefits, and unused leave payout.
2. Add reusable period parsing/range validation and a query specification containing the caller's permitted client scope.
3. Start a no-tracking Payslips query, apply ownership first, and apply period and optional worker filters afterward.
4. Join only the PayrollRun information needed for period and status. Project stored amounts without invoking PayrollCalculator.
5. Apply stable ordering and existing paging. Keep the unpaged authorized query reusable for MS-08.
6. Implement the detail route with the same ownership rule and response mapping.
7. Map the group in Program and add tests using a worker with payslips for two clients.
8. Document that Draft-run payslips are included and that displayed amounts are saved snapshots, not recalculated estimates.

**Test recipe.** A 12-month range succeeds; 13 months, month 00, month 13, and reversed bounds fail. Modify a contract's current salary after creating a payslip: the response retains the saved gross pay. Test both client roles, platform access, missing authentication, and a guessed foreign payslip ID.

**Explain before marking done.** Which column is the financial ownership boundary? Why must changing a current contract not change a previously returned payslip amount?

## MS-08: Payslip CSV export

**User story.** As a client administrator, I want to export the payslips selected in the browser so that I can reconcile the same figures in a spreadsheet.

**Current gap and learning.** MS-07 supplies a scoped query but no export. This story teaches sharing query semantics across representations, bounding memory use, and handling text correctly in a spreadsheet-oriented format.

**Read first.** Your MS-07 query and DTOs, [Payslip](../src/Atlas.Domain/Entities/Payslip.cs), and [PayrollEndpointTests](../tests/Atlas.Tests/Integration/PayrollEndpointTests.cs). **Dependency:** MS-07 must be complete. No schema change.

**Feature contract.** Add GET `/api/payslips/export.csv` with the same required periods and optional client/worker filters, without pagination parameters. Export at most 1000 matching rows; fetch up to 1001 and reject an oversized selection with 400 plus guidance to narrow filters. Never silently truncate. Use UTF-8 `text/csv`, a fixed safe download filename, and columns for IDs, period, run status, currency, gross, deductions, net, employer cost, reimbursements, benefit shares, unused leave payout, and total cost. Use the entity's actual component meanings when naming headers.

**Acceptance criteria.** CSV and JSON select identical authorized records. Empty matches return a header-only file. Decimal formatting uses an invariant culture and no thousands separators. Text is CSV-escaped, and spreadsheet formula-like text is neutralized without modifying numeric money cells. The export performs no writes.

**Implementation plan.**

1. Write the column list and one expected two-row CSV fixture before implementing the serializer.
2. Reuse MS-07's validation and authorized query; remove paging only. Do not copy the filter logic into a second handler.
3. Fetch the stable ordered projection with a 1001-row cap and reject overflow before building the response body.
4. Add a small CSV formatter: double embedded quotes, quote fields containing commas/quotes/newlines, and separate records consistently.
5. For textual cells, inspect the first non-whitespace character for `=`, `+`, `-`, or `@`; prefix an apostrophe when needed, and neutralize leading tab/carriage-return formula tricks. Then apply CSV escaping. Never apply this text policy to decimal columns.
6. Format stored decimal values with invariant culture and two fractional digits; format period and IDs explicitly.
7. Return bytes with CSV content type and a server-chosen Content-Disposition filename. Keep secrets and unrequested worker details out of the export.
8. Test serializer behavior separately from endpoint authorization and row selection, then add an API example.

**Test recipe.** Test 0, 1, 1000, and 1001 rows. Run formatting under a comma-decimal culture and verify `1234.50`. Unit-test text containing a quote, newline, comma, leading spaces before `=`, and a leading tab; round-trip structural escaping with a CSV reader or explicit fixtures. Compare exported payslip IDs to the browser's complete matching set.

**Explain before marking done.** Why is quoting a CSV cell different from neutralizing a spreadsheet formula? Why reject oversized exports instead of returning the first 1000 rows as if complete?

## MS-09: Invoice cost breakdown

**User story.** As a client administrator, I want to see the payslip costs behind an invoice so that I can reconcile its subtotal and fee without guessing how payroll and billing connect.

**Current gap and learning.** Invoices contain stored totals and payroll/client IDs, while their underlying costs live in Payslips. You will join those snapshots and explain discrepancies without silently recomputing historical fees.

**Read first.** [InvoiceEndpoints](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), [Invoice](../src/Atlas.Domain/Entities/Invoice.cs), and `CompleteRun` in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs).

**Feature contract.** Add GET `/api/invoices/{id}/breakdown`. Return the stored invoice financial fields plus payslip rows matching both PayrollRunId and ClientId. Rows expose payslip/contract/worker IDs and stored gross, employer contributions, reimbursements, benefit components, unused leave payout, and TotalCost. Return `calculatedPayslipSubtotal` and `subtotalDifference = invoice.PayrollSubtotal - calculatedPayslipSubtotal`. Show stored ManagementFee and FX fields separately. No schema change.

**Acceptance criteria.** Ownership is checked on the invoice before returning child rows. For a normal newly completed run, the summed TotalCost equals the stored subtotal and the difference is zero. A nonzero difference is reported explicitly rather than concealed. Never derive an old fee from the client's current ManagementFeeRate or distribute a rounded FX total across workers in this slice.

**Implementation plan.**

1. Trace exactly which Payslip field CompleteRun sums and how it stores invoice fee and billing-currency totals.
2. Write a response example with two payslips, a subtotal, a management fee, and a different billing currency.
3. Load the visible invoice and query child payslips by both run and client IDs. Sort by payslip ID.
4. Project stored components. If adding worker names for convenience, label them as current display names; IDs and financial snapshots remain the reconciliation basis.
5. Sum TotalCost using decimal and compute the signed difference. Do not sum GrossSalary plus TotalCost, which would double-count wages. GrossSalary already includes UnusedLeavePayout in final-month payslips; the payout is an explanatory component, not another amount to add to gross.
6. Return the stored fee, total, billing currency, FX rate, and billing total without rerunning payroll or FX selection.
7. Add happy-path tests plus an intentionally inconsistent fixture to prove the difference field is meaningful.
8. Document which subtotal reconciles in employment currency and why billing-currency conversion is shown only at invoice level.

**Test recipe.** Payslip costs 1100.00 and 550.00 produce 1650.00. An invoice subtotal of 1650.01 reports a 0.01 difference. Change the current client fee and FX table after invoice creation: stored invoice values remain unchanged. A same-run payslip belonging to another client is excluded.

**Explain before marking done.** Why is a reported discrepancy preferable to rewriting an invoice? Which fields are saved financial facts and which optional labels can change later?

## MS-10: Invoice CSV export

**User story.** As a client administrator, I want a downloadable list of invoices issued during a date range so that I can reconcile billing in my existing spreadsheet.

**Current gap and learning.** Invoice list and detail routes exist, but no bounded CSV representation exists. This story reinforces ownership and snapshot handling on a different entity. It can be completed independently of the payslip export.

**Read first.** [InvoiceEndpoints](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), [Invoice](../src/Atlas.Domain/Entities/Invoice.cs), and the date-boundary handling in [ReportService](../src/Atlas.Api/Reporting/ReportService.cs).

**Feature contract.** Add GET `/api/invoices/export.csv?issuedFrom=2026-09-01&issuedTo=2026-09-30`, optional clientId for platform admins, maximum 366 inclusive days, maximum 1000 rows. Filter using UTC calendar days, including the entire final day. Columns: invoice ID/number, client ID, payroll run ID, issued UTC timestamp, local currency, subtotal, management fee, total, billing currency, applied FX rate, and total in billing currency. Do not add payment status until MS-13 exists. No schema change or dependency.

**Acceptance criteria.** Client callers receive only their own invoices. Too many matching rows returns 400, not a partial export. Empty results contain headers. Monetary totals match existing invoice detail values. FX rates retain their stored precision instead of being formatted as two-decimal money.

**Implementation plan.**

1. Define the column order and separate formatting rules for money, FX rates, timestamps, and text.
2. Validate both dates and the inclusive range length before constructing UTC boundaries.
3. Authorize any explicit client filter and build a no-tracking, scoped invoice query. Use start-inclusive/end-exclusive timestamp bounds for the requested days.
4. Order by IssuedAtUtc then Id and take 1001 rows to detect overflow consistently in one bounded read.
5. Extract a shared CSV helper if MS-08 exists; otherwise implement the quoting and text-formula policies described there without depending on its route.
6. Format money with two invariant decimal places and FX using a decimal format that preserves its significant stored digits. Use a round-trip UTC timestamp representation.
7. Return a fixed download filename and CSV content type. Do not send the export by email or call an accounting provider.
8. Verify selection, formatting, content headers, and authorization in focused tests; add documentation distinguishing issue dates from payroll periods.

**Test recipe.** Include an invoice at 23:59:59 UTC on the final day and exclude the next day's midnight. A stored FX rate of 1.234567 remains 1.234567. A comma or quote in an invoice-number fixture is escaped correctly. An own-client export must not contain another client's IDs or amounts.

**Explain before marking done.** Why are issue date and payroll month different filters? Why would formatting an exchange rate like a monetary total change its meaning?

## MS-11: Record invoice payments

**User story.** As a platform administrator, I want to record a payment received against an invoice so that Atlas can show how much remains unpaid without changing the original invoice.

**Current gap and learning.** Atlas issues invoices but has no payment ledger. This story records an external fact; it does not charge a card or move money. You will learn idempotency, append-only amounts, and a cross-row invariant protected by a shared parent.

**Read first.** [Invoice](../src/Atlas.Domain/Entities/Invoice.cs), [InvoiceEndpoints](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), and DbContext's concurrency logic.

**Feature contract.** Add platform-only POST `/api/invoices/{id}/payments` with amount, currencyCode, paidAtUtc, and reference. Add scoped GET on the same collection. Amount is positive with at most two decimal places, normalized currency equals the invoice BillingCurrencyCode, and IssuedAtUtc <= PaidAtUtc <= server now. Trim reference, require 1–100 characters, and compare it case-sensitively. Exact replay of the same normalized reference/payload returns the original payment with 200; different payload under that reference returns 409. **Schema:** InvoicePayment plus Invoice.Version/IVersioned; unique `(InvoiceId, Reference)` and restricted foreign-key deletion.

**Acceptance criteria.** A first payment returns 201 and appears in the ledger. Sum of payments cannot exceed stored TotalInBillingCurrency. The original invoice totals never change. Two competing payments cannot both consume the same remaining balance. Clients can read their ledger but cannot record payments.

**Implementation plan.**

1. Add payment ID, InvoiceId, Amount, CurrencyCode, PaidAtUtc, RecordedAtUtc, Reference, and recording API-user ID; configure constraints and the invoice concurrency anchor.
2. Put payment validation and remaining-balance calculation in a focused service. Keep HTTP adaptation in endpoints.
3. Authorize before looking up reference matches. Normalize input and distinguish externally supplied PaidAtUtc from server RecordedAtUtc.
4. Load the tracked invoice before reading its payments. If the reference exists, compare normalized amount/currency/timestamp and return the documented replay or conflict result without writing.
5. Calculate remaining balance in billing currency from the ledger. If MS-12 is later present, subtract only unreversed net payments through the shared balance helper.
6. Reject an overpayment, insert the new payment, explicitly touch invoice Version, and save both atomically.
7. On a recognized uniqueness/concurrency conflict, use a fresh context to check for an exact reference replay. Return that payment or 409; do not reuse a failed tracked change set or blindly retry a different payment.
8. Implement a paged ledger sorted PaidAtUtc then Id, with the invoice ownership check and a discoverable payment detail Location.
9. Add two-context invariant tests and document that this endpoint records payments already received elsewhere.

**Test recipe.** A 100.00 invoice accepts 40.00, then 60.00, and rejects another 0.01. Two different 60.00 requests starting at balance 100.00 cannot both persist. An exact retry produces one ledger row; changing the amount under the same reference conflicts. Reject 0, negative amounts, 1.001, wrong currency, future payment time, and dates before issuance.

**Explain before marking done.** Why do you need both reference uniqueness and invoice concurrency? Why must a payment alter the ledger while leaving the invoice's original total intact?

## MS-12: Reverse an invoice payment

**User story.** As a platform administrator, I want to reverse an incorrectly recorded payment so that the balance is corrected while the original payment remains explainable.

**Current gap and learning.** MS-11 introduces an append-only ledger. Deleting a mistake would destroy its history. This story introduces a compensating entry and makes both payment and reversal writers share the same invoice balance rule.

**Read first.** Your MS-11 payment service, InvoicePayment mapping, and [ConcurrencyEdgeTests](../tests/Atlas.Tests/Integration/ConcurrencyEdgeTests.cs). **Dependency:** MS-11. **Schema:** new InvoicePaymentReversal table with a unique PaymentId, unique invoice/reference pair, required reason, RecordedAtUtc, and actor ID.

**Feature contract.** Add platform-only POST `/api/invoices/{invoiceId}/payments/{paymentId}/reverse`, accepting reference and reason. Reference uses MS-11 normalization/length rules; reason is trimmed length 1–1000. Reverse the entire original amount only, at server now; no partial or backdated reversals. Exact replay with the same reference, payment, and reason returns 200; mismatches or an already reversed payment under a new reference return 409. Return 201 for a new reversal and include it in ledger reads.

**Acceptance criteria.** The payment and invoice totals remain unchanged. Net paid amount decreases by the original payment amount, so outstanding balance increases. One payment has at most one reversal. A reversal cannot target a payment on another invoice, even if the caller can view both.

**Implementation plan.**

1. Add the reversal entity with required InvoiceId and PaymentId relationships. Validate their membership in the service; configure uniqueness and restricted deletion.
2. Extract or extend the shared ledger balance calculation to subtract reversed payment amounts exactly once.
3. Load the tracked invoice first, authorize the platform action, and load the payment restricted to that invoice.
4. Normalize input, check exact replay, then reject any other reversal already attached to this payment.
5. Capture the current clock and create a reversal referencing the original amount rather than accepting a caller-supplied amount or currency.
6. Touch the same invoice Version used by MS-11 and save atomically. Handle recognized conflicts with a fresh replay lookup, as in MS-11.
7. Extend payment ledger DTOs with reversal metadata and effective net amount; retain original payment fields.
8. Test reversal-versus-reversal and reversal-versus-payment schedules. Recompute balances after both successful operations using a fresh context.
9. Document the distinction between correcting Atlas's record and initiating a refund at a payment provider.

**Test recipe.** Invoice 100.00, recorded payment 40.00: outstanding moves 60.00 to 100.00 after reversal. Replaying the reversal leaves it 100.00. Two simultaneous reversal attempts create one row. A new payment using the freed balance must be evaluated against a consistent invoice version; a losing writer receives a conflict instead of corrupting the ledger.

**Explain before marking done.** Why does reversal require a new record? What makes the original payment and its reversal two facts rather than one edited amount?

## MS-13: Outstanding invoice balances and aging

**User story.** As a platform administrator, I want invoice aging to account for recorded payments and reversals so that I can distinguish unpaid debt from invoices that have already been settled.

**Current gap and learning.** The existing aging report treats issued invoice totals as outstanding. You will build an as-of projection over a ledger while preserving currency groups and the existing age bucket definitions.

**Read first.** `InvoiceAgingAsync` in [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), [ReportEndpoints](../src/Atlas.Api/Endpoints/ReportEndpoints.cs), and your payment/reversal service. **Dependencies:** MS-11 and MS-12. No additional schema change.

**Feature contract.** Add GET `/api/invoices/{id}/balance?asOf=2026-09-30` for scoped readers and extend the platform aging report to use outstanding billing-currency balances. As-of is a UTC calendar day, defaulting to clock today. Include invoices issued by that day's end; include payments with PaidAtUtc by that cutoff and reversals with RecordedAtUtc by it. This is effective-date reconstruction using records known now: a payment recorded today with an earlier paid date can change a past report. Return invoice total, net paid, outstanding, currency, and asOf. A visible invoice issued after asOf returns 400 on the detail balance route and is excluded from aging.

**Acceptance criteria.** Outstanding equals stored billing total minus included payments plus included reversals. Fully paid invoices are excluded from aging counts and sums. Existing age boundaries remain 0–30, 31–60, 61–90, and greater than 90 days, measured from issuance rather than an invented due date. Currencies are never added together.

**Implementation plan.**

1. Write a timeline fixture with issuance, two payments, and a later reversal on different days.
2. Extract a shared balance projection accepting an explicit UTC cutoff. Keep current balance and as-of balance on the same arithmetic path.
3. Select only eligible invoices under the caller/report scope, then load their eligible ledger entries in batches. Avoid one query per invoice.
4. Group payment and reversal amounts by invoice ID and calculate decimal balances. Do not clamp negative values silently; ledger invariants should prevent them and fixtures should expose defects.
5. Add the scoped balance endpoint with explicit handling for an as-of date before issuance.
6. Change aging to sum outstanding amounts and count only positive outstanding invoices, retaining its platform policy and bucket labels.
7. Update report tests that previously assumed every invoice was unpaid; keep cases with no payments to prove compatibility.
8. Document effective-date reconstruction and show how a later reversal changes subsequent report dates only.

**Test recipe.** Invoice 100.00 issued day 1; payment 40.00 on day 10; reversal day 20. Balances on days 9, 10, and 20 are 100.00, 60.00, and 100.00. Test ages 30/31/60/61/90/91, final-day UTC timestamps, fully settled invoices, and separate USD/EUR groups.

**Explain before marking done.** Why can a backdated payment change a past report? What additional data semantics would be needed for a report of “what we knew at the time”?

## MS-14: Client leave calendar

**User story.** As a client administrator, I want a calendar feed of upcoming requested and approved absences so that I can plan team coverage across contracts.

**Current gap and learning.** Leave endpoints focus on requests and balances. This feature provides a client-wide interval view and teaches why overlap filtering differs from checking whether a request starts inside a window.

**Read first.** [LeaveEndpoints](../src/Atlas.Api/Endpoints/LeaveEndpoints.cs), [LeaveRequest](../src/Atlas.Domain/Entities/LeaveRequest.cs), and [ClientEndpoints](../src/Atlas.Api/Endpoints/ClientEndpoints.cs).

**Feature contract.** Add GET `/api/clients/{clientId}/leave-calendar?from=2026-09-01&to=2026-09-30`. Require a valid inclusive range of at most 93 days. Include Pending and Approved requests whose dates overlap it. Return request/contract/worker IDs, current worker name, leave type, status, original start/end dates, saved Days, and clipped displayStart/displayEnd. Exclude reasons and decision notes. Return a bounded maximum of 1000 rows; reject overflow with 400 and narrowing guidance. No schema change.

**Acceptance criteria.** Readers see only the selected visible client. A request starting before the window can appear. Cancelled and Rejected requests do not appear. Clipping dates never changes saved Days or pretends that Days is the count within the displayed slice. Sort by displayStart, worker ID, and request ID.

**Implementation plan.**

1. Write four intervals on paper: inside, enclosing, touching the first day, and completely outside the requested window.
2. Validate the dates and authorize the client before querying leave.
3. Filter through Contract.ClientId, allowed statuses, and `StartDate <= to && EndDate >= from`.
4. Project only fields required by the calendar. Preserve the original request dates and Days.
5. Compute displayStart as the later start and displayEnd as the earlier end. Name them distinctly from persisted dates.
6. Apply stable ordering and fetch at most 1001 records; return a complete bounded feed or a clear overflow error.
7. Add the endpoint and integration tests for overlap, status, permissions, and the result cap.
8. Document that worker names are current labels and saved Days refers to the entire request.

**Test recipe.** A request August 28–September 4 appears in a September window with displayStart September 1 and unchanged saved Days. A request ending August 31 is absent. A September 1 one-day request is included. A client with no matching leave receives an empty array. Add foreign-client leave with identical dates to prove scope.

**Explain before marking done.** Why would `StartDate >= from` miss valid calendar entries? Why must display clipping not rewrite the balance reservation?

## MS-15: Leave request quote

**User story.** As a client administrator, I want to preview the working days and remaining allowance for proposed leave so that I can adjust dates before creating a request.

**Current gap and learning.** `LeaveService.CreateRequestAsync` calculates eligibility while performing a write. This story separates assessment from persistence and keeps both routes using the same rules.

**Read first.** [LeaveService](../src/Atlas.Infrastructure/LeaveService.cs), [LeaveCalculator](../src/Atlas.Domain/Services/LeaveCalculator.cs), and [LeaveEndpoints](../src/Atlas.Api/Endpoints/LeaveEndpoints.cs).

**Feature contract.** Add POST `/api/contracts/{contractId}/leave-quote` with type, startDate, and endDate. Require manage permission to match the intended request-creation workflow. Return 200 with `eligible`, requested working days, nullable allowance/remaining figures when unavailable, and stable blocker codes. Malformed dates, reversed ranges, unsupported years, and undefined enum values return 400; valid but ineligible requests return 200 with blockers such as `contract-not-active`, `cross-year`, `outside-contract`, `no-working-days`, `missing-policy`, `overlap`, or `insufficient-balance`. No rows are created. No schema change.

**Acceptance criteria.** A quote and an immediate create evaluated against unchanged data agree on eligibility and Days. Pending and Approved requests both reserve balance. Missing policy produces an explanation rather than fabricated zero allowance. Creation still rechecks the rules; a successful quote is not a reservation.

**Implementation plan.**

1. List every existing CreateRequestAsync validation and preserve its order or intentionally document a new structured assessment order.
2. Introduce an assessment result containing calculated values and blockers. Keep it independent of API status codes.
3. Extract the shared load-and-assess path from CreateRequestAsync without moving HTTP concerns into Infrastructure.
4. Let creation call the assessment, adapt blockers to its existing DomainException/409 behavior, then construct and save the request as before.
5. Implement the quote route's input and ownership checks, then map the assessment to a read-only response.
6. If MS-17 or MS-18 has been implemented, route holiday and adjusted-allowance calculations through this same assessment; do not fork their formulas.
7. Add a fixed-data quote/create agreement test and a second test that changes reservations between quote and creation.
8. Check all row counts and relevant versions before and after repeated quotes. Add examples for both eligible and ineligible responses.

**Test recipe.** Monday–Friday requests five days when no holiday feature is present. With allowance 10 and existing Pending 3 plus Approved 2, remaining is 5; requesting 6 is ineligible. Quote a valid request, create conflicting leave separately, then attempt the original creation: it must fail. Test weekend-only and missing-policy cases.

**Explain before marking done.** Why does sharing a calculator alone not guarantee shared eligibility rules? Which work is deliberately repeated at creation time?

## MS-16: Cancel approved future leave

**User story.** As a client administrator, I want to cancel approved leave that has not started so that the worker's reserved allowance becomes available again when plans change.

**Current gap and learning.** LeaveRequest currently allows cancellation only from Pending. You will add a narrowly defined transition without weakening approval or allowing historical payroll facts to be silently rewritten.

**Read first.** [LeaveRequest](../src/Atlas.Domain/Entities/LeaveRequest.cs), [LeaveEndpoints](../src/Atlas.Api/Endpoints/LeaveEndpoints.cs), and final-pay leave usage in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs).

**Feature contract.** Add POST `/api/leave-requests/{id}/cancel-approved` with expectedVersion. Allow only Approved requests on an Active contract with no EndDate and StartDate strictly after UTC today. Transition to existing Cancelled and set DecidedAtUtc to now; preserve saved Days and prior DecisionNote. Return 409 for other states, dates, or stale versions. Expose Version in leave responses if absent. The existing pending-cancel route retains its behavior. No new columns or statuses.

**Acceptance criteria.** The next balance read releases the cancelled request's full saved Days. Cancellation on the first leave day is rejected. Terminated or end-dated contracts are rejected. Two cancellation/termination writers cannot both act on an assumed unchanged active contract. Existing payslips are never recalculated by this action.

**Implementation plan.**

1. Add a dedicated domain transition receiving explicit `today` and `now`; keep the existing Pending-only Cancel method unchanged.
2. Validate nonnegative expectedVersion and authorize the request through its contract.
3. Load the tracked contract and leave request, then check the request version, Approved state, future start, Active state, and absence of EndDate.
4. Perform the new transition; do not delete or modify the Days snapshot.
5. Explicitly touch contract Version as a shared anchor. Existing termination writes already change the contract; confirm all relevant termination paths use its concurrency token.
6. Save request and contract atomically. Return 409 for a recognized competing write, leaving the other aggregate state untouched.
7. Extend response mappings and lifecycle tests. Verify GetBalances excludes the new Cancelled result through its existing status rule.
8. Document that cancellation replaces DecidedAtUtc with the latest decision time; this slice does not introduce a full decision-history table.

**Test recipe.** With a fixed September 1 clock, approved leave starting September 2 cancels and releases its Days; September 1 and August 31 reject. Repeating cancellation returns 409. Arrange cancellation and termination from two contexts: a stale contract writer must fail. Verify request and contract persistence after the conflict.

**Explain before marking done.** Why preserve Days after cancellation? Why check the contract's version as well as the leave request's version?

## MS-17: Country holiday calendar

**User story.** As a platform administrator, I want to configure country holidays so that newly submitted leave does not consume allowance on those non-working days.

**Current gap and learning.** LeaveCalculator currently excludes only Saturdays and Sundays. This story adds explicit calendar data while keeping previously saved reservations stable and avoiding implied rules about real-world holiday observance.

**Read first.** [LeaveCalculator](../src/Atlas.Domain/Services/LeaveCalculator.cs), [LeaveService](../src/Atlas.Infrastructure/LeaveService.cs), and [CountryEndpoints](../src/Atlas.Api/Endpoints/CountryEndpoints.cs).

**Feature contract.** Add authenticated GET `/api/countries/{code}/holidays?year=2027`, platform POST on the collection with date/name, and platform DELETE `/api/countries/{code}/holidays/{id}`. Only future dates strictly after UTC today may be added or removed; names are trimmed length 1–200. One explicit date is one holiday; no automatic substitute weekdays or imported government calendar. **Schema:** CountryHoliday with unique `(CountryCode, Date)`, required country foreign key, name, and server creation timestamp. Existing LeaveRequest.Days values remain unchanged, even for previously booked future leave.

**Acceptance criteria.** New leave subtracts weekday holidays once and never subtracts a weekend twice. Quotes use the same calendar if MS-15 exists. A duplicate date returns 409. Changing a future holiday affects later assessments/submissions, not existing requests or payroll snapshots. Calendar reads include historical entries already stored.

**Implementation plan.**

1. Add the table and constraints using the schema recipe. Define country normalization and bounded year/date validation.
2. Implement the calendar routes with platform-only changes and authenticated country-scoped reads. A delete must match both country and holiday ID.
3. Extend the pure working-day calculator to accept a set of holiday DateOnly values, keeping a no-holidays overload for existing callers/tests.
4. Count each inclusive day only when it is a weekday and absent from the set. Keep reversed-range validation.
5. Have LeaveService load the relevant country's holidays for the requested interval once and pass them to the calculator.
6. Store the resulting Days at submission as today. Do not run updates against existing leave requests when a calendar entry changes.
7. Define a concurrent calendar edit as acceptable before-or-after assessment input: one assessment uses one loaded holiday set. No promise of retroactive rebalance is made.
8. Add calculator, persistence uniqueness, endpoint, and quote/create tests. Explain the saved-days policy prominently in the API reference.

**Test recipe.** September 7–11, 2026 is five weekdays; a configured September 7 holiday makes it four. Adding a Saturday holiday changes no weekday count. Submit a future five-day request, then add a holiday in its interval: the existing request remains five, while a new assessment uses four. Past-date additions and cross-country deletes fail.

**Explain before marking done.** Why can two requests for the same dates have different saved Days if the calendar changed between submissions? What user-facing wording makes that policy understandable?

## MS-18: Leave allowance adjustments

**User story.** As a platform administrator, I want to add or deduct a documented leave allowance adjustment for one contract and year so that exceptional entitlements can be represented without changing every worker's country policy.

**Current gap and learning.** Leave balances currently come directly from country policy. This story adds a small ledger and follows its effect into request validation and unused-leave payout. It is a later exercise because the same invariant crosses several services.

**Read first.** [LeavePolicy](../src/Atlas.Domain/Entities/LeavePolicy.cs), [LeaveService](../src/Atlas.Infrastructure/LeaveService.cs), and the unused-leave branch of [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs).

**Feature contract.** Add platform-only POST `/api/contracts/{contractId}/leave-adjustments` and scoped paged GET with required year. POST accepts year, defined leave type, signed nonzero integer deltaDays from -366 to 366, reason length 1–1000, and reference length 1–100. Only Active, non-end-dated contracts may receive adjustments. Effective allowance is country allowance plus all adjustments for contract/year/type; it must be nonnegative and at least Pending+Approved reserved Days. **Schema:** append-only adjustment rows with actor/time and unique contract/year/type/reference. Exact normalized replay returns 200; mismatched replay returns 409.

**Acceptance criteria.** Balances show base allowance, total adjustment, effective allowance, reservations, and remaining. New requests and MS-15 quotes use the effective allowance. Future final-pay calculations use adjusted annual allowance for the ending year; existing payslips remain unchanged. An allowance reduction cannot race a leave submission into an overdrawn balance.

**Implementation plan.**

1. Add the adjustment entity, uniqueness, and foreign keys. Retain the existing policy table as the base entitlement.
2. Extract a reusable effective-allowance query/calculation consumed by balance reads, request assessment, and payroll final-pay calculation.
3. Implement POST normalization, state checks, and replay handling under a tracked contract loaded before balance reads.
4. Sum base plus adjustments and reservations with integer arithmetic; reject a proposed result below zero or reserved Days.
5. Insert the adjustment, touch contract Version, and save atomically. Use fresh-context replay handling after recognized conflicts.
6. Update LeaveService request creation to use the same contract anchor before reading reservations and to save its new request with a touched contract Version.
7. Update leave approval, rejection, and cancellation paths to load and touch the same contract anchor when saving decisions. The current country-policy API creates policies and rejects replacements; keep that behavior in this slice. Require an existing policy before adding adjustments. If you previously added policy updates on your branch, they must also coordinate with this invariant before combining the features.
8. Use the shared adjusted allowance in future annual unused-leave payout, retaining the existing saved reservation and payout rounding rules. Termination's contract update participates in concurrency, so a stale Active-only adjustment loses cleanly.
9. Add ledger, balance, race, and final-pay tests. Document that reductions are ledger entries, not edits to earlier adjustments.

**Test recipe.** Base 20, adjustment +3, Pending 4, Approved 5 gives effective 23 and remaining 14. A -15 adjustment is rejected because effective 8 is below reserved 9. Race a reduction against a new reservation and prove no committed state is overdrawn. Show that +2 annual days changes a later final-pay estimate by two daily rates, but not an already saved payslip.

**Explain before marking done.** Which writers can break the balance rule even if the adjustment endpoint is correct? Draw the path from one adjustment row to a future unused-leave payout.

## MS-19: Edit a pending expense claim

**User story.** As a client administrator, I want to correct a pending expense claim's description and items so that a mistake can be fixed before approval without submitting a duplicate claim.

**Current gap and learning.** Expense claims can be created and decided, but not edited. Updating a parent with child items teaches aggregate boundaries: changing an item must still conflict with somebody approving the parent based on the old amount.

**Read first.** [ExpenseEndpoints](../src/Atlas.Api/Endpoints/ExpenseEndpoints.cs), [ExpenseClaim](../src/Atlas.Domain/Entities/ExpenseClaim.cs), and expense mappings in [AtlasDbContext](../src/Atlas.Infrastructure/AtlasDbContext.cs).

**Feature contract.** Add PUT `/api/expense-claims/{id}` with expectedVersion, optional description, and a complete replacement list of 1–50 items. Each item has description length 1–300, positive decimal amount, nondefault supported incurred date, and optional absolute HTTP(S) receipt URL up to 2000 characters. Claim description is at most 500 characters. Reuse existing creation semantics for amount precision and dates; this slice does not introduce an unrelated backdating ban. Only Pending claims can change. Contract, currency, SubmittedAtUtc, and claim ID are immutable. Existing Version is exposed in responses; no schema change.

**Acceptance criteria.** A successful replacement returns the new total and incremented Version. Invalid input changes neither parent nor children. Approved, Rejected, or Reimbursed claims reject edits with 409. A concurrent approval cannot approve an old total while a later child-only edit silently persists.

**Implementation plan.**

1. Extract normalized item validation from creation so edit and create agree on existing rules; apply the new explicit field/count limits to both and document those validation additions.
2. Add the update DTO without writable contract, currency, status, item IDs, or timestamps. Replacement creates fresh item IDs.
3. Load the claim with Contract and Items, authorize management, and compare expectedVersion before changing anything.
4. Validate the entire replacement and Pending state. Build new item objects in memory only after validation succeeds.
5. Remove old item entities explicitly or verify configured orphan deletion, then attach replacement items to the same claim.
6. Update the optional normalized description and explicitly mark the claim Version modified even if its text did not change.
7. Save once. Existing approval/rejection paths already modify the versioned claim; verify they still do and allow the concurrency handler to reject a losing edit.
8. Return a freshly projected total with all Items loaded. Update response constructors and add edit-versus-approve persistence tests.
9. Document full replacement semantics so callers do not mistake the body for a partial item patch.

**Test recipe.** Replace items 10.00 + 20.00 with 15.00 + 25.00: total becomes 40.00 and old rows disappear. Submit one invalid URL among valid items: old rows remain. Race an item-only edit against approval and assert one writer loses, with no partial replacements. Test 0/1/50/51 items and foreign ownership.

**Explain before marking done.** Why can editing only child rows evade parent concurrency unless you touch the parent? Why is replacement easier to reason about than mixing additions, edits, and deletions in this first slice?

## MS-20: Withdraw a pending expense claim

**User story.** As a client administrator, I want to withdraw a pending expense claim so that an accidental submission is retained for reference but cannot be approved or reimbursed.

**Current gap and learning.** ExpenseClaim has no cancellation state. This story extends the lifecycle deliberately and checks all consumers of its enum, including filters and payroll selection.

**Read first.** [ExpenseClaim](../src/Atlas.Domain/Entities/ExpenseClaim.cs), [ExpenseEndpoints](../src/Atlas.Api/Endpoints/ExpenseEndpoints.cs), and expense selection in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs). **Dependency:** MS-19 for the Version response contract and edit behavior. No schema change: Status is already stored as a string and existing columns hold the decision timestamp.

**Feature contract.** Add `Cancelled = 4` without renumbering existing enum members, plus POST `/api/expense-claims/{id}/withdraw` accepting expectedVersion. Only Pending can become Cancelled. Set DecidedAtUtc from the injected clock; preserve description, items, SubmittedAtUtc, and any existing fields not involved in withdrawal. Do not set reimbursement fields. A repeated withdrawal returns 409. No reopening or delete operation in this slice.

**Acceptance criteria.** A withdrawn claim remains visible through list/detail and a Cancelled status filter. It cannot be edited, approved, rejected, or selected for reimbursement. Version increases once on a successful transition. Only managers of the visible client and platform admins can withdraw it.

**Implementation plan.**

1. Add the new enum value and update the domain lifecycle comment/diagram with the Pending-to-Cancelled arrow.
2. Add a Pending-only Withdraw method receiving now. Keep state validation in the entity, not only in the route.
3. Add the route, load Contract and Items, authorize, validate expectedVersion, and invoke the domain method.
4. Save once and return the existing response with Cancelled status and unchanged item amounts.
5. Search all ExpenseClaimStatus references. Extend any exhaustive display/filter handling; preserve PayrollService's explicit Approved-only reimbursement selection.
6. Verify MS-19's edit rule rejects Cancelled by requiring Pending, rather than trying to enumerate every disallowed state.
7. Add a lifecycle matrix row for every attempted transition out of Cancelled and an integration test that runs payroll with a withdrawn claim present.
8. Test withdrawal racing approval and document the new status in the API reference and lifecycle teaching material.

**Test recipe.** Withdraw a pending 75.00 claim: it remains queryable, has the same items, and contributes zero reimbursement to the next payroll run. Withdraw an Approved claim: 409 with unchanged state. Race two writers sharing a version: one success at most. Confirm no previous enum numeric values changed.

**Explain before marking done.** Why is an explicit Cancelled state better than deleting the record? Why should allowed transitions be written as positive rules such as “only Pending”?

## MS-21: Monthly client expense summary

**User story.** As a client administrator, I want a monthly summary of approved and reimbursed expense items so that I can understand spending by the month in which costs were incurred.

**Current gap and learning.** Claim totals exist, but a claim can contain items from multiple months. This feature teaches choosing the correct aggregation unit and naming whether a report represents current state or historical state.

**Read first.** [ExpenseClaim](../src/Atlas.Domain/Entities/ExpenseClaim.cs), [ExpenseEndpoints](../src/Atlas.Api/Endpoints/ExpenseEndpoints.cs), and decimal grouping patterns in [ReportService](../src/Atlas.Api/Reporting/ReportService.cs).

**Feature contract.** Add GET `/api/clients/{clientId}/expense-summary?fromPeriod=2026-01&toPeriod=2026-12`, maximum 12 inclusive months. Group by item IncurredDate month and claim currency. Return separate approvedAmount and reimbursedAmount, itemCount, and distinctClaimCount per group. Include only claims currently Approved or Reimbursed; this is a current-status spending view, not “what was approved at month-end.” Return only groups with matching items. No schema change or dependency on MS-19/MS-20.

**Acceptance criteria.** Each item contributes once to its own month. A multi-month claim is not charged in full to every month. Pending, Rejected, and Cancelled if present are excluded. Currencies stay separate. A claim spanning two months may count once in each month's distinctClaimCount; that count is not additive across months.

**Implementation plan.**

1. Create an example claim with a January item and a February item, plus separate Approved and Reimbursed claims.
2. Validate periods using a shared helper if MS-07 exists, or extract the relevant parsing rules from report validation without making this story depend on it.
3. Authorize the client, then query ExpenseItems through ExpenseClaim.Contract.ClientId and the allowed claim statuses.
4. Filter IncurredDate before aggregation. Project claim ID, item ID/date/amount, status, and currency.
5. Group the projection by year/month/currency. Use decimal sums. Follow ReportService's projection-then-in-memory-grouping pattern for SQLite decimal calculations; project only the selected client's items in the requested range and document that the date bound does not impose a row-count bound.
6. Compute separate status amounts, item count, and distinct claim count. Sort chronologically then by currency.
7. Return the resolved period range with groups, and avoid a misleading grand total across currencies.
8. Add boundary and authorization tests and document the report's current-status semantics.

**Test recipe.** One Approved claim has January 10.00 and February 20.00; results are 10.00 and 20.00, not 30.00 twice. After reimbursement, those amounts move to reimbursedAmount in the same incurred months. A EUR item never joins a USD group. Foreign-client items with matching dates are excluded.

**Explain before marking done.** Why must the query start from items rather than summing whole claims? Why can a current-status report change after the month has ended?

## MS-22: Benefit selection quote

**User story.** As a client administrator, I want to compare the monthly employer and employee cost of selected benefit plans before enrolling a contract so that I can discuss the proposed package with clear numbers.

**Current gap and learning.** Benefit plans expose costs and enrollment writes create coverage, but there is no contract-aware package preview. This story reuses the existing split formula and makes the estimate's scope explicit.

**Read first.** [BenefitPlan](../src/Atlas.Domain/Entities/BenefitPlan.cs), [BenefitEndpoints](../src/Atlas.Api/Endpoints/BenefitEndpoints.cs), and [BenefitTests](../tests/Atlas.Tests/Domain/BenefitTests.cs).

**Feature contract.** Add POST `/api/contracts/{contractId}/benefit-quote` with 1–20 distinct plan IDs. Require manage permission and an Active contract. Selected plans must exist, be active, and belong to the contract country. Return each plan's ID/name, MonthlyCost, EmployerShare, EmployeeShare, currency, and package totals. This is the selected basket's full monthly cost only: do not automatically add current enrollments, prorate days, or create enrollment rows. No schema change.

**Acceptance criteria.** The two shares for each plan sum exactly to its MonthlyCost using the existing EmployerShare/EmployeeShare properties. Duplicate IDs return 400, nonexistent plans 404, inactive/wrong-country selections 409. Quote reads create no coverage or payroll changes. The response labels prices as current estimates.

**Implementation plan.**

1. Read how BenefitPlan computes the employer rounded amount and employee remainder. Write an odd-cent example by hand.
2. Add the quote DTO and validate collection size and duplicates before loading plans.
3. Load and authorize the contract, require Active state, then fetch all selected plans in one query.
4. Validate membership count, country, and IsActive. Do not return a partial successful basket if one plan is invalid.
5. Map the existing computed shares instead of independently rounding both halves. Use the contract/country currency consistently.
6. Sum per-plan values with decimal arithmetic and return plans sorted by ID for deterministic output.
7. Make the handler read-only and avoid calling the enrollment endpoint or service internally.
8. Add money-invariant, authorization, selection, and no-write tests; document that current enrollments are excluded from the scenario.

**Test recipe.** For MonthlyCost 10.01 and employer rate 0.5, employer share is 5.01 and employee share 5.00 under the existing rounding rule. Add a second plan and verify totals equal the sum of rows. An already enrolled selected plan is still quoted once because this is a basket calculation, not an enrollment eligibility promise.

**Explain before marking done.** Why is one share computed as a remainder? Why must a quote distinguish a selected package from an estimate of the worker's actual next payslip?

## MS-23: Contract benefit calendar

**User story.** As a client administrator, I want to see which benefit enrollments cover each month for a contract so that I can understand why a plan contributes to a payroll period, including after an enrollment has ended.

**Current gap and learning.** Enrollment lists expose dates, but not a month-by-month explanation. This story teaches the difference between current state and historical interval coverage.

**Read first.** [BenefitEnrollment](../src/Atlas.Domain/Entities/BenefitEnrollment.cs), benefit selection in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs), and [BenefitEndpoints](../src/Atlas.Api/Endpoints/BenefitEndpoints.cs).

**Feature contract.** Add GET `/api/contracts/{contractId}/benefit-calendar?fromPeriod=2026-01&toPeriod=2026-12`, maximum 12 inclusive months. Return every requested month, including empty months, with covering enrollments and current plan prices/shares. Use the existing inclusive `CoversMonth` rule: any covered day produces a full monthly premium. Include Ended enrollments for months they cover. Label totals `estimatedEmployerCost` and `estimatedEmployeeCost`; these are not saved historical payslip charges. No schema change or dependency on MS-22.

**Acceptance criteria.** An enrollment ending on March 1 covers March but not April. Inactive plans still appear where existing enrollment dates cover the month. A future enrollment does not appear early. The calendar describes enrollment coverage even if employment dates would separately exclude the contract from payroll; return `contractCoversMonth` to make that distinction visible.

**Implementation plan.**

1. Write a three-month example with one ended enrollment and one future start.
2. Validate the period range and authorize the contract before loading its enrollments with plans.
3. Restrict loaded enrollments to those overlapping the overall window; do not filter on Active status.
4. Iterate at most 12 months and use CoversMonth for membership. Use EmploymentContract.CoversMonth separately for the explanatory contract flag.
5. Project enrollment IDs, plan IDs, original dates/status, and current plan shares. Keep empty months explicit.
6. Sum current estimates per month and preserve the full-month pricing rule. Do not introduce day proration in a display feature.
7. Add tests comparing coverage selection with PayrollService's enrollment selection under unchanged inputs.
8. Document that changing a plan price can alter these estimates while already stored payslips remain unchanged.

**Test recipe.** Coverage January 31–March 1 appears in January, February, and March at a full monthly estimate. April is empty. Setting plan IsActive false does not erase those covered months. A Draft contract shows `contractCoversMonth=false`, while its enrollment data, if present in a fixture, remains explained rather than billed by this endpoint.

**Explain before marking done.** Why would filtering only Active enrollments make a historical calendar wrong? Which endpoint should a user consult for the actual saved charge instead of this estimate?

## MS-24: Termination base final-pay preview

**User story.** As a client administrator, I want to preview prorated base salary and unused annual leave payout for a proposed termination date so that I can understand those components before submitting an offboarding request.

**Current gap and learning.** Termination validation and final-pay calculations exist in separate workflows. You will reuse their rules in a read-only estimate and clearly separate included amounts from other payroll components.

**Read first.** [TerminationEndpoints](../src/Atlas.Api/Endpoints/TerminationEndpoints.cs), [FinalPayCalculator](../src/Atlas.Domain/Services/FinalPayCalculator.cs), and salary/unused-leave selection in [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs).

**Feature contract.** Add POST `/api/contracts/{contractId}/termination-preview` with proposedEndDate. Require manage permission, an Active contract, no pending termination request, and the same minimum-notice/start-date checks as request creation. Return notice date, earliest allowed end, currency, selected monthly salary and effective date, proratedBaseSalary, annualAllowance, reservedAnnualDays, unusedAnnualDays, dailyRate, unusedLeavePayout, and `estimatedBaseTotal`. The total includes only prorated gross base salary plus unused leave payout; it excludes deductions, employer contributions, expenses, benefits, and invoice fees. No schema change.

**Acceptance criteria.** Salary selection calls SalaryRecord.EffectiveForMonth, preserving its month-end cutoff and creation-time tie handling; when it returns null, use the current contract salary and a null selected effective date, matching payroll's fallback. The existing final-month proration rule and daily-rate rounding are preserved. Pending and Approved annual leave both reserve days. If MS-18 exists, use adjusted allowance. Without a leave policy, match current payroll's zero leave payout and expose `leavePolicyConfigured=false`, with allowance/unused-day figures null rather than invented entitlements. Repeated previews leave all rows and versions unchanged.

**Implementation plan.**

1. Trace the relevant PayrollService branch and write down its exact salary cutoff, reservation year, and rounding order.
2. Extract reusable effective-salary and base final-pay assessment logic without making preview call CreateRun or CompleteRun.
3. Reuse termination eligibility checks; inject the same clock into new preview and the request-creation date decision so fixed-clock tests agree.
4. Validate proposedEndDate and arithmetic bounds before calculating the earliest notice date or month end.
5. Load the authorized contract, country, salary records, leave policy, and applicable reservations; do not mark any entity modified.
6. Calculate unused annual days with the existing nonnegative rule and call FinalPayCalculator for daily rate, payout, and salary proration.
7. Return named components and estimate scope. Do not label estimatedBaseTotal as take-home pay or total employer cost.
8. Add tests comparing preview components to a subsequent final payroll run with unchanged relevant inputs; the complete payslip may contain additional components.
9. Document that this is Atlas's existing model, not a new jurisdiction-specific payroll calculation.

**Test recipe.** Monthly salary 3000.00 with a valid September 15 end date gives prorated base 1500.00 under the existing rule. Daily rate is 138.46; two unused days yield 276.92 and base total 1776.92. Choose a fixed clock early enough to satisfy notice. Add a salary increase effective after September and prove it is excluded; test missing policy and no unused days.

**Explain before marking done.** Why can estimatedBaseTotal differ from both NetPay and TotalCost? Why must the preview use salary history rather than just Contract.MonthlySalary?

## MS-25: Amendment comparison preview

**User story.** As a client administrator, I want a before-and-after comparison of proposed contract terms so that I can check a salary or title change before creating an amendment request.

**Current gap and learning.** Amendment creation stores proposed fields, while approval changes current contract terms and appends salary history. This feature teaches the difference between a comparison, a proposal, and an approved effective record.

**Read first.** [AmendmentEndpoints](../src/Atlas.Api/Endpoints/AmendmentEndpoints.cs), [EmploymentContract](../src/Atlas.Domain/Entities/EmploymentContract.cs), and [SalaryRecord](../src/Atlas.Domain/Entities/SalaryRecord.cs).

**Feature contract.** Add POST `/api/contracts/{contractId}/amendment-preview` with optional newMonthlySalary/newJobTitle and required effectiveDate. Require manage permission, Active state, at least one supplied change, positive supplied salary, and effectiveDate not before contract start. Use the same pending-amendment restriction as creation. Return current contract salary/title, proposed resulting salary/title, monthly salary delta, percentage delta rounded to two decimals, currency, effective date, and contract Version. An omitted field carries forward its current value. This compares the current Contract fields; it is not a reconstruction of terms effective on the requested date. No schema change.

**Acceptance criteria.** A title-only proposal has zero salary delta. Salary 4000 to 4400 yields +400 and +10 percent. A quote creates neither ContractAmendment nor SalaryRecord and does not mutate Contract. Approval through the existing workflow remains the only step that applies the proposed terms. A nonblank supplied title is trimmed and limited to the existing 200-character model limit.

**Implementation plan.**

1. Trace create and approve separately; write down which of them writes SalaryRecords and when Contract.MonthlySalary changes.
2. Extract shared input/state assessment used by creation and preview. Keep preview-specific response formatting outside the domain entity.
3. Apply the explicit title bound to both routes when sharing validation, and test/document that validation addition.
4. Load and authorize the contract, check pending amendments, and calculate resulting terms in local values without calling ApplyAmendment on the tracked entity.
5. Compute salary delta with decimal and percentage as `delta / currentSalary * 100`, using two-decimal away-from-zero rounding. Current valid salaries are positive; handle invalid legacy zero salary as a 409 data-state error rather than divide by zero.
6. Return current Version and the comparison. An equal supplied value is allowed if creation currently permits it; do not secretly redefine “change” only in preview.
7. Add no-write checks and examples for salary-only, title-only, and combined proposals.
8. Test a preview followed by a competing amendment; creation must recheck its own state and pending-request rules.

**Test recipe.** Salary-only input preserves the title; title-only input preserves salary and yields zero percent. A whitespace-only title with no salary is invalid. A future-effective already approved salary record may already be reflected in Contract fields: the preview labels those fields as current terms and does not present them as historical payroll truth.

**Explain before marking done.** Why must you avoid temporarily calling ApplyAmendment and trying to undo it afterward? How do current contract terms differ from the SalaryRecord selected by payroll for a month?

## MS-26: API key expiration

**User story.** As a platform administrator, I want to create an API key with an expiration time so that temporary access ends automatically without requiring a manual deactivation request.

**Current gap and learning.** Authentication currently checks only whether an API-user row exists and is active. This story adds a time-dependent credential rule and teaches fixed-clock testing at an exact boundary.

**Read first.** [ApiUser](../src/Atlas.Domain/Entities/ApiUser.cs), [ApiUserEndpoints](../src/Atlas.Api/Endpoints/ApiUserEndpoints.cs), [ApiKeyAuthenticationHandler](../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs), and [AuthorizationTests](../tests/Atlas.Tests/Integration/AuthorizationTests.cs).

**Feature contract.** Extend API-user creation with optional expiresAtUtc. Null preserves nonexpiring behavior. A supplied expiration must be strictly after now and no more than 365 days later. Authentication succeeds only when IsActive and either expiration is null or now is strictly before it. Expose expiration in created and masked list responses; continue returning the full key only on creation. **Schema:** nullable ExpiresAtUtc column. Existing rows remain null. No automatic deletion or background worker.

**Acceptance criteria.** The exact expiration instant is rejected with 401 on a protected endpoint. Expiration does not mutate IsActive or delete the user. Deactivated keys remain invalid even before their expiration. Existing nonexpiring seeded keys continue to authenticate. Logs and error responses contain no full API key.

**Implementation plan.**

1. Add the nullable column and follow the schema recipe, including a persistence test for UTC conversion.
2. Extend create and response DTOs; update all constructor usages without removing existing fields.
3. Inject the application TimeProvider into creation and authentication, storing it under a distinct field such as `_clock` to avoid confusion with framework members.
4. Validate creation against one captured now value and save the supplied timestamp normalized to UTC.
5. Extend the handler's existing invalid-key branch with the expiration condition. Keep failure wording generic and do not include the submitted secret.
6. Leave deactivation and role/client claim construction unchanged in behavior. Do not run a save during authentication.
7. Use a replaceable fixed clock in AtlasApiFactory-based tests; advance it explicitly rather than sleeping.
8. Check masked-list serialization and document null, exact-boundary, and maximum-lifetime behavior.

**Test recipe.** A key expiring at September 10 12:00:00Z succeeds just before that instant and fails at it and afterward. Null succeeds while active. Expiration equal to creation now or beyond 365 days returns 400. Use two offset representations of the same instant and verify identical behavior. Assert the full secret never appears in the list response.

**Explain before marking done.** Why is `now < expiresAt` different from `now <= expiresAt`? Why does expiration belong in authentication even when administrators can also view it in a list?

## MS-27: Replace an API key immediately

**User story.** As a platform administrator, I want to replace an active API key in one operation so that I can retire the old credential while giving its replacement exactly the same access scope.

**Current gap and learning.** Separate create/deactivate requests leave a window where both keys work or neither works. This story teaches atomic replacement and honest handling of one-time secrets when a response is lost.

**Read first.** Your MS-26 changes, [ApiUserEndpoints](../src/Atlas.Api/Endpoints/ApiUserEndpoints.cs), and DbContext concurrency configuration. **Dependency:** MS-26. **Schema:** ApiUser implements IVersioned; add Version and nullable ReplacesApiUserId with a unique nonnull relationship to the replaced row and restricted deletion.

**Feature contract.** Add platform-only POST `/api/api-users/{id}/replace` with expectedVersion. Require the old row to be active and not expired. Create a new API-user row with a fresh key, the same name/role/client scope, inherited expiration, and ReplacesApiUserId; deactivate the old row atomically. Return 201 with the new full key once. A repeat or competing replacement returns 409 with replacement ID when available, never the replacement secret. Add Version/replacement metadata to masked responses. No grace period, role change, or expiration extension.

**Acceptance criteria.** After commit, the old key fails on the next request and the new key has exactly the inherited access. A request already authenticated before commit is not forcibly cancelled. Only one replacement row can reference an old user. Existing deactivation participates in the old row's concurrency token. Failed replacement saves leave neither a partial new row nor a silently deactivated original.

**Implementation plan.**

1. Add versioning and replacement relationship mapping. Verify uniqueness permits many rows with null ReplacesApiUserId while rejecting duplicate nonnull IDs.
2. Extend masked response mapping; retain the existing one-time-secret response style for creation/replacement.
3. Authorize platform access, load the old row, compare expectedVersion, and check active/expiry state using one clock value.
4. Generate a fresh credential using the existing generation approach; copy scope fields from the loaded row, never from request data.
5. Set old IsActive false, add the new row, and save once. Let old-row versioning and replacement uniqueness reject competing writes.
6. Translate recognized replacement conflicts into metadata-only 409 responses after a fresh lookup. Do not recover by returning a stored full key.
7. Verify existing deactivate writes use the now-versioned entity and test replacement-versus-deactivation.
8. Document lost-response recovery: locate the new row through the masked list, then deactivate/replace it through a separate still-valid platform key. A self-replacing sole admin who loses the response needs operator recovery; provide this limitation explicitly rather than promising replay recovery.
9. Test old/new authentication and exact role/client inheritance, including MS-26 expiration.

**Test recipe.** Replace a client-viewer key: the new key still cannot create a contract or read another client. Two replacements from the same old version create at most one successor. An inherited expiry does not move forward. A second replacement request never reveals the first successor's secret.

**Explain before marking done.** Why can an idempotent metadata response and a one-time secret have different retry policies? What exactly does “immediate” mean for requests already in progress?

## MS-28: Saved report presets

**User story.** As a platform administrator, I want to save a named report configuration and run it again so that I do not repeatedly reconstruct the same report filters.

**Current gap and learning.** Report endpoints already validate inputs, but their parameters are not reusable saved objects. This story introduces typed persisted configuration without accepting arbitrary queries or bypassing report authorization.

**Read first.** [ReportEndpoints](../src/Atlas.Api/Endpoints/ReportEndpoints.cs), [ReportQueryValidation](../src/Atlas.Api/Reporting/ReportQueryValidation.cs), [ReportService](../src/Atlas.Api/Reporting/ReportService.cs), and the authentication NameIdentifier claim.

**Feature contract.** Add platform-only `/api/report-presets` collection GET/POST and `/{id}` GET/PUT/DELETE, plus POST `/{id}/run`. Every preset belongs to the current API-user ID; even another platform admin gets 404 for someone else's preset. Name is trimmed 1–100 characters and unique per owner ignoring case through a stored normalized name. Kind is exactly one of headcount, payroll-costs, compliance-expiries, invoice-aging. Filters are typed per kind and serialized to JSON at most 4000 characters. PUT requires expectedVersion; DELETE takes expectedVersion as a query parameter. **Schema:** versioned ReportPreset table with owner foreign key and owner/normalized-name uniqueness.

**Acceptance criteria.** Save and run both validate through the same rules as direct reports. Unknown filter fields and unknown kinds return 400. A null asOf is resolved using the clock when run, while an explicit date remains fixed. Running changes no preset version. No SQL, arbitrary route URL, or caller-supplied owner is stored or executed. Existing reports remain platform-only.

**Implementation plan.**

1. Write four allowed filter DTOs from the actual report signatures: headcount asOf; payroll-costs country/client/fromYear/fromMonth/toYear/toMonth; compliance withinDays/asOf; aging clientId/asOf.
2. Extract reusable parsing/validation where endpoint handlers currently contain it. Keep actual ReportService calls as the only execution targets.
3. Add entity/mapping and normalize names consistently, for example trim then invariant uppercase. Use database uniqueness for competing creates.
4. Implement owner-scoped paged list/detail/create. Validate typed filters before serializing them; reject unexpected fields rather than silently dropping them.
5. Implement versioned replacement PUT and DELETE. On DELETE, set/check the original concurrency version so a concurrent PUT cannot be silently deleted.
6. Implement run by loading the owner's preset, deserializing the allowed type, validating again, resolving clock defaults, and dispatching through an explicit kind switch.
7. Return the report kind, resolved parameters, and report result. Do not call an arbitrary URL or execute JSON as a query language.
8. Add ownership, invalid stored-filter, concurrency, and direct-versus-saved result tests.
9. Document ownership by API-user identity: MS-27 creates a new identity, so presets do not automatically follow a replacement key in this bounded slice.

**Test recipe.** Save a headcount preset with null asOf, move the fixed clock one day, and verify the next run resolves the new date. An explicit date remains unchanged. Names ` Monthly ` and `monthly` conflict for one owner but may exist for different owners. A client-admin key cannot access any preset route, and a second platform key cannot read the first one's preset.

**Explain before marking done.** Why revalidate stored filters at execution? Why is a platform role different from ownership of one administrator's saved configuration?

## MS-29: Client approval work queue

**User story.** As a client administrator, I want one queue of pending leave, expense, amendment, and termination requests so that I can choose my next review without opening four lists.

**Current gap and learning.** Each workflow has a pending list, but no combined entry point. This feature teaches merging bounded queries with stable identities while retaining the original decision endpoints as the authority.

**Read first.** [LeaveEndpoints](../src/Atlas.Api/Endpoints/LeaveEndpoints.cs), [ExpenseEndpoints](../src/Atlas.Api/Endpoints/ExpenseEndpoints.cs), [AmendmentEndpoints](../src/Atlas.Api/Endpoints/AmendmentEndpoints.cs), and [TerminationEndpoints](../src/Atlas.Api/Endpoints/TerminationEndpoints.cs).

**Feature contract.** Add GET `/api/clients/{clientId}/approval-queue?limit=25`, with limit 1–100. Return pending counts per kind, totalPending, `hasMore`, and the newest limited items sorted by submitted timestamp descending, then kind, then source ID. Use RequestedAtUtc for leave/amendment/termination and SubmittedAtUtc for expenses. Items contain kind, source ID, contract/worker IDs, timestamp, a short typed summary, and a local detail path to the existing endpoint. No approval/rejection action is added here. No schema change.

**Acceptance criteria.** Client readers may see their queue; only existing workflow permissions determine whether they can decide an item. Every source query is scoped by Contract.ClientId before counts and limits. Kinds and source IDs together form the item identity. An item decided after the queue was fetched is rejected appropriately by the existing action if acted on again; the queue is a refreshable view, not a reservation.

**Implementation plan.**

1. Define a typed queue item with separate summaries: leave dates/type, expense total/currency, proposed amendment fields, and proposed termination end date. Exclude free-text sensitive reasons from the overview.
2. Authorize the client and validate limit before issuing source queries.
3. For each kind, filter Pending and client scope, calculate its count, and project at most limit+1 newest rows in stable order.
4. Run EF queries sequentially on the request's DbContext; it does not support concurrent operations. Do not use Task.WhenAll over a single context.
5. Merge the four small projections, apply the global timestamp/kind/ID ordering, and take limit. Compute hasMore from the combined counts.
6. Build detail paths from a fixed mapping of kinds to existing routes; never store or accept caller-controlled URLs.
7. For internally consistent counts/items, execute the read set within one explicit read transaction appropriate to the current SQLite provider. Dispose it before returning; no rows or versions change.
8. Add tied-timestamp, per-source overflow, authorization, and post-fetch-decision tests. Document refresh behavior and the absence of paging in this first queue slice.

**Test recipe.** Seed two pending items of each kind with interleaved timestamps and request limit 3: counts total eight, exactly the newest three are returned, and hasMore is true. Fill one kind with more than limit items to prove per-source limiting still produces the correct global top items. A foreign client's newer requests never affect the queue or counts.

**Explain before marking done.** Why is taking limit+1 from each source enough for a global newest-limit merge? Why must the original decision endpoint recheck state even when the queue says Pending?

## MS-30: Pause and resume country hiring

**User story.** As a platform administrator, I want to pause new hiring in a country so that new contracts and draft activations stop while existing employment and payroll continue operating.

**Current gap and learning.** Country.IsActive already exists and contract creation checks it, but there is no availability update route and activation does not currently enforce a pause. This story turns an existing flag into a coherent workflow rule across multiple writers.

**Read first.** [Country](../src/Atlas.Domain/Entities/Country.cs), [CountryEndpoints](../src/Atlas.Api/Endpoints/CountryEndpoints.cs), creation/activation in [ContractEndpoints](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), and [PayrollService](../src/Atlas.Infrastructure/PayrollService.cs).

**Feature contract.** Add platform-only PUT `/api/countries/{code}/hiring-availability` with isActive and expectedVersion. Return the updated country including Version. A no-change request with a current version returns 200 without a write; stale versions return 409. Paused countries reject new contracts and activation of existing Draft contracts with 409. Existing Active contracts, payroll, leave, expenses, and reads continue under their existing rules. **Schema:** add Country.Version and implement IVersioned. No automatic termination, deletion, or background job.

**Acceptance criteria.** Resuming permits creation and activation again, subject to their other rules. Pausing does not alter any contract state. A competing pause and creation/activation cannot both save based on an unchanged active-country assumption; after a conflict, the caller can reload and retry against the committed state. If MS-03 exists, readiness reports country-paused through the shared activation assessment.

**Implementation plan.**

1. Add country versioning and extend every CountryResponse constructor. Follow the schema recipe.
2. Implement the platform availability route with code normalization, expectedVersion validation, and no-op handling after the version check.
3. Audit contract creation's IsActive check and ensure it loads a tracked Country before checking availability. Its current paused-country response is a 400 validation error; deliberately change this specific business-state response to the story's 409 contract, updating existing assertions and API documentation.
4. Add the same availability rule to activation, after authorization and alongside its existing state/checklist assessment.
5. For both successful contract creation and activation, explicitly touch the loaded Country.Version and save it with the contract/checklist changes atomically. A read-only IsActive check alone does not coordinate with pause.
6. Keep payroll selection based on supported country and covering contracts, without adding an IsActive rejection. The flag is hiring availability, not a ban on servicing existing workers.
7. Add MS-03 blocker integration only if that story is present; otherwise keep a reusable activation rule ready for it.
8. Write two-context pause-versus-create and pause-versus-activate tests. Verify failed creation leaves no checklist or initial SalaryRecord orphaned.
9. Test resume and unchanged-state updates; document that country Version may increase from coordinated hiring writes even when IsActive stays true.

**Test recipe.** Pause a country with an Active contract and a ready Draft. New creation and Draft activation fail; payroll for the Active contract still succeeds. Resume and activate the Draft successfully. In a race where creation commits first, pause using the old version conflicts; after reloading, the administrator can pause while the already created Draft remains subject to the activation pause rule.

**Explain before marking done.** Why is checking IsActive in two handlers insufficient to make a concurrent pause reliable? Why does a hiring pause leave payroll running?

## Review your work as a mid-level engineer

Use this checklist after implementing any one story, not as a requirement to implement the entire catalog:

- [ ] I can demonstrate the user-visible capability with a concrete request and response.
- [ ] I can point to the authorization boundary before data is paged, aggregated, or changed.
- [ ] I know which values are current configuration, saved snapshots, or estimates.
- [ ] I tested a meaningful rejection and, for a write, checked the persisted result after failure.
- [ ] I used a shared rule where another endpoint promises the same behavior.
- [ ] I covered the stated currency, date, repeat-request, and concurrency cases that apply to this story.
- [ ] My documentation describes the implemented slice accurately, including any fresh-database-only validation.
- [ ] My PR contains only the chosen story and the supporting changes it needs.

For a short teach-back, complete these sentences aloud: “The caller wants __. The request enters __. We check __ before reading or changing __. We save __ together because __. The test that best proves it works is __.” Then explain it again without file names. Being able to move between the user story and the code path is the skill this workbook is intended to build.
