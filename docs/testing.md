# Testing

One xUnit project, `tests/Atlas.Tests`, holds the whole suite with no external
service setup. Run it to obtain the current count and duration; these depend on
the checkout and machine.

## Running

```bash
dotnet test                                   # everything
dotnet test --filter "FullyQualifiedName~Atlas.Tests.Domain"       # unit only
dotnet test --filter "FullyQualifiedName~Atlas.Tests.Integration"  # API only
dotnet test --filter "FullyQualifiedName~PayrollEdgeCaseTests"     # one class
```

## Taxonomy

### `Domain/` — pure unit tests

No database, no HTTP; entities and calculators exercised directly.

| Area | Files |
| --- | --- |
| Payroll math + rounding | `PayrollCalculatorTests`, `FinalPayCalculatorTests`, `FinalPayProrationBoundaryTests`, `MoneyInvariantTests` |
| Period selection | `SalaryRecordTests`, `FxRateTests`, `EffectivePeriodBoundaryTests` |
| Lifecycles | `EmploymentContractTests`, `LeaveRequestTests`, `ExpenseClaimTests`, `ContractAmendmentTests`, `TerminationRequestTests`, `BenefitTests`, `LifecycleTransitionMatrixTests` |
| Other domain rules | `LeaveCalculatorTests`, `ComplianceDocumentTests` |

Conventions worth copying:

- **Exact-message assertions**: invalid transitions assert the full
  `DomainException` message, because the API returns it verbatim as the 409
  `detail` — the message *is* API surface.
- **Property-style sweeps**: `MoneyInvariantTests` iterates grids of amounts
  and rates asserting invariants (cent conservation, 2-dp scale) rather than
  single examples; `FinalPayProrationBoundaryTests` sweeps every day of a
  leap year.

### `Infrastructure/` — EF-level tests

`DateTimeOffsetConverterTests` proves the UTC-ticks converter orders,
filters, and round-trips instants correctly *in SQL*; `DataSeederTests`
pins the dev seed (populates every aggregate, idempotent, active contracts
have completed onboarding).

### `Integration/` — full-stack API tests

Boot the real host and talk HTTP. Coverage per feature
(`ContractEndpointTests`, `PayrollEndpointTests`, `LeaveEndpointTests`, …)
plus cross-cutting suites:

- `AuthorizationTests` / `AuthorizationMatrixTests` — the role × endpoint ×
  ownership matrix: 401s, platform-only 403s, viewer-write 403s,
  cross-client 404s (reads *and* writes), list scoping.
- `ApiHardeningTests` / `ProductionReadinessTests` — malformed JSON,
  ProblemDetails shapes, pagination headers and limits, health probe,
  request logging, stale-version conflicts.
- `ConcurrencyEdgeTests` — stale `Version` writes, double decisions,
  decide-after-state-change races.
- `PayrollEdgeCaseTests`, `BoundaryValidationTests` — month-edge and
  leap-year payroll, amendment/FX effective-date boundaries, leave window
  edges, exact-fit balances.
- `FullJourneyTests` — one end-to-end walk (country → client → worker →
  contract → onboarding → compliance → payroll → invoice) through the
  public API only.

## The integration harness

`AtlasApiFactory` (in `Integration/`) is a `WebApplicationFactory<Program>`
that:

1. **Rebinds the database** — removes the app's `AtlasDbContext` registration
   and substitutes an **in-memory SQLite** connection
   (`DataSource=:memory:`) that the factory opens and holds for its
   lifetime; the schema lives exactly as long as the connection, and
   `EnsureCreated()` builds it once per factory.
2. **Seeds an admin key** — a `PlatformAdmin` `ApiUser` with key
   `test-platform-admin-key`, attached as `X-Api-Key` to every
   `CreateClient()` by default. `CreateClientWithApiKey(key)` swaps in a
   scoped key (or none, for 401 tests).
3. **Exposes the database** — `WithDb(db => …)` runs an action in a fresh
   DI scope, used for test setup the API deliberately refuses (e.g. bulk
   completing onboarding items) and for direct-DbContext concurrency tests.

Isolation rules that keep the suite deterministic:

- Each test **class** is an `IClassFixture<AtlasApiFactory>` — one private
  database per class, shared across that class's tests.
- Anything globally unique gets a per-test value: payroll tests create a
  **private country per test** (runs are unique per country+month), FX tests
  use a **private currency pair per test** (rates are global), and emails
  carry unique prefixes.
- No test depends on execution order; setup that must exist is created
  idempotently in the constructor (guarded by existence checks).

Because the real authentication handler, authorization policies, exception
handlers, and SQLite engine are in the loop, these tests validate exactly
what a client would observe — status codes, ProblemDetails bodies, headers,
and money amounts.

## Learn from the tests

Start with [testing and debugging](learning/05-testing-and-debugging.md). New worked examples:

- `ReportQueryTests`: field-specific period validation, representable date windows,
  explicit/default report dates, UTC issue boundaries, exact aging totals, and currency separation.
- `OperationalDiagnosticsTests`: readiness outages with healthy liveness, final status logging,
  shared request IDs, and ProblemDetails for handled failures.

These classes create isolated factories. Their test clocks affect report defaults only;
the rest of the application's timestamp creation has not been migrated to `TimeProvider`.

```powershell
dotnet test Atlas.slnx --filter "FullyQualifiedName~ReportQueryTests|FullyQualifiedName~OperationalDiagnosticsTests"
python scripts/learn.py test 09
python scripts/learn.py check-docs
```

The Python helper is optional (3.10+, standard library only). `check-docs` verifies local
inline Markdown destinations in README, docs, and astradocs; it does not validate external URLs,
heading anchors, or every possible Markdown extension.

## Continuous integration

[The workflow](../.github/workflows/ci.yml) restores, builds Release, runs the complete
suite, and checks local documentation links. SDK selection is constrained to stable
.NET 10 feature bands by `global.json`. The action configuration follows the official
[setup-dotnet](https://github.com/actions/setup-dotnet) and
[checkout](https://github.com/actions/checkout) usage. Run the same build/test commands
locally before reviewing a change; a configured workflow is not evidence that a remote run passed.
