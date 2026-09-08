# Lesson 4: Keep the facts; calculate the balance

This lesson connects MS-11–13 with SS-06–08. Read [InvoiceLedgerService](../../src/Atlas.Infrastructure/InvoiceLedgerService.cs), [InvoiceLedger](../../src/Atlas.Domain/Services/InvoiceLedger.cs), and the billing tests in [WorkflowStoryTests](../../tests/Atlas.Tests/Integration/WorkflowStoryTests.cs). The [progress register](progress.md) distinguishes implementation from verified behavior.

## Begin with three different sentences

“We billed 100.” “We received 40.” “We corrected the bill by 10.” These are three facts, not three edits to one number.

An invoice preserves the original billed amount. A payment records money received elsewhere. A credit note records a billing reduction. Atlas's ledger endpoints record these facts; they do not charge a card, transfer money, or initiate a refund.

For an invoice in USD:

| Fact | Original billed | Net billed | Outstanding |
| --- | ---: | ---: | ---: |
| Issue invoice | 100.00 | 100.00 | 100.00 |
| Record payment 40.00 | 100.00 | 100.00 | 60.00 |
| Issue credit 10.00 | 100.00 | 90.00 | 50.00 |
| Reverse the payment | 100.00 | 90.00 | 90.00 |

The original invoice still says 100.00 in every row. The balance is a calculation over the invoice and its related entries.

Say it another way: keep the receipt book, then add up the receipts. Do not erase a receipt to make today's sum look right.

## Follow one payment through the code

Open [InvoiceLedgerEndpoints](../../src/Atlas.Api/Endpoints/InvoiceLedgerEndpoints.cs). The route loads a visible invoice, validates transport fields, obtains the authenticated actor, and delegates the write to the service.

The service performs the ledger-specific checks:

1. Load the existing entries after loading the tracked invoice.
2. Look for a matching normalized reference.
3. If the reference already exists, compare the normalized payload and return a replay or conflict.
4. Check billing currency and the payment's effective timestamp.
5. Add the proposed movement to an in-memory candidate history and validate its balances.
6. Insert the payment and touch Invoice.Version.
7. Save both together.

The endpoint accepts positive amounts with at most two decimal places. It does not silently round 1.001 to 1.00. Silent correction would change the fact the operator intended to record. The currency must match the invoice's saved billing currency; changing today's FX table cannot rewrite the payment.

## A retry is not always a new action

Suppose the server saves a payment, but the client loses the response. The client cannot tell whether the operation completed. Sending another payment with a new reference could duplicate the record.

The client instead repeats the same reference and payload. An exact normalized replay returns the original payment and 200; the first creation returns 201. Reusing that reference for a different amount returns 409.

Whitespace around the reference is trimmed, but case remains meaningful. `receipt-A` and `receipt-a` are different references. DateTimeOffset comparison treats timestamps representing the same instant as equal; stored timestamps use UTC.

Read `MS11_MS12_MS13_SS06_LedgersReplayAndKeepBilledDistinctFromOutstanding`. Find the repeated request and the final row-count assertion. A correct-looking second response is insufficient: a replay must leave one stored payment.

The service also handles a competing writer at save time. After a recognized unique/version conflict, it opens a fresh context and checks whether the winning row is the exact same operation. It returns that row if so. It does not reuse the failed change tracker and blindly retry a different payment.

## Why a unique reference cannot protect the balance

Invoice total: 100. Alex tries payment 60 with reference A. Blair tries payment 60 with reference B. Both references are unique. Both operators could read a remaining balance of 100.

If reference uniqueness were the only check, both inserts could succeed and the balance would become -20. The common invoice version prevents this. Both writers try to update the same versioned parent alongside their child insert; one stale write loses and its insert rolls back.

Reference uniqueness answers “have we already recorded this operation?” The invoice version answers “did the shared balance change while this operation was being prepared?” You need the answers to both questions.

Credits, payments, and reversals use the same invoice version. Protecting each table independently would miss conflicts between different kinds of entries.

## A reversal adds evidence

An incorrect payment is reversed with a new record containing the original payment ID, a reference, a reason, an actor, and server time. The original amount and timestamp stay unchanged. The payment list includes reversal metadata and an effective net amount of zero after reversal.

