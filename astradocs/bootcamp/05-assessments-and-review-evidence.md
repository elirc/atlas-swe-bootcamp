# Lesson 5: Preview a decision, then preserve what was reviewed

This lesson connects leave quotes, leave adjustments, contract previews, expense editing, two-approver expenses, and saved reports. These features all ask a similar question: which information can change between reading it and acting on it? Check [progress](progress.md) before treating a feature as verified.

Read [LeaveService](../../src/Atlas.Infrastructure/LeaveService.cs), [ContractProposalService](../../src/Atlas.Infrastructure/ContractProposalService.cs), [ExpenseApprovalService](../../src/Atlas.Infrastructure/ExpenseApprovalService.cs), and [ProposalAndExpenseStoryTests](../../tests/Atlas.Tests/Integration/ProposalAndExpenseStoryTests.cs). You can study one section per session.

## A quote is an explanation, not a reservation

Suppose a contract has five annual leave days remaining. A Monday-to-Friday request needs five working days. Its quote says eligible. Before the user submits it, someone else creates a request that reserves two days. The original submission must now fail.

That is expected behavior. The quote promises an assessment of the inputs it read. It does not hold days for later use. Creation calls the same assessment again, using the then-current inputs.

In `LeaveService`, find `AssessAsync`. Then find its two callers: the leave-quote endpoint and `CreateRequestAsync`. Creation only constructs a LeaveRequest after the assessment succeeds. The quote returns the assessment without constructing or saving a request.

Said another way: asking whether a seat is available does not book the seat. The booking operation checks again and reserves it.

## Follow the leave arithmetic by hand

Use this example before opening the code:

| Input | Days |
| --- | ---: |
| Country annual allowance | 20 |
| Contract adjustment | +3 |
| Approved reservations | 5 |
| Pending reservations | 4 |
| Effective allowance | 23 |
| Remaining | 14 |

Pending leave counts because another in-progress request is already reserving those days. Rejected and Cancelled requests do not reserve allowance. The saved Days field remains on a cancelled request even though balance calculations exclude its status.

Try subtracting 15 days from allowance. Effective allowance would become 8, below the 9 reserved days. The adjustment must fail. Its reason and reference do not make an invalid balance acceptable.

The adjustment writer touches Contract.Version. Leave creation and decisions also coordinate through that contract. A reduction and a new reservation must not both succeed from incompatible views of the balance. This repeats the invoice lesson with a different shared parent.

## Holidays change new assessments

September 7–11, 2026 contains five weekdays. A configured holiday on September 7 reduces the working-day count to four. A Saturday holiday does not reduce it a second time; Saturday was already excluded.

The calculator receives a set of explicit dates. It does not infer substitute holidays or import an official calendar. This is the application's configured calendar model.

What happens to a request already saved with five Days before the holiday was configured? It keeps five. That count is the stored reservation accepted at submission time. Recalculating every old request from today's calendar would change historical decisions implicitly.

Read `MS15_MS17_MS18_QuoteAndCreationShareAdjustedAllowanceAndHolidayDays` in [WorkflowStoryTests](../../tests/Atlas.Tests/Integration/WorkflowStoryTests.cs). It observes a quote, adds configuration, creates a request, and changes the calendar again. The final assertion checks that the saved request did not change.

Explain this twice: first as a database fact (“Days is stored, not recomputed on every read”), then as a user promise (“your existing booking keeps the amount of leave accepted when it was submitted”).

## Cancellation needs both record and contract state

Approved future leave can be cancelled only if its start is strictly after UTC today and its contract is Active with no end date. Cancellation on the first leave day fails. The existing Pending-only cancellation route has its own unchanged state rule.

The request version protects the leave decision. The contract version protects the assumption that the employment relationship still permits this action. A competing termination changes the contract and can make a previously prepared cancellation stale.

Cancellation preserves Days and the earlier decision note, while recording the latest decision time. This is not a complete decision-history table; do not claim that every earlier decision timestamp is preserved by this slice.

## Two contract previews intentionally use different salary sources

An amendment preview compares current Contract fields. It answers “what would these currently displayed terms become?” If current salary is 4,000 and proposed salary is 4,400, delta is 400 and percentage delta is 10%. A title-only proposal has zero salary delta.

A final-pay preview selects salary history for the proposed ending month, using `SalaryRecord.EffectiveForMonth`, with current salary only as a fallback. It answers a payroll-period question. A future-effective salary record must not leak into an earlier month's calculation merely because it already changed the current Contract field during approval.

Neither preview applies an amendment or terminates a tracked entity and then tries to undo it. They calculate local result values. Temporarily mutating a tracked entity can leave unexpected changes in the change tracker, making a later save persist work that was meant only as a preview.

