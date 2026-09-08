# Review focused on learning and engineering judgment

This review concentrates on reporting, diagnostics, and how a learner navigates the repository. It is not a comprehensive security or production-readiness audit.

## What was already useful

The repository has clear Domain/Infrastructure/API projects, pure calculation tests, real SQLite integration tests, tenant authorization examples, concurrency tokens, and ADRs. Those are valuable learning anchors. The missing piece was a guided path from reading them to making and defending independent changes.

## Findings addressed

| Finding | Concrete consequence | Change / evidence |
| --- | --- | --- |
| Report periods accepted incomplete or unsupported values | Month-only filters were ignored; year arithmetic could overflow; reversed periods were not rejected | Field-specific validation; `ReportQueryTests` |
| Expiry windows checked negativity only | Large positive windows caused DateOnly overflow | Shared representable-range check on both expiry routes |
| Date-sensitive reports read current time directly | Difficult to reproduce a chosen date | Optional `asOf`, injected default clock, UTC boundary tests |
| Aging included all issues regardless of evaluation day | Future-issued invoices could appear in the youngest bucket | Inclusive UTC issue cutoff with populated regression data |
| Reports mixed HTTP and query responsibilities | Harder to follow, review, and safely extend | Endpoint/query-service separation; preserved existing report tests |
| Logging was inside exception translation | Escaping exceptions could be logged with a stale status | Logger wraps exception handling; actual status captured in tests |
| Custom exception handlers wrote ProblemDetails directly as JSON | Shared customization/content type was bypassed | Shared ProblemDetails service and correlation checks |
| Only one database-backed health endpoint existed | No independent liveness probe | Separate liveness/readiness with outage regression |
| Reference docs lacked a progression of exercises | Readers had little evidence of growing independence | Curriculum, worked changes, backlog, journal, local study helper |

## Open work worth learning from

- **Database evolution:** Development uses `EnsureCreated`; it does not upgrade existing schemas. Address migration and recovery before adding persistent payment or audit features.
- **Credential storage:** API keys are stored as plaintext values for direct lookup. Designing hashed verification and rotation is a separate, migration-sensitive feature.
- **Cross-row concurrency:** row versions reject stale updates to the same entity; they do not by themselves prove aggregate balance or overlap guarantees across competing inserts. Build a deterministic reproduction before selecting a fix.
- **Report scale:** SQL filtering and projection reduce unnecessary data, but report aggregation still materializes rows. No load benchmark establishes capacity. Consider explicit bounds or asynchronous exports from measured requirements.
- **Historical meaning:** `asOf` evaluates current rows. Compliance has no history of what was known at a past date; invoice aging has no payment ledger.
- **Time consistency:** this revision injects time at report boundaries, not every timestamp-producing operation.
- **Probe depth:** readiness checks connectivity rather than schema or successful business transactions.
- **Remaining validation:** enum parsing and other date arithmetic outside this scope deserve targeted boundary reviews; see the practice backlog.

These items are not all equal-priority production defects. Their importance depends on deployment and requirements. The [backlog](learning/practice-backlog.md) turns them into scoped exercises with observable acceptance criteria.

## Validation approach

Run the existing suite to protect established behavior, then exercise populated report boundaries and middleware failures. Validate documentation links and study-helper commands locally. The final test result should come from the current checkout, not a hard-coded count in a README.

Start learning with [your first session](learning/00-first-session.md); start reviewing the implementation with [worked changes](learning/worked-changes.md).
