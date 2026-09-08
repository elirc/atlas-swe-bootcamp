# Atlas implementation journal

This is a record of implementation and learning, not a list of intended successes. Consult [progress](progress.md) for the status of each story.

## Entry 001: Establish the starting point

**User objective:** implement all 75 story plans while making the upskilling bootcamp the primary deliverable.

**Scope:** 25 junior stories, 30 mid-level stories, and 20 mid/senior stories. The source already includes earlier report, diagnostics, and learning improvements; those changes remain in place.

**What we inspected:** the three workbooks, endpoint handlers, PayrollService, AtlasDbContext, authentication, and the integration-test factory. The initial Release suite passed all 474 existing tests. New story tests are verified separately; that existing-suite result does not prove the new features.

**First lesson:** authorization, filtering, and paging are separate steps. An authenticated caller may still be forbidden from seeing another client's records. A filter narrows the records already allowed by ownership. Paging happens last so both returned rows and counts describe the same selection.

**Say it another way:** first choose the company's filing cabinet, then choose matching folders, then take the requested page. Taking a page before choosing the company can leak records or produce misleading totals.

**Next work:** implement the junior boundary/filter stories with focused behavior tests and a lesson that traces their queries. Every later feature will reuse these habits.

## Entry 002: Junior boundaries and views

**Implementation:** the junior handlers now include trimmed name limits, ASCII currency checks, defined enum validation, a clock-based birth-date guard, optional filters, deterministic name ties, and the nested document detail route. Report filters enter before grouping. The dedicated JuniorStoryTests cover these promises using a fresh fixture and fixed clock; verification is in progress.

**Design decision:** ApiInput contains small repeated transport rules. It does not load entities or decide ownership. Keeping that boundary visible makes it easier to reuse validation without accidentally skipping authorization.

**Review finding:** normalizing a Unicode character before checking ASCII shape can accept input outside the promised character set. The implementation checks original trimmed characters and only then uses the uppercase normalized value. The tests include a long-s example alongside ordinary accented text.

**Learning artifact:** [Lesson 1](01-http-boundaries-and-query-composition.md) explains the same query through a user request, a four-row example, code navigation, and practice tasks. Its exercises ask you to predict errors and temporarily remove a guard on your own branch to see which test catches the defect.

**Remaining:** finish focused verification, correct any failures, update per-story evidence, and continue into the dependent feature workflows. No story is marked verified merely because its handler has been edited.

## Entry 003: A test catches normalization at the wrong boundary

**Observed result:** the first focused run passed 55 tests and failed the long-s currency case: the tested build accepted a non-ASCII character after normalization. The source now checks original trimmed characters before normalizing. A fresh build is running to verify the corrected implementation together with the first mid-level views.

**What this teaches:** adding an edge-case test is useful even when the implementation looks simple. Keep the failing example, change the actual rule, and rerun it. Do not remove the example or weaken the expected result just to obtain a green suite.

**Current implementation:** twelve mid-level read features have handlers and tests: workforce summary/timeline/readiness, payslip browsing/export, invoice breakdown/export, leave calendar, expense summary, benefit quote/calendar, and approval queue. Their verification is still pending.

**Learning artifact:** [Lesson 2](02-views-snapshots-and-estimates.md) distinguishes stored financial facts from live views and estimates. It uses a changed salary, an odd-cent benefit split, a cross-month expense claim, and a clipped leave interval to repeat that distinction in different ways.

## Entry 004: First feature tranche verified

**Observed result:** the fresh focused run passed all 66 tests across JuniorStoryTests and MidlevelViewStoryTests, with zero failures. The currency long-s regression is now green. These tests cover the 25 junior stories and twelve mid-level read stories. This is focused evidence, not yet a final regression run for all 75 stories.

**Current work:** batch onboarding, notes, country templates, hiring pauses, expiring/replacement credentials, invoice payments/reversals/credits, billing schedules, and leave changes. These write workflows require additional tests before verification.

**Integration decision:** the payment and credit workbooks were originally independent. With both enabled, credits reduce the balance available to payments, and payments reduce the amount that can be credited without an overpayment. This implementation rejects negative outstanding balances; it does not invent a cash-refund workflow. The separate credited-aging report describes net billed amounts, while the payment-aware invoice-aging report describes outstanding amounts.

**Surprising edge:** a payment dated before an earlier payment's reversal may overdraw historical balance even when today's balance can cover it. Checking every effective instant catches that. For example, invoice 100, payment 100 on day 1, reversal on day 10: another payment of 100 dated day 1 would make the day-1 balance negative. A payment dated day 10 can use the released balance.

