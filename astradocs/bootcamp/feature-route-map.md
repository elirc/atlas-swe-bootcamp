# Feature route map

Use this map to find a starting file, then follow one request into the domain and database. The [75-story register](progress.md) records verification evidence. The original story workbooks contain the detailed acceptance criteria; this page helps navigate their implementations.

All paths below are relative to the API root. A manager is a platform administrator or the client administrator for the visible client. Scoped readers include that client's viewer. Import batches, saved-report presets, and data exports additionally enforce API-identity ownership. Read [the API labs](api-labs.md) and [senior labs](senior-api-labs.md) for request bodies and expected outcomes.

## Junior changes: JS-01 through JS-25

The junior stories improve existing Country, Client, Worker, Contract, Invoice, Payroll, FX, Onboarding, Compliance, salary-history, and report routes. They add bounded inputs, defined-enum checks, literal name-prefix filters, stable ordering, period filters, parent-child checks, and consistent date handling. They are deliberately small entry points into the codebase.

Start with [Lesson 1](01-http-boundaries-and-query-composition.md), [ApiInput](../../src/Atlas.Api/ApiInput.cs), and [JuniorStoryTests](../../tests/Atlas.Tests/Integration/JuniorStoryTests.cs). Open the specific story with `python scripts/bootcamp.py show JS-01`, replacing the ID as needed. Find its named test, predict the response, and then follow the endpoint. The [API reference](../../docs/api-reference.md) describes the original routes and added filters.

## Mid-level features: MS-01 through MS-30

| Stories | Route or operation | Start reading | Focused test suite |
| --- | --- | --- | --- |
| MS-01, 02, 03, 14 | Client workforce-summary; worker employment-timeline; contract activation-readiness; client leave-calendar | [WorkforceViewEndpoints](../../src/Atlas.Api/Endpoints/WorkforceViewEndpoints.cs) | MidlevelViewStoryTests |
| MS-04, 05, 06, 30 | Batch onboarding completion; contract notes; country onboarding-template; hiring-status | [OnboardingWorkflowEndpoints](../../src/Atlas.Api/Endpoints/OnboardingWorkflowEndpoints.cs) | WorkflowStoryTests |
| MS-07, 08, 09, 10 | Payslip list/detail/CSV; invoice breakdown/CSV | [FinancialViewEndpoints](../../src/Atlas.Api/Endpoints/FinancialViewEndpoints.cs) | MidlevelViewStoryTests |
| MS-11, 12, 13 | Invoice payments, reversals, balance; payment-aware aging | [InvoiceLedgerEndpoints](../../src/Atlas.Api/Endpoints/InvoiceLedgerEndpoints.cs), [InvoiceLedgerService](../../src/Atlas.Infrastructure/InvoiceLedgerService.cs) | WorkflowStoryTests |
| MS-15, 16, 17, 18 | Leave quote; cancel approved leave; country holidays; allowance adjustments | [LeaveWorkflowEndpoints](../../src/Atlas.Api/Endpoints/LeaveWorkflowEndpoints.cs), [LeaveService](../../src/Atlas.Infrastructure/LeaveService.cs) | WorkflowStoryTests |
| MS-19, 20 | Replace pending expense; withdraw pending expense | [ExpenseWorkflowEndpoints](../../src/Atlas.Api/Endpoints/ExpenseWorkflowEndpoints.cs) | ProposalAndExpenseStoryTests |
| MS-21, 22, 23, 29 | Expense summary; benefit quote/calendar; client approval queue | [OperationsViewEndpoints](../../src/Atlas.Api/Endpoints/OperationsViewEndpoints.cs) | MidlevelViewStoryTests |
| MS-24, 25 | Termination and amendment previews | [ContractPreviewEndpoints](../../src/Atlas.Api/Endpoints/ContractPreviewEndpoints.cs), [ContractProposalService](../../src/Atlas.Infrastructure/ContractProposalService.cs) | ProposalAndExpenseStoryTests |
| MS-26, 27 | API-key expiry and immediate replacement | [ApiUserEndpoints](../../src/Atlas.Api/Endpoints/ApiUserEndpoints.cs), [authentication](../../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs) | WorkflowStoryTests |
| MS-28 | Owner-scoped typed report presets and execution | [ReportPresetEndpoints](../../src/Atlas.Api/Endpoints/ReportPresetEndpoints.cs) | ProposalAndExpenseStoryTests |

Read Lessons 2–5 alongside these features. The repeated themes are saved facts versus current views, a complete validation pass before mutation, a shared parent version for competing writes, and explicit request replay rules.

## Senior features: SS-01 through SS-20

