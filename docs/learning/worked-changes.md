# Worked examples from this revision

Read this after predicting the behavior yourself. These are completed changes; the [practice backlog](practice-backlog.md) contains unfinished exercises.

## 1. A valid integer can still be an invalid date operation

**Trigger:** `/api/reports/compliance-expiries?withinDays=2147483647`.

**Before:** the endpoint checked only whether the window was negative, then called `today.AddDays(window)`. A sufficiently large positive value overflowed the date range and escaped as a server failure. The operational expiry route had the same pattern.

**After:** [ReportQueryValidation.TryCutoff](../../src/Atlas.Api/Reporting/ReportQueryValidation.cs) checks the representable days remaining from the selected date. Both routes reject an invalid window with a 400 on `withinDays`. A valid cutoff is calculated once and passed to query code.

**Why this fix:** clamping the value silently would answer a different query. Catching every exception and returning 400 would hide unrelated server defects. Validate the known invalid input before arithmetic.

**Evidence:** `InvalidWindows_Return400InsteadOfOverflow` covers huge, negative, and maximum-date inputs. `DateLimits_AreSupported` covers a valid zero-day window at the maximum date. The populated compliance test proves inclusion and status boundaries.

**Transfer:** apply the same reasoning to pagination multiplication, monetary precision, file-size arithmetic, and integer-to-enum binding. Check the operation's actual domain, not just the input type.

## 2. An optional filter can be silently ineffective

**Trigger:** `/api/reports/payroll-costs?fromMonth=7`.

**Before:** filtering occurred only when `fromYear` was supplied. The month was accepted but ignored. Years had no supported-range guard, reversed periods were not rejected, and an invalid `toMonth` was reported against `fromMonth`.

**After:** each side validates its own year/month, a supplied month requires a year, and complete bounds must be ordered. Year-only bounds remain useful: January for the start and December for the end. Period arithmetic happens after validation.

**Evidence:** `InvalidPeriods_ReturnFieldSpecific400` checks both the response code and error key. `ValidPeriods_AcceptInclusiveAndOpenBounds` protects supported combinations. Existing report tests verify actual payroll aggregation and filtering.

**Tradeoff:** callers that relied on ignored input now get 400. Document that tightening even though normal successful responses keep their JSON shape.

## 3. Test time without waiting for time

**Trigger:** finance wants to evaluate aging on a chosen date, and a test must run at a leap-day boundary on any day of the year.

**Before:** invoice aging always read the machine's date and included every invoice, including any future issue timestamps.

**After:** an optional `asOf` selects a UTC calendar date. The default comes from the registered clock. Query code receives the date as an ordinary parameter and excludes issues after the end of that day. Compliance reports also accept the date.

**Evidence:** a fixed clock test proves the default and explicit override; the populated aging test covers all four buckets, exact decimal totals, separate currencies, and a non-UTC input offset. See [ReportQueryTests.cs](../../tests/Atlas.Tests/Integration/ReportQueryTests.cs).

**Limit:** this is evaluation against current stored data, not an immutable historical reporting system. No payment state has been invented.

## 4. A correct error response can have a misleading success log

**Trigger:** a domain exception escapes a handler.

**Before:** the logger ran inside exception handling. Its `finally` executed before the outer handler changed the response status. Reading “responded 200” could mislead an incident investigation.

**After:** logging wraps error handling. Custom exception handlers use the shared ProblemDetails service, giving them consistent content type and request-ID customization. `X-Request-Id`, the problem's `traceId`, and the structured completion log agree.

**Evidence:** `HandledExceptions_LogFinalStatus_AndShareRequestIdWithProblem` injects domain, concurrency, and unexpected failures and verifies 409/409/500 plus correlation and error-detail redaction. The healthy-request logging test continues to cover 200.

**Transfer:** whenever middleware records results, draw the unwind order. A `finally` block runs reliably, but not necessarily after some outer component has finished changing the result.

## 5. Separate process health from dependency readiness

**Before:** `/health` was the only probe and included database connectivity. Using it for automatic restarts could restart healthy processes during a database outage.

**After:** `/health/live` runs no dependency checks; `/health/ready` selects checks tagged `ready`. `/health` retains its existing behavior. [OperationalDiagnosticsTests.cs](../../tests/Atlas.Tests/Integration/OperationalDiagnosticsTests.cs) supplies a failing tagged dependency and tests all three routes anonymously.

**Limit:** connectivity does not prove the schema exists or a business transaction will succeed. An operational guarantee must be stated precisely enough to test.