**Schema note:** the learning database uses EnsureCreated, which creates a fresh schema and does not upgrade an existing database. The new columns and tables require a fresh disposable database for these lessons. Keep any existing database; select a new connection-string file rather than deleting data. A production migration path is separate work and must not be implied by a passing fresh-schema test.

## Entry 005: Shared assessments and reviewed amounts

**Observed result:** the first expanded workflow build succeeded with zero warnings and zero errors. Its focused workflow and regression tests are running; there is no passing test result to record yet for that tranche. Documentation link validation passed across 61 Markdown files before Lesson 5 was added.

**Implementation:** the remaining mid-level write features now include expense replacement/withdrawal, contract previews, and typed saved-report presets. The two-approver expense feature is integrated with edits: a pre-vote edit re-evaluates policy; any existing vote prevents an edit. A first vote leaves a high-value claim Pending and excluded from payroll.

**Design decision:** final-pay preview and payroll call the same base-final-pay calculation. Amendment preview deliberately compares current contract fields instead; its question is different from a historical payroll-period calculation. Both proposal creation routes reuse the same eligibility checks as their previews.

**Learning artifacts:** Lessons 3–5 repeat the features through request traces, tables, race schedules, source reading paths, and practice questions. Their tests check persisted results after failures, not only response codes.

**Verification discipline:** a new build is being prepared for the proposal and expense changes. The earlier passing 66-test tranche remains useful evidence, but it does not automatically verify later changes to shared services.

## Entry 006: Replacing children requires explicit inserts

**Observed failure:** the workflow test replacing a configured onboarding template expected 200 but received 409. The first configuration and contract copy succeeded; replacing the existing header's children failed.

**Diagnosis and change:** replacement children receive fresh GUIDs in memory. When EF discovers those objects through an existing tracked parent, their nondefault generated keys can cause it to infer updates of existing rows. Those rows do not exist, so the update becomes a concurrency failure. The replacement now explicitly calls AddRange for the new rows. The expense replacement path uses the same explicit insert pattern. Verification of this correction is pending.

**What to inspect in a debugger:** look at each new child's EntityState before saving. It must be Added, the old children Deleted, and the versioned parent Modified. A correct-looking object graph is not enough; the change tracker also needs the correct persistence intent.

**Completed run:** the workflow/regression tranche finished with 58 passing tests and one failure, the MS-06 replacement case above. The other story tests in that run passed. The register records those results individually; it does not label the overall run green. A later build will verify the explicit-insert correction and the newer proposal/expense changes.

## Entry 007: Build success is not test execution

**Observed result:** the next solution build succeeded with zero warnings and zero errors. The subsequent focused test invocation failed during assembly loading: Windows Application Control blocked Atlas.Tests.dll with error 0x800711C7. xUnit could not discover or execute the tests. Its later “no test matches” message is a consequence of that load failure, not evidence that the requested tests do not exist.

**Status:** no feature is promoted using this blocked run. The template correction and newer proposal/expense tests remain pending verification. Read-only operating-system diagnostics are being checked; this task does not change the machine's security policy.

**Learning point:** distinguish compiler success, test discovery, test execution, and assertion results. They are different stages. Record the first actual failure so that a secondary message does not send the investigation in the wrong direction.

## Entry 008: Final stories implemented, verification still distinct

The compliance/configuration solution build passed with zero warnings and errors. The later focused test run discovered 27 tests but all failed while loading Atlas.Domain.dll because Windows Application Control blocked the assembly (0x800711C7). These are environment failures before business assertions, not 27 demonstrated feature defects. No verification credit is assigned from that run.

All 75 stories now have implementation code. The final seven add cent allocation and historical reconciliation, staged worker imports, atomic contract decision evidence, a personal notification inbox, bounded expiring data exports, and frozen workforce comparisons. New tests exercise rollback, ownership, stale leases, parser behavior, and saved values. A fresh build and final regression remain necessary.

Review correction: the cost-center report now follows the workbook’s four year/month query parameters and exposes an explicit legacy not-captured bucket as well as reconciliation totals. Returning only a legacy total would have made the missing allocation evidence less visible than the requested report.

Learning artifacts: Lessons 7–9 explain the final features through small examples, source traces, equations, tables, and retrieval practice. New records are still introduced through the repository’s fresh-schema EnsureCreated lane; existing databases are not silently upgraded.

## Entry 009: A broad regression run finds a shared-input incompatibility

The complete implementation built with zero warnings and errors. The next full test run moved past the previous Application Control blockage and began executing behavioral tests. It found Worker creation rejecting configured two-character country identifiers used by existing payroll fixtures. Country creation already accepts those identifiers; the newly extracted worker helper had accidentally narrowed that contract to ASCII letters.