The route checks both invoice ID and payment ID. Even a platform administrator cannot reverse invoice A's payment through invoice B's URL. Broad permission does not make an inconsistent parent-child relationship valid.

This slice supports one full reversal, at server time. It does not accept a replacement amount or backdated reversal timestamp. That smaller contract makes the correction rule easier to reason about and test.

## Effective time differs from recording time

A payment has two timestamps:

- `PaidAtUtc`: when the payment is said to have occurred.
- `RecordedAtUtc`: when Atlas recorded it.

An as-of balance uses the payment's effective time. A payment entered today with last week's paid date can therefore change last week's reconstructed balance. This report means “effective history using records we know now.” It does not mean “exactly what the system knew last week.”

A reversal uses its server recording time as its effective time. A credit uses its server issue time. The endpoint turns an as-of UTC calendar day into an inclusive end-of-day cutoff.

Practice with invoice 100 issued August 1, payment 40 effective August 10, and reversal August 20:

| As-of day | Outstanding |
| --- | ---: |
| August 9 | 100.00 |
| August 10 | 60.00 |
| August 19 | 60.00 |
| August 20 | 100.00 |

An as-of date before issuance returns a validation error on the invoice detail route. The report excludes invoices not yet issued by its cutoff.

## The backdating trap

Now make the payment 100. Reverse it on day 10. Today's outstanding balance is back to 100, so recording another payment of 100 today is acceptable.

What if that second payment is dated day 1 instead? Between day 1 and day 10, the history would contain two payments totaling 200 against an invoice of 100. Today's balance alone cannot detect that earlier negative balance.

The pure `ValidateHistory` function orders movements by effective instant and checks the balance after each group. Movements at exactly the same instant are grouped, avoiding an invented intermediate order for simultaneous facts. The unit test `MS11_BackdatedPaymentCannotOverdrawAnEarlierBalance` contrasts the rejected day-1 payment with an accepted day-10 payment.

This is a useful engineering habit: test the invariant over the entire history the API promises, not only the latest value.

## Two reports answer two questions

Payment-aware invoice aging uses outstanding billing-currency balances. Credited invoice aging uses net billed amounts, excluding payment settlement. They are deliberately different views. Both retain age-from-issuance buckets: 0–30, 31–60, 61–90, and over 90 days.

Neither report combines currencies. A USD balance and a EUR balance form separate groups. The queries load inputs in batches under a read transaction so the invoice and its ledger entries describe a coherent read.

With both workbooks enabled, the implementation rejects a credit that would make outstanding negative after payments. This combined policy is recorded in the journal. A future product supporting cash refunds would need an explicit refund/overpayment model; changing a label would not be enough.

## Configuration has two clocks too

Billing terms select by invoice issue day. Fee schedules select by payroll run period. Read the pure selectors in [BillingConfiguration](../../src/Atlas.Domain/Services/BillingConfiguration.cs).

For example, a September payroll run completed in November selects September's applicable fee. Its invoice due date uses the terms effective on the November issue day. These choices answer different commercial questions, so one “current configuration” lookup cannot serve both.

[PayrollService](../../src/Atlas.Infrastructure/PayrollService.cs) stores the chosen terms, due date, fee rate, and source IDs on the new invoice. Later configuration changes do not move that invoice's due date or recalculate its fee. Legacy invoices expose null evidence rather than pretending we know which historical configuration produced them.

The fee remains rounded after multiplying the sum of gross salary. Rounding each employee's tiny fee contribution first can produce a different total. When changing configuration selection, preserve the existing arithmetic boundary unless the requirement explicitly changes it.

Due status says `not-yet-due`, `due-today`, `past-due-date`, or `unknown-terms`. Being past the due date is not evidence that an invoice remains unpaid; read the ledger balance to answer that separate question.

## Recall and review

Explain each statement in your own words before checking the source:

1. A replay compares the normalized payload before testing available capacity again.
2. A reversal preserves the original payment.
3. A credit changes net billed; a payment changes settlement.
4. A parent version coordinates writers to different child tables.
5. A later-recorded payment can change a past effective-date report.
6. An issue date and a payroll period can choose different configuration rows.

For a hands-on exercise, sketch a 132 invoice, a 40 payment, a 12 credit, and a reversal. Predict net billed and outstanding after every step. Then read the integration test with those exact amounts. Repeat the explanation tomorrow using different amounts, without looking at this table.
