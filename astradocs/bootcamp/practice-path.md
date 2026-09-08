# A repeatable practice path from junior tasks to system design

The implementation is a worked example, not an exam you must understand in one sitting. Use 25–45 minute sessions. Repeat a session when you need to; the session numbers are an order, not deadlines. Your evidence of progress is increasingly accurate explanations and predictions, not pages read.

Start with [the feature map](feature-route-map.md). Keep [your own journal](study-journal-template.md) beside [the implementation journal](journal.md). They have different jobs: the implementation journal records what happened during this build; yours records how your understanding changes.

## Sessions 1–6: follow a request without getting lost

| Session | Read and do | Evidence to leave in your journal |
| --- | --- | --- |
| 1 | Read the bootcamp README and run `python scripts/bootcamp.py show JS-01`. Locate the named endpoint and test. | A five-box drawing: request, authentication, endpoint, database, response |
| 2 | Choose one junior validation story. Predict blank, maximum-length, and over-limit input. | A three-row input/expected-result table, corrected after reading the test |
| 3 | Read a scoped Worker or Contract list. Trace a viewer from another client. | The exact query predicate that limits ownership |
| 4 | Follow name-prefix filtering and stable pagination in Lesson 1. | An example where equal names need an ID tie-breaker |
| 5 | Read one junior failure test and explain why it checks a boundary. | A plain-language explanation and a code-level explanation of the same test |
| 6 | Revisit the first story without notes. Explain it aloud, then reopen the source. | What you recalled, what you missed, and one corrected assumption |

At this stage, do not try to memorize the entire DbContext. Recognize the role of each layer. The API interprets requests and checks access. Domain code expresses rules and calculations. Infrastructure reads and saves data. Some older endpoints directly use the context; the feature map helps you find those paths rather than assuming every feature has identical structure.

## Sessions 7–12: reason about stored facts and complete changes

| Session | Read and do | Evidence to leave in your journal |
| --- | --- | --- |
| 7 | Compare a live workforce view with a saved payslip in Lesson 2. | Two lists: values that may change next week and values copied when created |
| 8 | Work through the odd-cent benefit example. | The gross/employer/employee arithmetic with the rounding point marked |
| 9 | Follow batch onboarding from validation through SaveChanges. | A prediction for a valid first item and invalid second item |
| 10 | Read the template replacement failure in journal Entry 006. | A change-tracker table: Added new children, Deleted old children, Modified parent |
| 11 | Draw two requests trying to change the same versioned parent. | Which request wins, why the other conflicts, and what persists |
| 12 | Explain one workflow to an imaginary reviewer in five sentences. | User behavior, access, validation, transaction, test evidence |

Your first small modification exercise can be a local change to a validation message or response label. Predict affected callers and tests before editing. Then restore your practice change if it is not intended for the product. Work on your own branch and avoid resetting someone else's changes. The goal is to experience the edit/build/check loop while the behavior is still easy to reason about.

## Sessions 13–18: explain time and financial evidence

| Session | Read and do | Evidence to leave in your journal |
| --- | --- | --- |
| 13 | Work the backdated-payment/reversal example in Lesson 4. | A dated balance table showing why today's balance is insufficient |
| 14 | Compare termination preview and payroll's shared base calculation. | Inputs they share and behavior the preview deliberately does not perform |
| 15 | Follow two-approver expense policy from submission to payroll. | A state table including submitter, first vote, duplicate vote, and second vote |
| 16 | Trace payroll simulation and creation to the same calculator. | Which method reads, which stages writes, and which commits |
| 17 | Change an input in a paper example after a payslip is created. | Why saved calculation evidence still explains the old result |
| 18 | Read the completion-receipt failure test. | A lost-response retry timeline and an invoice/receipt rollback timeline |

Try teaching one example to someone without using “idempotency” or “concurrency.” Then explain the same example using those words precisely. For example: “A repeated completed request returns the earlier result” explains the user promise. “A receipt keyed by owner, operation, and reference is committed with the result” explains its mechanism.

## Sessions 19–24: reason about process failure and historical views

| Session | Read and do | Evidence to leave in your journal |
| --- | --- | --- |
| 19 | Allocate one cent between three centers in Lesson 7. | Floors, remainders, winner of the residual cent, and reconciled total |
| 20 | Walk the CSV parser through a quoted comma and quote escape. | Parser state after each character of a short field |
| 21 | Compare an import preview with a committed batch. | A competing-email example proving why commit rechecks |
| 22 | Read the event-save rollback and notification dispatcher tests. | A crash before event commit versus a crash after event commit |
| 23 | Draw worker A's expired lease and worker B's new lease. | Why A's final fence fails even if A already prepared valid output |
| 24 | Compare document recording dates, roster capture time, and payroll periods. | Three different meanings of “as of,” each tied to an actual query |

For an independent capstone, choose one existing feature and propose one bounded extension without coding immediately. Examples: an additional preset-supported report kind, a new notification title template for a newly audited action, or another export metadata field that is already within the authorized client's scope. Write the input contract, permission rule, persistence boundary, failure behavior, and tests first. Check whether the proposed extension changes any current promise.

## How to assess understanding without guessing your level

For a chosen story, rate each question from 0 to 2: 0 means you need a full example, 1 means you can answer with the source open, and 2 means you can answer accurately before checking the source.

1. Can you describe the user's new behavior with one concrete example?
2. Can you identify the owner and explain a foreign-ID response?
3. Can you predict invalid input and stale-version behavior?
4. Can you separate current values, saved snapshots, and estimates?
5. Can you explain what survives a failed save or retry?
6. Can you name a meaningful test and state whether it actually passed?

Use the pattern of answers to choose your next session. A low score on ownership suggests another scoped read exercise. A low score on failure behavior suggests a rollback test and a transaction drawing. A high score on one story does not establish a job title; repeat the reasoning on an unfamiliar feature to see whether the skill transfers.

## A useful journal entry is specific

Prefer this:

> I predicted the first import worker would remain after the second insert failed. The test reloads the database and finds neither. Both inserts and the batch status share one transaction. Tomorrow I will predict the outcome when validation succeeds but another request claims an email before commit.

Over this:

> Learned transactions today.

The specific entry contains a prediction, evidence, a corrected model, and a next experiment. After several sessions, reread your earlier predictions. Improvements become visible in your own words.

## Use verification evidence carefully

Run `python scripts/bootcamp.py test SS-18` to invoke the registered focused suite when the local runtime permits test execution. The command preserves the test process's exit code; it does not automatically mark the feature verified. Consult the progress register and journal for the evidence gathered during implementation, including the Windows Application Control assembly-loading blockage.

When a command fails, identify its stage: SDK discovery, compilation, assembly loading, test discovery, test execution, or assertion. A build pass cannot replace a behavioral test. A blocked assembly cannot prove a business rule is wrong. Learning to classify the failure correctly is part of learning to debug.
