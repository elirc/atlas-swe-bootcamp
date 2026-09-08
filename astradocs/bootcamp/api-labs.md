# API labs: predict, send, inspect

These labs connect the implemented routes to their learning stories. Consult [progress](progress.md) for verification status. Use an isolated learning database or the synthetic integration fixtures; the original workbooks remain the detailed requirements. Start the API using [getting started](../../docs/getting-started.md), and read the [API reference](../../docs/api-reference.md) for the original routes.

The examples below use placeholders such as `<contract-id>`. Read actual IDs and Version values from your own database. The numeric IDs in the test files belong to their fixtures and are not universal application IDs.

Authenticated requests use `X-Api-Key`. “Manager” below means a platform administrator or the visible client's administrator. A client viewer may read scoped information but cannot perform management writes.

## Lab 1: Complete a batch without a partial result

Read `GET /api/contracts/<contract-id>/onboarding`. Choose two incomplete items and copy their ID and Version. Submit:

```http
POST /api/contracts/<contract-id>/onboarding/complete-batch
Content-Type: application/json
X-Api-Key: <manager-key>
```

```json
{
  "items": [
    { "itemId": "<first-item-id>", "expectedVersion": 1, "notes": "Reviewed" },
    { "itemId": "<second-item-id>", "expectedVersion": 1, "notes": null }
  ]
}
```

Replace the example versions with the values you just read. Predict three cases: both are valid, the second ID belongs elsewhere, and the first version is stale. Expected outcomes are success with the whole checklist, 404 with no completion, and 409 with no completion.

After any rejected batch, fetch the checklist again and inspect both items. This second read is the manual equivalent of an atomicity assertion.

## Lab 2: Separate configuration from copied work

| Operation | Route | Important body/behavior |
| --- | --- | --- |
| Read effective template | `GET /api/countries/PH/onboarding-template` | Platform only; absent template has configured=false and Version 0 |
| Replace template | `PUT /api/countries/PH/onboarding-template` | expectedVersion and 1–5 distinct type/title/isRequired items |
| Append contract context | `POST /api/contracts/<id>/notes` | body only; author/time come from the server |
| Read notes | `GET /api/contracts/<id>/notes?page=1&pageSize=10` | Scoped, newest timestamp first |
| Change hiring availability | `PUT /api/countries/PH/hiring-status` | isActive and expectedVersion; platform only |

Create one contract under a configured template. Replace the template's title, then create another contract for a different worker. Inspect both checklists. The older one keeps its copied title.

Pause hiring and inspect activation readiness for an otherwise ready draft. Then try activation. Both paths should explain the pause. Existing active-contract payroll remains a separate operation.

## Lab 3: Keep invoice facts distinct

Record a payment against an invoice using its billing currency:

```json
{
  "amount": 40.00,
  "currencyCode": "USD",
  "paidAtUtc": "2026-08-27T12:00:00Z",
  "reference": "training-receipt-1"
}
```

Send it to `POST /api/invoices/<id>/payments` with a platform key. Choose a paid time between that invoice's issue time and your server's current time. The example date is not valid for every invoice.

Replay exactly the same request, then change the amount while keeping the reference. Predict one original payment row, 200 on exact replay, and 409 on mismatched reuse.

| Read or action | Route | Question answered |
| --- | --- | --- |
| Payment history | `GET /api/invoices/<id>/payments` | Which payments and reversals were recorded? |
| Reverse a payment | `POST /api/invoices/<id>/payments/<payment-id>/reverse` | Correct an entire payment using a reference and reason |
| Record a credit | `POST /api/invoices/<id>/credit-notes` | Reduce billing using amount/currencyCode/reference/reason |
| Effective balance | `GET /api/invoices/<id>/balance?asOf=2026-09-01` | What is outstanding at the UTC cutoff? |
| Credit summary | `GET /api/invoices/<id>/credit-summary?asOf=2026-09-01` | What is net billed, independent of settlement? |
| Outstanding aging | `GET /api/reports/invoice-aging?asOf=2026-09-01` | Outstanding amounts grouped by issue age and currency |
| Credited aging | `GET /api/reports/credited-invoice-aging?asOf=2026-09-01` | Net billed amounts grouped by issue age and currency |

Read the original invoice after each operation. Its issued totals should stay unchanged. Ledger writes are platform operations; scoped clients may read their records.

## Lab 4: Observe the two billing clocks

Append billing terms with `POST /api/clients/<id>/billing-terms`: effectiveDate, termDays from 0–120, and reason. Append a fee schedule with `POST /api/clients/<id>/fee-schedule`: effectiveYear, effectiveMonth, rate, and reason. Both append operations are platform-only and reject duplicate effective dates/periods.

Read `GET /api/clients/<id>/fee-schedule/effective?year=2026&month=9`. Its rate answers a payroll-period question. Read an issued invoice's stored PaymentTermDays, DueDate, ManagementFeeRateApplied, and source IDs. Those fields preserve its actual selections.

Use `GET /api/invoices/<id>/due-status?asOf=...` for a due-date comparison. A legacy invoice has unknown terms. Due status alone does not answer whether a payment was received; use the balance route for that.

## Lab 5: A leave quote can become stale

As a manager, send `POST /api/contracts/<id>/leave-quote`:

```json
{ "type": "Annual", "startDate": "2026-09-07", "endDate": "2026-09-11" }
```

