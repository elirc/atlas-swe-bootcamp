# Learn engineering through Atlas

If you want a gentler introduction to the repository before these lessons, use
[AstraDocs](../../astradocs/README.md). It repeats the foundations through different
examples and formats, with short checkpoints and explained answers.

Atlas is your practice backend for moving from junior implementation work toward independent delivery and senior engineering judgment. You do not need to understand payroll before starting. Treat its rules as a realistic business specification, not as real payroll or tax guidance.

The aim is evidence: explain a request, reproduce a defect, ship a tested change, and defend a tradeoff. Completing a reading list alone does not establish seniority. Senior work also involves sustained ownership, collaboration, and decisions under real constraints.

## Start with one small win

1. Follow [your first session](00-first-session.md). Get the API running, observe a successful response and a rejected request, and trace one test.
2. Read [C# through this codebase](01-csharp-foundations.md) alongside the source. Stop at unfamiliar syntax and try the exercises.
3. Use the [roadmap](roadmap.md) to choose your next milestone. Repeat a milestone until you can explain your work without copying the walkthrough.
4. Record your reasoning in the [progress journal](progress-journal.md). Ask for review of a diff and its evidence, rather than a score based on how much you read.

## Curriculum

| Lesson | What you will practice | Evidence you produce |
| --- | --- | --- |
| [00: First session](00-first-session.md) | Run, inspect, predict, debug | One traced request |
| [01: C# foundations](01-csharp-foundations.md) | Types, nullability, collections, async | Explain and alter a small rule |
| [02: HTTP and authorization](02-http-and-authorization.md) | Binding, validation, role and tenant checks | A request/response matrix |
| [03: Domain modeling](03-domain-modeling.md) | State machines, invariants, money | Transition and boundary tests |
| [04: SQL and EF Core](04-sql-and-ef-core.md) | Query execution, tracking, projections, indexes | SQL evidence and a query comparison |
| [05: Testing and debugging](05-testing-and-debugging.md) | Reproduction, test selection, failure isolation | A regression test and diagnosis |
| [06: Safe refactoring](06-safe-refactoring.md) | Responsibilities, dependencies, compatibility | A focused refactor with preserved behavior |
| [07: Time and reporting](07-time-and-reporting.md) | Explicit dates, calendars, report semantics | Reproducible boundary examples |
| [08: Concurrency and reliability](08-concurrency-and-reliability.md) | Stale writes, transactions, retries | A race timeline and failure policy |
| [09: Operations and diagnostics](09-operations-and-diagnostics.md) | Middleware, request IDs, readiness | An incident investigation |
| [10: Design and delivery](10-design-and-delivery.md) | Requirements, tradeoffs, rollout, review | A design note and reviewable PR |
| [Worked changes](worked-changes.md) | Read the improvements in this revision | Explain why the previous version failed |
| [Practice backlog](practice-backlog.md) | Increasingly independent implementation | Small PRs with acceptance evidence |
| [Review findings](../review-and-improvement.md) | Separate demonstrated defects from open risks | A prioritized next change |

Each lesson includes an anchor in the actual source, a concrete exercise, and a check for understanding. The [reference docs](../architecture.md) describe the system; this curriculum teaches you how to reason about changing it.

## Optional study helper

With Python 3.10+ installed, run these from the repository root:

```powershell
python scripts/learn.py doctor
python scripts/learn.py list
python scripts/learn.py show 00
python scripts/learn.py test 07
python scripts/learn.py test all
python scripts/learn.py check-docs
```

The helper has no Python package dependencies. It prints lessons, runs the existing .NET tests, and checks local inline documentation links. It preserves test failure exit codes and can find a user-local SDK under `.dotnet` if it is absent from PATH. It does not track completion automatically; record your understanding in the journal. You can also use every lesson with ordinary `dotnet test` commands and Markdown files.

## How to use a mentor or assistant

Use this prompt when you need help:

> I am on Atlas lesson __. My prediction was __. I ran __ and observed __. Here is my diff or error. Ask me to explain the next step, then give one hint. Do not provide the full solution unless I ask.

After you finish, ask: “Review my change for correctness, authorization, data consistency, and clarity. Separate defects from preferences. Give me one follow-up exercise based on my weakest explanation.”

Read worked solutions after making a prediction. For independent practice, the backlog intentionally specifies outcomes without implementing every solution.