Read the two `ContractPreviewEndpoints` routes side by side. One calls proposal eligibility and calculates a comparison; the other calls proposal eligibility and computes base final pay from period inputs. Both authorize management first and use a read transaction for a coherent set of inputs.

## Base final pay is not take-home pay

The existing model prorates the final month by calendar days and derives a daily annual-leave rate from 12 monthly salaries divided by 260 working days. These are Atlas's existing simplified calculation rules.

For monthly salary 3,000 and an eligible September 15 end date:

| Component | Calculation | Result |
| --- | --- | ---: |
| Prorated base | 3,000 × 15 / 30 | 1,500.00 |
| Daily rate | round(3,000 × 12 / 260) | 138.46 |
| Two unused annual days | 138.46 × 2 | 276.92 |
| Estimated base total | 1,500.00 + 276.92 | 1,776.92 |

The preview excludes deductions, employer contributions, reimbursements, benefits, and invoice fees. A complete payslip can therefore have different NetPay and TotalCost. The preview's name and returned scope make that boundary visible.

Without a leave policy, the payout is zero to match payroll's existing behavior, but allowance and unused-day figures are null. Null here means “not configured,” not “the worker has an entitlement of zero.” The response also exposes `leavePolicyConfigured`.

The test `MS24_PreviewMatchesBaseComponentsOfLaterFinalPayroll` checks the same components against a subsequently created payroll run. Agreement tests are valuable because two separate endpoints can otherwise drift while each has individually plausible examples.

## Editing expenses changes the whole reviewed set

An expense edit replaces all items. It is not a patch to selected old item IDs. The request cannot change the contract, currency, claim ID, or original submission time.

The endpoint validates every replacement item before removing old rows. It then creates fresh item IDs, updates the description, and touches the claim's version. Touching the parent matters even if only child amounts changed: approval acts on the claim, so it must conflict with a stale item edit.

The shared `ExpenseInput` rules apply to creation and editing. They bound descriptions, item counts, date years, and receipt URLs. Existing positive decimal amount semantics remain; this feature does not add an unrelated two-decimal expense restriction.

Practice: replace 10 + 20 with 15 + 25. Predict total, item IDs, version, and submission time. Then introduce one invalid receipt URL and predict which rows survive. The answer is all the original rows: validation fails before replacement.

## Approval evidence changes what can still be edited

A configured threshold of 100 means 99.99 requires one approval, while 100 requires two. The claim captures the threshold, policy version, required approval count, and submitting API identity when accepted. Changing the policy later does not reinterpret that existing claim.

Before any votes, an edit recalculates the applicable policy snapshot for the replacement amount. After any vote, editing fails. Otherwise an approver could review 100 while the final approved claim silently became 10,000.

For a two-approval claim, the submitter cannot vote. Reviewer B's first vote leaves the claim Pending. Reviewer C's second vote changes it to Approved. B voting twice conflicts. The service saves each vote and parent version together, and only calls the domain approval transition when the required count is reached.

Payroll still selects explicitly Approved claims. A first vote is evidence of review, not permission to reimburse. Rejection while Pending preserves earlier votes as evidence of what happened without claiming that the claim was finally approved.

The tests check both details: no reimbursement after the first vote, and no extra final vote left behind when rejection wins a race. Legacy Approved claims can have no recorded voters; their historical status is preserved and missing evidence is labeled, rather than inventing people who supposedly approved them.

Two different API-user IDs are two credential identities. This feature does not prove that two different humans control them. There is no administrator bypass for the submitter restriction in this slice.

## A saved report is typed configuration

MS-28 stores a report kind and validated filters. It does not store SQL or an arbitrary URL. `ReportPresetRunner` dispatches through four explicit supported report kinds.

An unknown filter field fails rather than being silently ignored. Saved filters are validated again at execution, because stored configuration can become invalid or be altered outside the API. A null as-of value resolves to the clock when the preset runs; an explicit date stays fixed.

Ownership is the current API-user ID, even for platform administrators. Role says the caller may use platform reporting; ownership says which saved configuration belongs to that caller. A replacement credential receives a new ID and does not automatically inherit the old ID's presets.

Find the direct-versus-saved assertion in `MS28_PresetsAreTypedOwnedVersionedAndMatchDirectReports`. It compares the actual report result, not merely successful HTTP codes. Running also leaves preset Version unchanged: execution is a read, while replacing filters is a write.

## A short review session

Choose one feature and answer these without reopening the implementation:

1. Which values are live inputs, and which are stored evidence?
2. What can change after a preview but before a write?
3. Which parent version coordinates competing writes?
4. What must remain unchanged after failure?
5. Which test proves that unchanged state?

For your journal, write one request trace in plain language and another using exact method names. Next session, try a different feature and identify the same five questions. Repeatedly recognizing the pattern is more useful than memorizing one handler line by line.