Choose dates inside your actual contract period. Inspect eligible, requestedWorkingDays, allowanceDays, remainingDays, and blockers. A valid but ineligible request returns an explanatory assessment. A malformed or reversed interval returns a validation error.

Create a conflicting leave request, then repeat the quote or attempt the original creation. Explain why the earlier quote did not reserve the interval.

| Configuration/action | Route | Rule to predict |
| --- | --- | --- |
| Calendar read | `GET /api/countries/PH/holidays?year=2027` | Includes configured historical entries too |
| Add holiday | `POST /api/countries/PH/holidays` | date/name; platform only, strictly future date |
| Remove holiday | `DELETE /api/countries/PH/holidays/<id>` | Matching country and future date required |
| Adjust allowance | `POST /api/contracts/<id>/leave-adjustments` | year/type/deltaDays/reference/reason; platform only |
| Read adjustments | `GET /api/contracts/<id>/leave-adjustments?year=2027` | Scoped, paged ledger |
| Cancel approved future leave | `POST /api/leave-requests/<id>/cancel-approved` | expectedVersion; active contract without an end date |

After adding a holiday, compare a new quote with an already saved request's Days. After an allowance reduction, verify that the resulting allowance still covers reserved Pending and Approved days.

## Lab 6: Change an expense before review

Fetch a claim's Version, then PUT the claim with expectedVersion, description, and the complete replacement items list. Old item IDs disappear on success; contract, currency, and submission time stay unchanged.

Try an invalid URL in the last replacement item. Fetch the claim afterward. None of the replacement should have persisted.

Configure `PUT /api/clients/<id>/expense-approval-policies/USD` with threshold and expectedVersion. Initial configuration uses 0. Create a claim exactly at the threshold and inspect its requiredApprovals snapshot.

For a two-approval claim, use a different permitted API identity for the first vote. Status remains Pending and recordedApprovalCount becomes 1. An edit now conflicts. A second distinct permitted identity completes approval. The submitter cannot vote on a two-approval claim, and there is no administrator bypass for that restriction.

Withdraw an untouched Pending claim with `POST /api/expense-claims/<id>/withdraw`, supplying expectedVersion. It remains visible as Cancelled and cannot be reimbursed by payroll.

## Lab 7: Compare without mutating

Use `POST /api/contracts/<id>/amendment-preview` with newMonthlySalary and/or newJobTitle plus effectiveDate. Compare current and proposed terms, monthly delta, percentage delta, and contract Version. A title-only comparison has zero salary delta.

Use `POST /api/contracts/<id>/termination-preview` with proposedEndDate. The date must satisfy the same notice and employment rules as termination creation. Inspect selected salary history, prorated base, unused-leave payout, and estimatedBaseTotal. Do not compare the latter directly with take-home pay: the response names excluded components.

Before and after either preview, inspect proposal row counts and contract Version. Repeated previews do not insert a proposal or change employment state.

## Lab 8: Save a typed report

As a platform administrator, create a preset:

```json
{
  "name": "Philippines headcount",
  "kind": "headcount",
  "filters": { "countryCode": "PH", "asOf": null }
}
```

POST it to `/api/report-presets`, then POST its `/run` route. Inspect the resolved parameters and compare the result with the direct report for those parameters. An explicit asOf stays fixed; null resolves at execution.

Try adding an unknown filter, reading through another platform identity, replacing with a stale expectedVersion, and deleting with `?expectedVersion=<old-version>`. Predict each failure before sending it. The preset belongs to its API identity, not to every platform administrator.

## Lab 9: Simulate, capture, and retry payroll

POST countryCode/year/month to `/api/payroll-simulations`. Repeat it before creating the run. Approved expenses remain Approved, and the repeated simulations include them both times.

Create the run through `/api/payroll-runs`, then inspect `GET /api/payslips/<id>/calculation-evidence`. New evidence includes copied inputs and replay comparisons. A legacy payslip can return not-captured.

Complete a Draft run with an `Idempotency-Key` header. Repeat the same completion with the same key and compare the exact response. Change the run ID while keeping the key and predict the conflict. The receipt is scoped to the authenticated identity and operation.

Compare two saved runs using `/api/reports/payroll-variance?baselineRunId=<id>&comparisonRunId=<later-id>`. Check that row deltas sum to the per-currency total delta. A changed component is a finding to investigate, not proof of its cause.

## Lab 10: Poll durable work

POST countryCode/year/month to `/api/payroll-jobs`. First acceptance returns 202 and a polling Location; duplicate submission returns the existing job. Poll the Location and inspect state, attempt counters, resultRunId, and errorCode.

Failed jobs can be retried with POST `/api/payroll-jobs/<id>/retry` and expectedVersion after their input problem is addressed. Retrying resets the cycle counter and preserves total attempts. Jobs create Draft runs; completion remains a separate action.

For lease-takeover practice, use the dedicated test fixture instead of racing terminal commands by hand. Read `SS05_ExpiredWorkerCannotCommitAfterAnotherWorkerClaims`, predict A's and B's results, and inspect the final database assertions.

## Record what you observed

For every lab, write the expected HTTP result and the expected saved state before running it. Record both actual outcomes afterward. A passing status code with incorrect persistence is still a broken feature. A test runner that fails before discovery provides no assertion results; the engineering journal records that distinction for the local Application Control block.
