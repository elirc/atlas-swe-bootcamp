# 09: Make a failure understandable

**Outcome:** connect an HTTP failure to evidence and distinguish an unhealthy dependency from a process that needs restart. Read [Program.cs](../../src/Atlas.Api/Program.cs), [RequestLoggingMiddleware.cs](../../src/Atlas.Api/RequestLoggingMiddleware.cs), and [OperationalDiagnosticsTests.cs](../../tests/Atlas.Tests/Integration/OperationalDiagnosticsTests.cs).

## Middleware runs inward, then outward

In the previous order, exception handling wrapped logging. When an endpoint threw, the logger's `finally` ran while the response could still be 200; the outer exception handler set 409 or 500 afterward. The revised logger wraps exception handling, so its completion log sees the translated response. ASP.NET's [middleware documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/middleware?view=aspnetcore-10.0) explains the reverse response order.

Draw `logger → exception handler → status pages → auth → endpoint`, then trace an exception outward. The regression test injects a throwing clock into an existing report route and captures the real middleware's logs. No test-only endpoint is shipped.

## Correlate evidence without logging secrets

Responses include `X-Request-Id`, and generated ProblemDetails include the same ID as `traceId`. The completion log includes that ID with method, path, status, and elapsed time. This is a per-request identifier, not a complete distributed tracing system. The middleware does not trust a caller-supplied `X-Request-Id` as the server's identifier.

Do not add API keys, request bodies, or payroll data to general request logs to make debugging easier. For an incident, record the request ID, route, time, observed status, expected behavior, and relevant sanitized evidence.

## Ask each probe the right question

| Probe | Question | Current behavior |
| --- | --- | --- |
| `/health/live` | Can the host respond? | Runs no dependency checks |
| `/health/ready` | Does the tagged database connectivity check pass? | Fails with 503 on unhealthy readiness checks |
| `/health` | Does the original health check set pass? | Retains the existing database-backed behavior |

All are anonymous. A dependency outage should fail readiness while liveness stays healthy, avoiding restart loops that cannot repair the dependency. This follows ASP.NET's [separate health-probe guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).

The current database check proves connectivity, not schema correctness, successful payroll writes, or acceptable latency. Reports are not paginated exports. Readiness and a passing test suite are useful evidence, not proof of production capacity.

## Exercise: incident drill

Run the dependency-failure integration test. Explain why `/health` and `/health/ready` return 503 but `/health/live` returns 200. Then run the exception-log test and trace the request ID through body, header, and captured log.

Write a five-line incident note: impact, evidence, current hypothesis, immediate mitigation, and follow-up test. Separate observation from speculation. Include what evidence would disprove your hypothesis.

Next: [design and delivery](10-design-and-delivery.md).
