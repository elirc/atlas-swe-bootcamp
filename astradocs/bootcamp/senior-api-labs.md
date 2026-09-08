# Senior API labs: small requests, durable evidence

Use these with [Lessons 6–9](README.md) and the [earlier API labs](api-labs.md). Start with a fresh disposable learning database using [getting started](../../docs/getting-started.md). Keep an existing database intact and select a new database filename: EnsureCreated does not upgrade an existing schema. The API examples use placeholders, never real credentials. Replace IDs, dates, and version numbers with values from your running application.

For each lab, write a prediction first. Send the request. Read the result again through another endpoint. Record what persisted, what did not, and which rule explains the outcome. Passing a response-code check alone is not enough for these features.

## Lab 11: Give one contract a department allocation

Create two cost centers with a manager key:

```http
POST /api/clients/<client-id>/cost-centers
Content-Type: application/json
X-Api-Key: <manager-key>
```

```json
{ "code": "ENG", "name": "Engineering" }
```

Repeat with `SUPPORT`. Save both returned IDs. Submit a schedule for the current or a future month:

```http
POST /api/contracts/<contract-id>/cost-allocations
```

```json
{
  "year": 2026,
  "month": 10,
  "items": [
    { "costCenterId": "<engineering-id>", "basisPoints": 6000 },
    { "costCenterId": "<support-id>", "basisPoints": 4000 }
  ]
}
```

Change the example period if it is already past. Weights are integers totaling 10000. Predict the response for a total of 9999, a repeated center, a foreign-client center, and a duplicate effective period. The first three are validation errors; a duplicate stored period conflicts.

Run payroll simulation for that country and period, then create payroll using a platform key. Read `GET /api/payslips/<payslip-id>/cost-allocations`. Compare each saved component sum with the payslip. A contract without a selected schedule has one explicit Unallocated row. A pre-feature payslip with no rows reports `available: false` and `reason: "not-captured"`.

Read the report after completing the run:

```http
GET /api/clients/<client-id>/cost-center-payroll?fromYear=2026&fromMonth=10&toYear=2026&toMonth=10
```

For each currency, verify `allocatedTotal + legacyUncapturedTotal - sourcePayslipTotal` equals `reconciliationDifference`. It should be zero for valid data. The `not-captured` bucket remains visible separately from Unallocated. Draft payroll and invoice fees are excluded.

## Lab 12: Validate a CSV, then prove retry behavior

Send with a platform key:

```http
POST /api/worker-imports/validate
Content-Type: application/json
```

```json
{
  "csv": "FullName,Email,CountryCode,DateOfBirth\n\"Lee, Sam\",sam.training@example.test,PH,1995-04-12\nTaylor,taylor.training@example.test,PH,"
}
```

The comma in the quoted name is part of one field. An empty birth date is permitted. Source row numbers count CSV records, including the header; a quoted newline does not start another record. Use configured country codes and unused emails. Save the batch ID and version. Read the Workers list: neither new worker should exist yet.

```http
POST /api/worker-imports/<batch-id>/commit
```

```json
{ "expectedVersion": 1 }
```

Use the actual version. The successful response includes created worker IDs by source row. Repeat the commit: it should return the same mapping without creating more workers. Read `GET /api/worker-imports/<batch-id>` to inspect the saved result.

For the failure exercise, validate another two-row CSV. Create one of its emails through the ordinary Worker endpoint before committing. The import should conflict and create neither of its workers. Fixing invalid data requires a new validation batch. The exact-expiry and rollback tests show cases that are easier to reproduce with the fixture clock and an injected database failure than by waiting 24 hours.

## Lab 13: Renew metadata and assess coverage

With a platform key, renew an existing leaf document:

```http
POST /api/workers/<worker-id>/documents/<document-id>/renew
```

```json
{ "name": "Renewed passport", "issuedDate": "2026-09-01", "expiryDate": "2027-09-01" }
```

Use dates allowed by the current server clock. The result has a new ID and a `supersedesDocumentId` pointing to the old record. Read the old record: its expiry has not changed. Read `GET /api/workers/<worker-id>/documents/<document-id>/renewal-history` and follow the chain. Attempting another renewal of the same predecessor conflicts.

Read `GET /api/countries/PH/document-policy`, then configure its expected version:

```http
PUT /api/countries/PH/document-policy
```

```json
{
  "expectedVersion": 0,
  "requirements": [
    { "type": "Passport", "minimumRemainingDays": 30, "expiryRequired": true },
    { "type": "WorkPermit", "minimumRemainingDays": 0, "expiryRequired": true }
  ]
}
```

Read `GET /api/contracts/<contract-id>/document-coverage?asOf=2026-09-01`. Predict the result when no WorkPermit exists even if several passports exist. The missing requirement remains visible. This reports metadata coverage under current policy; it does not add an activation condition or establish legal authenticity.

