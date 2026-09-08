# 04: Understand the work your query asks for

**Outcome:** identify where a query executes, what it loads, and what evidence would justify optimizing it. Read [AtlasDbContext.cs](../../src/Atlas.Infrastructure/AtlasDbContext.cs) and [ReportService.cs](../../src/Atlas.Api/Reporting/ReportService.cs).

## The database is a separate execution engine

`db.Payslips.Where(...)` constructs an `IQueryable` expression. `ToListAsync` executes it. Filtering before materialization lets the database reduce the result. Filtering a list afterward transfers and allocates rows you may never need.

`Select` projects the needed columns. `Include` loads related entities. A report that needs a run's year and month can project those values through a navigation property without loading a full tracked `PayrollRun`. EF translates the navigation access as part of the query.

Tracking helps EF recognize entity changes for `SaveChanges`. Reporting does not need to modify those entities. The extracted report service uses no-tracking queries and, for payroll and aging, scalar projections. A scalar projection already avoids entity tracking; the explicit `AsNoTracking` also communicates the read-only intent. See Microsoft's [querying guidance](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).

## Measure before declaring success

The refactor reduces selected columns in several reports, but that is not a measured latency claim. Reports still materialize result sets and group monetary values in memory. A request over years of payroll can remain expensive.

Inspect SQL with EF logging or `ToQueryString()` while debugging the query before execution. Identify filters, joins, selected columns, and ordering. Record the number of rows returned. An index helps particular lookups; it adds storage and write work. Its existence alone does not prove a query uses it.

SQLite behavior is part of this application. The UTC-ticks converter makes timestamp comparisons queryable. Decimal aggregation remains explicit in memory. Do not replace decimals with floating-point values merely to make a provider translate a query.

## Exercise: a query investigation

Choose `PayrollCostsAsync`. Compare an unfiltered query with a one-client, one-period query on the same test data. Capture SQL and returned row counts. Explain which part is SQL and which part is C# after `ToListAsync`.

Write a proposed index and the exact query it would support. Inspect a query plan before and after on representative local data. Report the dataset size and conditions; do not generalize from a handful of rows to millions.

**Completion check:** why can a query with one SQL round trip still be expensive? When could `Include` be appropriate? What does the cancellation token accomplish if the caller disconnects during a read?

Next: [testing and debugging](05-testing-and-debugging.md).
