# Lesson 7: splitting money, staging imports, and freezing a view

This lesson follows SS-09, SS-10, SS-11, and SS-19. They look like four unrelated features. They share a question: **which facts should be calculated now, and which facts must be preserved for later?** Spend several sessions here. Finishing a page is less useful than being able to explain one example without looking.

## First pass: the user promises

A cost center is a client's label for a spending bucket, such as Engineering or Support. A contract allocation schedule says how to split future payroll between those buckets. A saved allocation records the split that actually accompanied a payslip. Changing a future schedule must not rewrite an older payslip's split.

An import preview says whether a small CSV can currently create workers. A committed import records the workers actually created. Passing preview does not reserve every email until tomorrow. Another request can use an email before commit, so commit checks again.

A workforce snapshot preserves a roster. Its date controls who belongs in the roster. Its capture time tells you when names and titles were copied. Those are different facts. A snapshot for January captured in September can contain a name that changed in July; the system does not pretend to reconstruct January's name.

Say those promises out loud. Then reduce each to one sentence:

- Allocate future payroll using a schedule, and preserve the result with the payslip.
- Preview a batch without creating workers; create all its workers together when committing.
- Copy the roster once, and read the copied values later.

## Second pass: one cent, three departments

Suppose three centers have weights 3334, 3333, and 3333. The unit is a **basis point**, one ten-thousandth of the whole. Their weights sum to 10,000, which means 100%.

You need to allocate $0.01. Multiplying by each weight gives fractional cents. Rounding each share independently can produce zero cents for all three. You would have lost the only cent.

The implementation uses the largest-remainder method. Work in integer cents. Compute each share, keep the integer floor, then distribute the remaining cents to the largest fractional remainders. A tie uses cost-center ID ordering, so the outcome is repeatable. The 3334-weight center receives this example's cent. The allocated amounts sum back to $0.01.

Open [CostAllocation.cs](../../src/Atlas.Domain/Services/CostAllocation.cs). Find `Split`. Trace `cents`, `portions`, `remainders`, and `remaining`. Write their values for the example before trying a larger amount. `BigInteger` prevents the intermediate multiplication from overflowing when a valid decimal amount is multiplied by a weight. This is an arithmetic implementation choice; the API still returns decimal amounts.

Now consider a subtler mistake. If you split gross pay, deductions, and net pay independently, each field can sum correctly across departments while one department's row fails `net = gross - deductions + reimbursements`. Column correctness alone is insufficient.

`Calculate` therefore splits the primitive components and derives each row's gross, net, and total cost. Unused leave is first separated from gross so it is not counted twice. Follow these equations:

```text
base gross = payslip gross - unused leave payout
allocated gross = allocated base gross + allocated unused leave payout
allocated net = allocated gross - employee deductions - employee benefits + reimbursements
allocated total cost = allocated gross + employer cost + employer benefits + reimbursements
```

The first version requires nonnegative primitive amounts with at most two decimal places for configured splits. It rejects unsupported precision instead of silently losing part of the source amount. Each resulting cent amount must also round-trip exactly through decimal storage: extremely large numbers can exhaust decimal precision even when the intermediate integer arithmetic is exact. An unconfigured contract is copied into one Unallocated bucket and preserves the original values. Explain why this explicit fallback differs from pretending an old payslip had a schedule.

## Trace the allocation through the layers

Read these files in order, one function at a time:

1. [CostAllocationEndpoints.cs](../../src/Atlas.Api/Endpoints/CostAllocationEndpoints.cs): find cost-center creation, schedule creation, and their ownership checks. A center belongs to a client, and every selected center must belong to the contract's client.
2. [PayrollAssessmentService.cs](../../src/Atlas.Infrastructure/PayrollAssessmentService.cs): find the schedule effective for the payroll period. It loads the schedule and copied center labels, then invokes pure allocation arithmetic.
3. [PayrollService.cs](../../src/Atlas.Infrastructure/PayrollService.cs): find `PayslipCostAllocations.AddRange`. The saved allocations, payslip, calculation evidence, and expense reimbursements participate in the same creation transaction.
4. [AtlasDbContext.Advanced.cs](../../src/Atlas.Infrastructure/AtlasDbContext.Advanced.cs): find the unique payslip/bucket index. It prevents duplicate buckets from being stored for one payslip.
5. Return to the report endpoint. It reads saved allocation rows, not today's schedule or center names.

Draw three boxes: schedule, assessment, saved allocation. Label the first arrow “select by payroll month” and the second “copy within payroll commit.” Add an arrow from saved allocation to report. There should be no arrow from today's schedule directly into a historical allocation report.

The report keeps legacy uncaptured costs visible. For each period and currency:

```text
allocated total + legacy uncaptured total - source payslip total = reconciliation difference
```

