# Lesson 2: A view, a saved fact, and an estimate answer different questions

This lesson accompanies the first mid-level read features. Check [progress](progress.md) for their verification status. The code is in [WorkforceViewEndpoints](../../src/Atlas.Api/Endpoints/WorkforceViewEndpoints.cs), [FinancialViewEndpoints](../../src/Atlas.Api/Endpoints/FinancialViewEndpoints.cs), and [OperationsViewEndpoints](../../src/Atlas.Api/Endpoints/OperationsViewEndpoints.cs). Read the [behavior tests](../../tests/Atlas.Tests/Integration/MidlevelViewStoryTests.cs) before trying to understand every handler.

## Three questions that look similar

| User question | Appropriate data | Why |
| --- | --- | --- |
| What did this payslip record? | Stored payslip amounts | Later salary changes cannot redefine it |
| What would these benefit plans cost now? | Current plan prices and split rules | This is a scenario using today's inputs |
| Which contracts cover this date? | Current contract records filtered by employment dates | This reconstructs coverage, not every historical edit |

These are different products even when all three return JSON and numbers. A route name or an asOf parameter does not turn a live query into a historical snapshot.

Say it another way: a receipt, a price quote, and a list of people booked for a date are all useful. They make different promises. The API needs to communicate which one it is returning.

## Count the thing you intend to count

The workforce summary returns workerCount and contractCount separately. Why two fields? A worker ID identifies a person in this model; a contract ID identifies an employment relationship. Counting contracts can overcount people when a fixture or historical data contains multiple covering relationships for the same person.

The test gives two visible covering contracts the same worker ID. It expects one worker and two contracts. It also changes one contract to end on September 1 and checks September 1 and September 2. The inclusive boundary belongs to the employment rule, not to the chart drawing the answer.

Country groups count distinct workers within each country independently. Adding those worker counts is not always the same as counting distinct workers across the whole client. Before writing a dashboard query, write the sentence “one row represents __” and the sentence “we count unique __.”

## A visible worker can have invisible history

The employment timeline first selects contracts the caller can view, then retrieves SalaryRecords for those contracts. It does not merely check that the worker is visible and then load every related salary record.

The test deliberately gives one worker a contract for another client and adds a distinctive foreign salary/title. It verifies that neither value appears in the first client's timeline. This is an example of a test built around the failure we fear, not simply the happy-path screen we want to see.

A useful review question is: “Does this authorization check apply to the exact rows being returned?” Parent visibility can be enough for some child collections, but not when a shared parent has children owned by different clients.

## Explain a command without executing it

Activation readiness uses [ContractReadiness](../../src/Atlas.Domain/Services/ContractReadiness.cs), a domain assessment shared with activation. The read response reports structured blockers. The command still validates current state immediately before changing it.

Readiness is not a reservation. Another request can change state after the GET. The test reads an incomplete checklist, confirms activation rejects it, completes required items, reads readiness again, activates, and then sees a not-draft blocker. The same contract moves through multiple meaningful answers.

Do not implement a preview by calling a command and rolling it back afterward. That invites side effects, version changes, logs, or future integrations to escape your supposed preview. Begin with a calculation or assessment that is read-only by construction, then let the command perform effects explicitly.

## Saved pay does not come from today's contract

The payslip browser projects saved Payslip fields and the run's period/status. It never invokes PayrollCalculator or reads the current salary to replace GrossSalary.

In the test, Contract.MonthlySalary changes to 9000 after a payslip with gross 100 exists. Reading the payslip still returns 100. This looks obvious when explained in words, yet it is easy to break when trying to “reuse” the latest contract object in a financial response.

The invoice breakdown follows the same principle. It sums saved Payslip.TotalCost for the invoice's run **and client**, then compares that sum with Invoice.PayrollSubtotal. A mismatch is returned as a signed difference. The route does not quietly repair the invoice or recompute its fee using the current client rate.

Be careful with components: GrossSalary already includes unused leave payout in final payroll. Adding UnusedLeavePayout to GrossSalary again would double-count it. Components can explain a total without being additional amounts to add to that total.

## Share selection between JSON and CSV

