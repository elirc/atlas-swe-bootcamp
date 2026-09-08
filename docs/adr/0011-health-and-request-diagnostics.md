# 0011: Separate probes and correlate completed requests

Status: accepted.

## Context

The existing `/health` included database connectivity. A restart probe based on it could restart a responsive process during a dependency outage. Request logging was inside the exception handler, allowing the logger to observe the response before the handler translated an exception.

## Decision

Preserve `/health`, add `/health/live` with no dependency checks, and add `/health/ready` selecting checks tagged `ready`. Keep all three anonymous. The current readiness check establishes database connectivity only.

Place request logging outside error handling so completed handled failures have their final HTTP status in the log. Expose the server request identifier as `X-Request-Id`, include it in ProblemDetails as `traceId`, and include it in the completion log. Route custom exception handlers through the shared ProblemDetails service.

## Consequences

Operators can distinguish dependency readiness from a responding host and connect a caller's error to a log. This is request correlation, not end-to-end distributed tracing. Secrets and payloads are not added to request logs. Cancellation and responses whose headers already started require separate analysis; the completed-error guarantee does not imply that an aborted client received a response.

Tests inject a failing readiness check and a throwing report clock to demonstrate the real pipeline's behavior. Revisit probe depth when schema verification or other dependencies are needed.