| Stories | Public entry points | Start reading | Focused test suite |
| --- | --- | --- | --- |
| SS-01 | `POST /api/payroll-simulations` | [PayrollLearningEndpoints](../../src/Atlas.Api/Endpoints/PayrollLearningEndpoints.cs), [PayrollAssessmentService](../../src/Atlas.Infrastructure/PayrollAssessmentService.cs) | PayrollReliabilityStoryTests |
| SS-02 | `GET /api/payslips/{id}/calculation-evidence` | [PayrollLearningEndpoints](../../src/Atlas.Api/Endpoints/PayrollLearningEndpoints.cs), [PayrollAssessment](../../src/Atlas.Domain/Services/PayrollAssessment.cs) | PayrollReliabilityStoryTests |
| SS-03 | `GET /api/reports/payroll-variance` | [PayrollLearningEndpoints](../../src/Atlas.Api/Endpoints/PayrollLearningEndpoints.cs) | PayrollReliabilityStoryTests |
| SS-04 | Existing payroll completion with optional `Idempotency-Key` | [PayrollCompletion](../../src/Atlas.Api/PayrollCompletion.cs) | PayrollReliabilityStoryTests |
| SS-05 | Payroll job submission, polling, explicit retry | [PayrollJobEndpoints](../../src/Atlas.Api/Endpoints/PayrollJobEndpoints.cs), [DurableJobService](../../src/Atlas.Infrastructure/DurableJobService.cs) | PayrollReliabilityStoryTests |
| SS-06 | Invoice credits and credit-summary; credited-invoice-aging | [InvoiceLedgerEndpoints](../../src/Atlas.Api/Endpoints/InvoiceLedgerEndpoints.cs) | WorkflowStoryTests |
| SS-07, 08 | Client billing-terms, fee-schedule; invoice due-status | [BillingConfigurationEndpoints](../../src/Atlas.Api/Endpoints/BillingConfigurationEndpoints.cs) | WorkflowStoryTests |
| SS-09, 10 | Client cost-centers; contract cost-allocations; payslip cost-allocations; client cost-center-payroll | [CostAllocationEndpoints](../../src/Atlas.Api/Endpoints/CostAllocationEndpoints.cs), [CostAllocation](../../src/Atlas.Domain/Services/CostAllocation.cs) | AdvancedStoryTests |
| SS-11 | Worker-import validate, detail, commit | [WorkerImportEndpoints](../../src/Atlas.Api/Endpoints/WorkerImportEndpoints.cs), [WorkerInput](../../src/Atlas.Domain/Services/WorkerInput.cs) | AdvancedStoryTests |
| SS-12, 13 | Document renew/renewal-history; country document-policy; contract document-coverage | [ComplianceWorkflowEndpoints](../../src/Atlas.Api/Endpoints/ComplianceWorkflowEndpoints.cs) | ComplianceConfigurationStoryTests |
| SS-14, 15 | Client payroll-budgets/payroll-budget-report/contract-field-definitions; contract custom-fields | [ClientConfigurationEndpoints](../../src/Atlas.Api/Endpoints/ClientConfigurationEndpoints.cs) | ComplianceConfigurationStoryTests |
| SS-16, 17 | Client and contract decision feeds; client notifications and personal read markers | [ContractDecisionEndpoints](../../src/Atlas.Api/Endpoints/ContractDecisionEndpoints.cs), [ContractDecisionWriter](../../src/Atlas.Api/ContractDecisionWriter.cs), [NotificationService](../../src/Atlas.Infrastructure/NotificationService.cs) | AdvancedStoryTests |
| SS-18 | Client data-export submission, polling, download | [ClientExportEndpoints](../../src/Atlas.Api/Endpoints/ClientExportEndpoints.cs), [export processing](../../src/Atlas.Infrastructure/DurableJobService.Exports.cs) | ExportStoryTests |
| SS-19 | Client workforce-snapshot capture/list/detail/compare | [WorkforceSnapshotEndpoints](../../src/Atlas.Api/Endpoints/WorkforceSnapshotEndpoints.cs) | AdvancedStoryTests |
| SS-20 | Client expense-approval-policy; votes through existing approve route | [ExpenseWorkflowEndpoints](../../src/Atlas.Api/Endpoints/ExpenseWorkflowEndpoints.cs), [ExpenseApprovalService](../../src/Atlas.Infrastructure/ExpenseApprovalService.cs) | ProposalAndExpenseStoryTests |

## Follow a feature through five questions

1. **Who owns it?** Find the caller's client or API-identity scope before studying the filter.
2. **What is accepted?** Find input bounds, normalization, and state requirements.
3. **What is calculated?** Separate pure arithmetic from database lookups and saved evidence.
4. **What commits together?** Identify the transaction, shared version, and unique constraints.
5. **What proves it?** Read one success test and one failure test, then check the register for whether those tests actually ran successfully.

The schema lives in [AtlasDbContext](../../src/Atlas.Infrastructure/AtlasDbContext.cs), [bootcamp mappings](../../src/Atlas.Infrastructure/AtlasDbContext.Bootcamp.cs), and [advanced mappings](../../src/Atlas.Infrastructure/AtlasDbContext.Advanced.cs). These partial files form one context and one EF model. They are split to make the larger mapping readable, not to create separate databases.

## Practical boundaries of this learning build

The repository uses `EnsureCreated` with a fresh SQLite schema. It does not upgrade an existing database. Use a new disposable database file for the new features; preserve any existing file. A migration and production rollout require separate work.

Background processing is enabled outside Testing unless its configuration switch disables it. Payroll and exports share `Jobs:Enabled`; inbox dispatch uses `Notifications:Enabled`. Development initialization enables SQLite WAL for concurrent read snapshots and lease renewal. A non-Development database used for background work must be provisioned with the same schema and WAL configuration; startup does not create a production schema for you.

Job limits, export caps, metadata-only document assessment, and API-identity-based approvals are deliberate boundaries in the stories. Passing a bounded feature test is evidence for that behavior, not a general production-readiness claim. The journal records the build results, test failures, and machine-level test-loading limitation encountered during implementation.