## Lab 14: Compare a budget with completed payroll

With a manager key:

```http
PUT /api/clients/<client-id>/payroll-budgets/2026/10/USD
```

```json
{ "amount": 1000.00, "expectedVersion": 0 }
```

Read `GET /api/clients/<client-id>/payroll-budget-report?year=2026`. Before completing October payroll, the budget can exist with actual zero. After completion, actual uses saved payslip TotalCost. Compare the amount with the payslips, excluding management fees and billing FX. Repeat an update with the old expected version and predict a conflict.

For the exact-value exercise, read the test using actual 899.999 against budget 1000. The displayed utilization rounds to 90.00, but its status stays within-budget. Explain which number controls the decision.

## Lab 15: Replace typed contract metadata atomically

Create definitions with a manager key:

```http
POST /api/clients/<client-id>/contract-field-definitions
```

```json
{ "key": "team", "label": "Team", "type": "choice", "options": ["ops", "eng"] }
```

Create a second definition with key `training_allowance`, label `Training allowance`, type `number`, and no options. Read `GET /api/contracts/<contract-id>/custom-fields` and copy its contractVersion.

```http
PUT /api/contracts/<contract-id>/custom-fields
```

```json
{
  "expectedContractVersion": 1,
  "values": [
    { "key": "team", "value": "eng" },
    { "key": "training_allowance", "value": 12.3456 }
  ]
}
```

Use the actual version. The number should remain a JSON number. Try replacing `team` with an undefined choice while also changing the allowance. After rejection, reread both previous values. Neither should change. An empty `values` list intentionally clears the set, using the latest contract version. These values are metadata and do not alter payroll.

## Lab 16: Follow a decision into a personal inbox

Approve a valid amendment or terminate an active contract using the existing workflow routes. Read:

```http
GET /api/clients/<client-id>/contract-decisions?afterSequence=0&limit=10
GET /api/contracts/<contract-id>/contract-decisions?afterSequence=0&limit=10
```

Find the actor, source kind, source ID, resulting contract version, and before/after values. The response excludes reasons and credentials. Use `nextAfterSequence` as the next request's bookmark.

With the hosted dispatcher enabled, read `GET /api/clients/<client-id>/notifications?unreadOnly=true&limit=25`. Notifications can appear after a short processing delay. Deterministic tests disable the loop and call `DispatchOnceAsync` explicitly.

Using a client viewer key, send `POST /api/clients/<client-id>/notifications/<notification-id>/read`. Repeat it. Both successful calls return 200, and that identity's unread count falls. Read the inbox with another permitted identity: its unread count remains unchanged. This personal write does not grant permission to activate contracts.

## Lab 17: Request an expiring client export

With a manager key:

```http
POST /api/clients/<client-id>/data-exports
```

```json
{ "fromPeriod": "2026-07", "toPeriod": "2026-09", "reference": "quarterly-training-1" }
```

Save the 202 Location and poll it. The result includes job state and, after success, artifact metadata and downloadPath. Download JSON with the same current authorized owner. A different platform identity receives 404 for this owner's export. Reusing the same reference and period returns the existing job; changing the period under the same reference conflicts.

Inspect the JSON fields. Workers contain ID, full name, and email, without DOB. Contracts are all client contracts; payslips and invoices are limited to the requested periods and saved client ownership. Check generatedAtUtc separately from the requested period. The artifact expires after 24 hours; the exact-expiry tests verify 410 before and after byte cleanup.

If background processing is disabled, submission remains queued until a worker processes it. Jobs use `Jobs:Enabled`; the notification loop uses `Notifications:Enabled`. These are independent configuration switches. A failed export reports a safe error code, including explicit row and byte limits where applicable.

## Lab 18: Compare two frozen rosters

With a manager key:

```http
POST /api/clients/<client-id>/workforce-snapshots
```

```json
{ "asOf": "2026-09-01", "label": "Before review" }
```

Read the returned detail Location with `page` and `pageSize` to inspect saved rows. Capture another snapshot after a controlled fixture change. The application has no general worker-name editing route in this slice; use the test's explicit fixture setup to study name changes rather than inventing an endpoint.

```http
POST /api/clients/<client-id>/workforce-snapshots/compare
```

```json
{ "baselineId": "<first-snapshot-id>", "comparisonId": "<second-snapshot-id>" }
```

The direction is the order you chose. Compare added, removed, and retained rows by ContractId. Retained rows list changed fields and both captured versions. Equal asOf dates are allowed for distinct snapshots. Their capturedAtUtc values explain when labels were copied.

## Finish one lab before starting another

For each session, save four notes: your prediction, the response, a fresh read proving persistence, and the source file that enforces the promise. On the next day, hide the response and predict it again. When the happy path feels familiar, repeat with a foreign ID, an old version, or a deliberate failing input. That is how the same feature becomes practice in authorization, state, and reliability rather than a copied command.