The browser and payslip export use the same scoped query builder. JSON adds paging; CSV caps the matching selection at 1000 and refuses a larger export. Neither representation gets a different permission rule.

Why fetch 1001? It is enough to distinguish “there are exactly 1000” from “there are more than we promise to export.” Returning the first 1000 without saying anything would produce a spreadsheet that looks complete while silently omitting records.

[CsvExport](../../src/Atlas.Api/CsvExport.cs) handles two separate jobs:

1. Escape the CSV structure so commas, quotes, and line breaks remain inside a cell.
2. Neutralize formula-like **text** so spreadsheet software does not treat a user's label as a formula.

Quoting a CSV cell handles the first job; it does not guarantee the second. Numeric money values stay numeric, including negative values. FX rates use a separate decimal format so a rate such as 1.234567 is not rounded like a two-decimal monetary total.

The formatting test runs under a French culture to prove that output still uses invariant decimal points. Your machine's language must not change the meaning of an exported amount.

## Clip display intervals without changing reservations

A leave request can start in August and continue into September. A September calendar should show its overlapping portion. The endpoint returns original dates, saved Days, and separate displayStart/displayEnd fields.

Those display fields are presentation data. They do not replace the saved interval or recalculate the balance reservation. A request with six saved working days remains a six-day reservation even if this calendar page shows only September's portion.

The overlap predicate is `request.StartDate <= window.End && request.EndDate >= window.Start`. A predicate requiring the request to start inside the window would miss an absence that began earlier and is still underway.

## Group expense items at the right level

An expense claim can contain January and February items. The monthly summary starts from matching items, not from each claim's full TotalAmount. Otherwise the same claim total could be charged to both months.

The test uses one claim with 10 in January and 20 in February. The expected monthly amounts are 10 and 20, not 30 and 30. Distinct claim count can be one in both months; that count is not additive across months.

Status is current status. When an Approved claim becomes Reimbursed, its incurred-month amounts move between status columns. This view does not reconstruct what was approved at the end of January. That would require historical state evidence, which a future audit-oriented feature would need to capture.

## Benefits have coverage and price semantics

An Ended enrollment can still cover a historical month. The benefit calendar uses CoversMonth rather than filtering only Active enrollments. January 31 through March 1 overlaps January, February, and March; the existing product charges a full premium for any overlap.

The calendar uses current plan prices and labels its amounts as estimates. A saved payslip remains the source for actual past charges. The benefit quote is a selected basket: it does not silently add existing enrollments or enroll a contract as a side effect.

For an odd-cent plan costing 10.01 at a 50% employer contribution, the existing formula gives employer 5.01 and employee 5.00. One side is the rounded calculation; the other is the remainder. Independently rounding both halves would total 10.02.

## Combine queues without sharing a DbContext concurrently

The approval queue loads pending leave, expenses, amendments, and terminations, taking a bounded newest set from each. It merges those sets under one global ordering and returns counts plus hasMore.

The queries execute sequentially inside a read transaction. A DbContext is not a thread-safe collection of connections. Task.WhenAll over that one context would introduce unsupported concurrent database operations, even if parallel calls look faster in the source.

Each queue entry links to the existing decision endpoint. Reading Pending does not guarantee it will still be Pending when clicked. The decision endpoint remains responsible for rechecking state and permissions.

## Practice the distinction

1. Change a live salary in a test fixture. Predict which of these responses should change: saved payslip, workforce title, salary timeline, benefit quote.
2. Add a missing invoice allocation in a fixture and explain why a reconciliation discrepancy is useful information.
3. Write down the CSV representation of text containing both a comma and a quote. Then explain why spreadsheet formula protection is another step.
4. Draw an absence that begins before a calendar window and ends inside it. Derive the overlap predicate from the drawing.
5. Explain why a queue can be correct when fetched and still return a conflict when an item is later approved.
6. Teach the odd-cent benefit split to someone without using the words decimal or rounding mode.

When reviewing your own feature, finish this sentence: “This number is a __, calculated from __ at __, and it can change when __.” If you cannot fill the blanks, the API probably needs a clearer data contract.
