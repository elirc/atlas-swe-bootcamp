# 10: Own a change from requirement to review

**Outcome:** turn an ambiguous feature request into a bounded, reviewable delivery. Read the [ADRs](../adr/README.md), [review findings](../review-and-improvement.md), and [practice backlog](practice-backlog.md).

## Ask questions that change implementation

“Add invoice payments” hides decisions. Who can record one? Are partial payments permitted? In what currency? Can payments be corrected? What makes retries safe? Does aging use issue date or due date? Which date controls the historical balance?

Write concrete examples before tables or classes. For an invoice of 100 USD, a 40 USD payment might leave 60 USD outstanding. A repeated submission might return the first result. A 40 EUR payment needs an explicit policy, not an implicit sum. These are proposed requirements until agreed; Atlas does not yet implement payment tracking.

## Compare alternatives against constraints

A mutable `PaidTotal` field is small but may lose evidence needed for corrections. An append-only payment ledger preserves individual events but requires aggregation and reversal rules. An external payment integration adds reconciliation and failure modes. Choose based on the requested capability, audit needs, and operational capacity.

Use a short ADR: context, considered options, decision, consequences, and a trigger to revisit it. Explain the cost you accept. Avoid importing queues, microservices, or generic repositories merely because senior engineers sometimes use them.

## Plan compatibility and rollout

Identify existing callers, persisted data, and operational assumptions. Atlas currently uses `EnsureCreated` for Development and has no migration workflow. Adding a table needs a database evolution decision; `EnsureCreated` does not upgrade an existing schema. The report improvements in this revision do not change that schema.

For a real schema change, outline deployment order, old/new code compatibility, data backfill, verification, and recovery. “Rollback the binary” may not reverse a destructive data transformation.

## Prepare a reviewable PR

Lead with the problem and resulting behavior. Include an example, relevant validation, and material limitations. Separate proven failures from style preferences when reviewing someone else's work.

A good report PR description might say: “A month-only filter was silently ignored. Period filters now reject missing years and reversed ranges with field-specific 400 responses. Existing report shapes and platform authorization remain intact. Regression tests cover invalid bounds and the original aggregation behavior.”

## Exercise: a one-page design

Choose one capstone from the backlog. Write the user problem, acceptance examples, data changes, authorization, failure handling, alternatives, rollout, and observability. Then deliberately reduce the first implementation to one useful vertical slice.

Have a reviewer challenge one assumption. Respond with evidence or revise the design. Record what you learned in the journal. This feedback loop practices senior-level judgment more directly than adding another abstraction.

**Completion check:** can another engineer implement the slice from your examples? Can operations detect failure? Can you explain what you chose not to implement yet and the requirement that would justify it?
