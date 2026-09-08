# The same application, explained three ways

**One objective:** explain who Atlas serves and what its records mean.

## Version 1: a short story

Imagine a company called Acme wants to employ a person called Sam in another country. Atlas keeps track of the company, the worker, their employment agreement, required onboarding, and monthly pay information.

Acme is a **Client**. Sam is a **Worker**. Their agreement is an **EmploymentContract**. Its checklist contains **OnboardingItems**. A monthly calculation belongs to a **PayrollRun**. Sam's individual result is a **Payslip**. The amount billed to Acme is an **Invoice**.

The names in this story are explanatory; use the actual IDs returned by your own database when making requests.

## Version 2: a set of questions

| Business question | Record or code to inspect |
| --- | --- |
| Which countries can Atlas hire in? | `Country` |
| Which company is paying? | `Client` |
| Who is employed? | `Worker` |
| What salary and dates apply? | `EmploymentContract` and `SalaryRecord` |
| Are the required onboarding tasks finished? | `OnboardingItem` |
| What happened in this country's payroll month? | `PayrollRun` |
| What was one person's pay? | `Payslip` |
| What was billed to the company? | `Invoice` |

You can find these classes under [Domain/Entities](../src/Atlas.Domain/Entities). Open only `Client`, `Worker`, and `EmploymentContract` on your first pass.

## Version 3: a folder of paperwork

Think of a contract as an agreement in a folder, a payslip as a calculation sheet for a worker, and an invoice as a bill addressed to the company. This analogy helps distinguish the records. In the actual application they are related database rows, not physical documents.

The distinction matters: changing a current salary is different from rewriting an already-created payslip. Payroll runs store their calculated results. Later salary changes do not automatically rewrite those snapshots.

## What the other features add

Leave tracks time off. Expense claims track reimbursements. Benefits add employer and employee premium shares. Amendments record changed terms and salary history. Termination requests support ending employment. FX rates support billing in another currency. Reports summarize stored data.

You can postpone those features until the main story is familiar. The code models simplified business rules; learning this code is not the same as learning a country's real payroll requirements.

## Say it again

A client is a company. A worker is a person. A contract connects them. A payslip concerns the worker's pay. An invoice concerns the company's bill.

## Checkpoint

Explain why a payslip and an invoice are different, using your own example. Then find `ClientId` and `WorkerId` on `EmploymentContract`. The IDs connect the story to the code.

Next: [pictures and maps](02-pictures-and-maps.md).
