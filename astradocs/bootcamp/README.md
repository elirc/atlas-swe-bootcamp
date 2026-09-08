# Build Atlas, learn the engineering

This bootcamp accompanies implementation of all 75 stories in the three AstraDocs workbooks. The learning material is the main deliverable: working features give us real examples to read, test, explain, and change.

Start with the [first-session walkthrough](first-session.md) for copyable PowerShell requests, then read [progress](progress.md), the [verification record](verification.md), and the [engineering journal](journal.md). The original [junior](../16-junior-user-stories.md), [mid-level](../17-midlevel-feature-stories.md), and [mid/senior](../18-mid-senior-feature-stories.md) workbooks remain the requirements. Their original planning notices describe when those documents were written; the progress register is the source of truth for implementation status.

## Lessons available now

- [Lesson 1: HTTP boundaries and query composition](01-http-boundaries-and-query-composition.md) follows the junior implementation through authorization, validation, paging, time, and report aggregation.
- [Lesson 2: Views, snapshots, and estimates](02-views-snapshots-and-estimates.md) traces the mid-level read features through financial history, CSV, intervals, and aggregation.
- [Lesson 3: Atomic workflows and concurrency](03-atomic-workflows-and-concurrency.md) repeats one batch update as a request trace, a database condition, and a two-writer timeline, then applies it to templates, hiring, and credentials.
- [Lesson 4: Ledgers and effective time](04-ledgers-and-effective-time.md) follows payments, reversals, credits, historical balances, and the two different clocks used by billing configuration.
- [Lesson 5: Assessments and review evidence](05-assessments-and-review-evidence.md) connects leave, contract previews, editable expenses, policy snapshots, and two-approver review with agreement and race tests.
- [Lesson 6: Payroll evidence and durable work](06-payroll-evidence-and-durable-work.md) traces simulation, saved-input replay, variance, completion receipts, job leases, and failure recovery.
- [Lesson 7: Allocation, imports, and frozen views](07-allocation-imports-and-frozen-views.md) explains a one-cent split, CSV parser states, atomic batch creation, and copied roster labels.
- [Lesson 8: Decisions, notifications, and exports](08-decisions-notifications-and-exports.md) follows a failed audit insert, durable inbox creation, personal read markers, and expiring export artifacts.
- [Lesson 9: Time, policies, and typed configuration](09-time-policies-and-typed-configuration.md) separates recording and effective dates, starts coverage from requirements, and preserves types and exact budget thresholds.

## The learning loop

Use the [study routine](study-routine.md) for short sessions and the [24-session practice path](practice-path.md) for a concrete order with recall exercises. Revisit the [four visual models](one-page-mental-models.md) when you want a smaller view of the system. Keep your own observations with the [study journal template](study-journal-template.md).

The [API labs](api-labs.md) and [senior API labs](senior-api-labs.md) provide 18 concrete request sequences and expected persistence outcomes for the implemented feature groups. The [feature route map](feature-route-map.md) connects feature families to their handlers, lessons, and test suites.

You can use the workbook from a terminal:

```powershell
python scripts/bootcamp.py serve
python scripts/bootcamp.py list
python scripts/bootcamp.py show JS-01
python scripts/bootcamp.py test JS-01
python scripts/bootcamp.py quiz
python scripts/bootcamp.py quiz --answers
```

`serve` runs the API at `http://127.0.0.1:5080` using `artifacts/bootcamp/atlas-training-v1.db`, keeping the repository's other database files intact. Use `serve --new` for another isolated practice database, or `serve --database my-session.db` to reuse a named session. It uses Development seeding and WAL; it does not migrate older schemas. `serve --dry-run` prints the launch command without starting a process.

The test command runs the actual registered feature suite and preserves its failure
exit code. It does not mark a story verified automatically. Read the questions
without answers first, then explain your reasoning before checking the answer view.

1. **Predict:** read one request and say what should happen before running it.
2. **Trace:** locate the endpoint, permission check, business rule, database operation, and response.
3. **Observe:** run a focused test and inspect the saved result.
4. **Explain:** describe the behavior first without code, then with exact file names.
5. **Change:** try the lesson's small exercise on your own branch.
6. **Recall:** return later and explain the same flow without reopening the notes.

You do not need to read every file at once. Each lesson names a short reading path and repeats the idea through a concrete request, a code explanation, and a practice question. Repetition is part of learning; there is no deadline for remembering a concept.

## Implementation sequence

| Stage | Focus | What you practice |
| --- | --- | --- |
| 1 | Junior validation, filters, and detail reads | HTTP boundaries, nulls, query composition, tests |
| 2 | Client views and workflow operations | Ownership, shared assessments, transitions |
| 3 | Payments, configuration, and credential lifecycle | Schema, snapshots, concurrency, identity |
| 4 | Payroll assessment, evidence, and retry behavior | Pure calculations, transaction boundaries |
| 5 | Allocation, imports, compliance, and custom data | Typed models, temporal rules, reconciliation |
| 6 | Audit, notifications, jobs, exports, approvals | Durable processing, recovery, cross-feature integration |

Stories may move between stages when a dependency makes that useful. A story is **verified** only after its implementation and relevant behavior tests pass. A compiling handler alone is not completion. Journal entries record what actually happened, including failures and remaining work.

## How to read a completed lesson

Use the smallest example first. Answer “who can do this?” before studying the query. Read the test before the implementation to see the promise. Then follow one successful request and one rejected request. Write a three-sentence explanation in your own words; compare it with the lesson afterward.

The repository already contains changes from earlier learning work. This implementation preserves those changes and does not treat them as new accomplishments in this bootcamp.