The helper now preserves the configured two-character identifier contract while retaining name/email bounds, normalization, and birth-date validation. Simulation/job period input follows the same catalog-compatible format. A focused integration test covers single creation, CSV validation, and simulation with a configured A1 identifier. Existing payroll regression fixtures remain unchanged. A separate leave regression found a less-specific error message; the shared assessment now distinguishes starting before and ending after the employment interval while retaining the outside-contract blocker code. Verification of these corrections is pending.

Debugging lesson: a later JSON conversion error was secondary. A test helper tried to deserialize an earlier 400 ProblemDetails response as a success DTO. Checking the HTTP status at each setup step reveals the first rejected request and avoids investigating the wrong serializer.

The learning launcher now supports serve, named practice databases, fresh isolated sessions, and a dry-run command. Python compilation, launcher help/dry-run, 44-question quiz output, and local Markdown links were checked successfully.

## Entry 010: A read snapshot must leave room for lease renewal

Design review identified a SQLite transaction-mode issue in the background read phase. A default immediate transaction can reserve the writer lock even when its application code only reads. That can obstruct lease renewal on the separate connection. The read phase now explicitly uses a deferred transaction through SqliteReadSnapshot; the final fenced write remains a separate write transaction. The installed Microsoft.Data.Sqlite 10.0.10 API documentation confirms the deferred BeginTransaction overload.

The new file-backed WAL test holds a reader open, renews the job from another connection, proves the original reader retains its version, and then proves a fresh reader sees the renewal. This tests both consistency and the ability to make progress. It has been added for the next build; no passing result is claimed yet. Claim timestamps are captured after selecting the job so a slow initial read does not consume the newly granted lease before it is assigned.

## Entry 011: Full-run evidence and the focused correction pass

The full solution test run completed with 586 tests: 569 passed, 17 failed, none skipped. All 112 tests in the eight story-focused suites passed, including template replacement, compliance configuration, allocation, imports, audit, notification, and export scenarios. The failures were nine PayrollEdgeCaseTests, seven PayrollEndpointTests, and one BoundaryValidationTests case. They match the country-identifier compatibility and leave-message corrections recorded above.

The original report is retained at artifacts/bootcamp-tests/bootcamp-final.trx. Its name does not make it a green result: this is the pre-correction full run. The next build and focused correction pass include the new deferred-reader test, export size caps and isolation fixture, country compatibility test, and exact-cent precision guard. Their outcome must be recorded separately. No security policy was changed to obtain this executing run.

## Entry 012: Corrections verified and the learning path completed

**Build:** a newly added compatibility fixture initially omitted Country's required EmployerCostRate property, producing compiler error CS9035. The fixture now explicitly supplies zero employer and employee rates for its synthetic country. The final Release solution build passed with zero warnings and zero errors. This was a fixture construction error, distinct from the earlier runtime validation regressions.

**Behavior tests:** the focused correction run completed with 101 tests passed, zero failed, and zero skipped. It includes PayrollEndpointTests, PayrollEdgeCaseTests, BoundaryValidationTests, ClientAndWorkerEndpointTests, LeaveEndpointTests, AdvancedStoryTests, ExportStoryTests, and PayrollReliabilityStoryTests. A comparison of exact test names in the two TRX reports confirms that all 17 failures from the earlier full run now pass. The new deferred-reader, country compatibility, export row-limit, and export byte-limit cases pass; expanded allocation precision and export isolation assertions pass too.

**Evidence limits:** the earlier full run remains 569 passed and 17 failed out of 586. The later result is a passing 101-test correction run, not a newly executed full suite. Their tests overlap. The story register now marks all 75 stories verified using the relevant passing suites, with the run scope explained in the [verification record](verification.md).

**Real startup check:** a separate Development API process created a fresh uniquely named database, seeded sample data, and passed readiness. HTTP checks found one USD country, two clients for the platform administrator, and only Acme for the Acme viewer. A PH payroll simulation returned its assessment while the saved-run count stayed zero before and after. The process was stopped after the check, and its practice database was retained. This complements integration tests by exercising the real Development startup and seeder.

**Learning deliverables:** the bootcamp has nine source-linked lessons, 18 API labs, 45 question/answer prompts, a 24-session practice path, four visual models, a first-session PowerShell walkthrough, an individual study-journal template, a feature route map, and this implementation journal. The lessons explain the same mechanisms through plain-language examples, request traces, tables, calculations, failure timelines, and recall exercises. The learner's first task is to predict and explain a small request before attempting a larger change.

**How to continue:** begin with the [first-session walkthrough](first-session.md), then choose one story with `python scripts/bootcamp.py show JS-09`. Use `test JS-09` to run its registered suite and record your own prediction and observation. `serve --new` creates another practice database; a named database resumes a prior session without deleting it. Existing schemas are not migrated by EnsureCreated.
