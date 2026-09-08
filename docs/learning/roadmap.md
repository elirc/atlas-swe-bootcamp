# Roadmap: build independence, then judgment

Use milestones instead of a promised promotion deadline. A possible rhythm is three sessions per week: read/trace, implement/test, explain/review. A session can be 45–90 minutes. Spend longer where the evidence is weak.

| Milestone | Suggested lessons | Scope | Gate before moving on |
| --- | --- | --- | --- |
| 1. Find your way | 00–01 | One request, one class, one test | Explain input → rule → persistence → response |
| 2. Deliver a small fix | 02–05 | One bug with a clear expected result | Reproduce it, fix it, prove the boundary and failure case |
| 3. Own a feature | 04–07 | Several layers and existing callers | Define acceptance criteria, preserve auth, document behavior |
| 4. Handle failure | 08–09 | Races, retries, dependency outages | Explain what persists after failure and how you would diagnose it |
| 5. Make a design decision | 10 | An ambiguous requirement | Compare alternatives, scope delivery, plan rollback and monitoring |
| 6. Help another engineer | All | Review and teach | Give actionable feedback and improve another person's reasoning |

## A suggested first twelve sessions

1. Run a filtered test and trace `/api/countries`.
2. Explain `required`, `decimal`, `DateOnly`, and an async query in your own words.
3. Call a report with no key, a viewer key, and an admin key; explain each outcome.
4. Draw the contract state machine; test an invalid transition.
5. Predict a payroll rounding boundary, then run its test.
6. Inspect one EF query and identify where execution begins.
7. Reproduce an invalid report period and examine the field-specific error.
8. Walk through the report extraction; distinguish a refactor from a behavior change.
9. Test an invoice issued at a UTC day boundary.
10. Trace two competing updates and identify the database check that rejects one.
11. Use a request ID to explain an error response and its log.
12. Submit one backlog task with a short design note, tests, and self-review.

This sequence is a starting schedule, not an assessment of your current ability. Skip a session only if you can already produce its evidence.

## What increasing responsibility looks like

For a report-filter bug, a junior engineer can implement the specified validation with guidance. A mid-level engineer can discover related boundary cases, preserve the API contract, and deliver the fix independently. A senior engineer can also assess who relies on the old behavior, choose a consistent policy across endpoints, evaluate operational cost, and help others apply the lesson.

These are overlapping behaviors, not job-title guarantees. Evaluate your work across several changes, including ones with incomplete requirements and reviewer disagreement.

## A practical assessment

Pick an unfinished [backlog task](practice-backlog.md). Spend the first session writing examples and risks without coding. In the next session implement a small slice. In the third, have someone review it and respond with either a fix or a reasoned explanation.

You pass a milestone when you can explain the result, reproduce the evidence, and address review feedback. If a test passes but you cannot explain what defect it catches, repeat the exercise with different data.
