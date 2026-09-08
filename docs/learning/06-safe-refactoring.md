# 06: Refactor around a responsibility you can name

**Outcome:** improve code structure while recognizing and documenting intentional behavior changes. Compare [ReportEndpoints.cs](../../src/Atlas.Api/Endpoints/ReportEndpoints.cs), [ReportService.cs](../../src/Atlas.Api/Reporting/ReportService.cs), and [ADR 0010](../adr/0010-report-query-boundaries.md).

## Start with the reason to change

The original report endpoint class combined route mapping, parameter validation, SQL construction, and result grouping. The extraction gives these concerns clear homes:

| Responsibility | Home | Example |
| --- | --- | --- |
| HTTP policy and binding | Endpoint | Platform-admin group; optional `asOf` |
| Input validity | Query validation helper | Reject a month without a year |
| Read orchestration | Report service | Filter and project invoice rows |
| Business state and calculations | Domain | Payroll math; document status |
| Persistence mapping | DbContext | UTC ticks; keys and relationships |

The report service stays inside the API project because it creates API-specific read models. Moving it into Infrastructure would require moving or mapping those models to avoid an Infrastructure → API dependency. That extra boundary is not needed for the current use case. Revisit it if another host needs these reports.

No interface was added merely to mirror one concrete class. Test the behavior at HTTP/SQLite boundaries; inject the actual variable dependency, time. The service remains scoped with the DbContext, so a singleton does not hold a request-scoped context.

## Separate preservation from new behavior

Extracting a query is a refactor if callers observe the same result. Rejecting orphaned months and excluding invoices issued after an `asOf` date are behavior changes. They need explicit acceptance examples and documentation even when implemented in the same feature branch.

Preserve route names, authorization, JSON property names, grouping by currency, and default report windows. An “internal cleanup” that accidentally removes a tenant filter is a security defect.

## Exercise: review the extraction

Sketch the old responsibilities from the version-control diff. For each moved block, find the equivalent code and the existing test that protects it. Then list intentional changes separately. Read the [worked changes](worked-changes.md) only after writing your own explanation.

Propose one further refactor with a single reason to change. State why it is worth the extra file or abstraction. If its only benefit is shorter methods, ask whether names and local structure could do the job instead.

**Completion check:** why is “more layers” not an engineering objective? Which compatibility promises are visible to an HTTP caller? What new evidence would make a shared reporting library worthwhile?

Next: [time and reporting](07-time-and-reporting.md).
