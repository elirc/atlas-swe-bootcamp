# 0010: Explicit report dates and separate query orchestration

Status: accepted.

## Context

Report endpoints mixed HTTP concerns with query construction and aggregation. Current-date reads made calendar tests depend on the day they ran. Period filters could be ignored or overflow arithmetic, and expiry windows could exceed DateOnly's representable range.

## Decision

Keep authorization and binding in `ReportEndpoints`; validate query inputs before arithmetic; resolve optional dates with `TimeProvider`; pass explicit dates and cancellation into an API-local `ReportService`. Use read-only projections for payroll costs and invoice aging. Keep existing DTO names and JSON shapes.

The service belongs to the API because it produces that host's read models. Do not make Infrastructure depend on API or add an interface solely to mirror one implementation. Extract a shared reporting layer if another consumer establishes that need.

Invoice aging includes issues through the end of the chosen UTC day and excludes later issues. Compliance evaluates current document rows relative to the chosen day. Neither is a historical snapshot system. Existing currency grouping and default compliance windows are preserved.

## Consequences

Queries can be understood separately from transport, and tests can choose exact dates. Invalid previously ignored periods now return 400. Decimal aggregation still occurs in memory after filtering and projection, so large reports remain a scaling concern. No schema migration is needed for this change.

Revisit when reports need bounded asynchronous export, another host, audited historical results, or a different database provider. Measure query cost before claiming a performance improvement.