A correct captured dataset has difference zero. A nonzero difference is evidence to investigate; hiding it with a clamp would conceal a data problem. Never add USD and EUR into one number. Currency belongs in the grouping key.

## Third pass: CSV is a small language

Open [WorkerInput.cs](../../src/Atlas.Domain/Services/WorkerInput.cs). It contains shared worker normalization and a bounded CSV parser. Find the `quoted` and `closed` variables. They represent parser states.

This input has four columns, even though the name contains a comma:

```csv
FullName,Email,CountryCode,DateOfBirth
"Lee, Sam",sam@example.test,PH,1995-04-12
```

This name contains a literal quotation mark:

```csv
"Sam ""Sunny"" Lee",sunny@example.test,PH,
```

Within a quoted value, two consecutive quotes represent one quote. A newline inside quotes is part of the field, not the start of another worker. `Split(',')` cannot represent those rules. Walk through the parser with your finger and say which state you are in after each character of a short quoted example.

The request body is capped before JSON decoding. The decoded CSV has its own smaller byte cap and a maximum of 100 data rows. These are separate limits because JSON escaping can make the transport larger than the CSV string it contains. The parser demands the exact four-column header and reports malformed quoting before creating a batch.

After parsing, each row uses the same normalization as single-worker creation: trim the name, normalize email casing, uppercase a configured two-character country code, and reject future birth dates. Country existence and duplicate emails are database concerns layered on top. The import additionally detects duplicate emails inside the same batch. Country identifiers preserve the existing catalog's accepted format; currency validation has a separate three-ASCII-letter rule.

## A batch lifecycle you can rehearse

Read [WorkerImportEndpoints.cs](../../src/Atlas.Api/Endpoints/WorkerImportEndpoints.cs). Cover the code and reconstruct this table:

| Stored state | Commit behavior |
| --- | --- |
| Invalid | Conflict; correct the CSV and validate a new batch |
| Validated, expired | Gone; validate a new batch |
| Validated, current version | Recheck current data and attempt one atomic commit |
| Committed | Return the existing row-to-worker mapping |

A batch is owned by an API identity. Another platform identity receives 404 for its ID. This protects the staged worker data even though both identities have platform roles. The endpoint takes the owner from authenticated claims, never from the request body.

Imagine preview succeeds for two emails. Before commit, another request creates the second email. What should happen? Neither import worker should be created. The commit recheck detects the conflict, and the unique worker-email constraint still protects the race between that check and saving. This is the recurring distinction between a helpful check and the final database guarantee.

Imagine instead the import commits successfully but the client loses the response. Retrying the same batch returns its saved mapping. It does not create two more workers. The committed-state check comes before the expiry check: expiry prevents acting on stale validation, while an already committed mapping remains evidence of an earlier completed action.

Write this in ordinary language: “The preview is a reservation” is false. “The completed batch remembers its results” is true.

## Freeze a view without inventing history

Open [WorkforceSnapshotEndpoints.cs](../../src/Atlas.Api/Endpoints/WorkforceSnapshotEndpoints.cs). Locate the capture predicate: non-Draft contracts whose inclusive employment interval covers `asOf`. If a contract ends September 15, it belongs to a September 15 snapshot and does not belong to a September 16 snapshot.

The projection copies worker name, title, country, and employment dates. Reading snapshot detail queries the saved rows. It does not rejoin the current Worker table to recover a name. That one query choice makes an older snapshot stable after a name change.

The header separately counts contracts and distinct workers. A fixture can contain one worker with two qualifying contracts, producing contractCount 2 and workerCount 1. Do not “simplify” the count by substituting one for the other.

Comparison uses ContractId as its identity key. A retained contract can have changed captured fields. Added and removed mean present in only one saved roster; they do not prove that an employment event occurred between the two captures. Both requested dates and capture timestamps accompany the comparison to make that limitation visible.

Try a prediction exercise: capture A; change a worker's live name; read A; capture B for the same date; compare A to B. Predict every name and classification before running it. A keeps the old name. B has the new name. The contract is retained, with `workerName` listed as changed.

## Review and retrieval practice

Close the source and answer these without memorizing method names:

1. Why can allocating every total separately produce an inconsistent department row?
2. What does Unallocated mean, and how is it different from legacy not-captured?
3. Which operation makes the allocation snapshot durable?
4. Why must import commit repeat checks already performed during validation?
5. How does a committed batch make a lost-response retry safe?
6. Which values does `asOf` control in a workforce snapshot?
7. Why can two snapshots with equal `asOf` legitimately have different names?

Return tomorrow and answer the same questions using a drawing instead of prose. On a third day, explain one answer to an imaginary reviewer and point to the exact database constraint or copied field that supports it. Repetition should change the representation: example, diagram, code trace, and explanation.
