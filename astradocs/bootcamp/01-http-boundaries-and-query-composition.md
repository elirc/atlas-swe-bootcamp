# Lesson 1: A request becomes a carefully scoped query

This lesson accompanies JS-01 through JS-25. Implementation and verification status live in [progress](progress.md). Read the [tests](../../tests/Atlas.Tests/Integration/JuniorStoryTests.cs) beside the code: the test names identify which story they demonstrate.

## Start with the user's question

Suppose a client viewer asks for active contracts in the Philippines:

```http
GET /api/contracts?countryCode=ph&status=Active
X-Api-Key: <your-client-viewer-key>
```

The question is not simply “which contracts match PH?” It is “which contracts that this caller is allowed to see also match PH and Active?” The ownership rule is always present. Optional filters can narrow access; they cannot grant more of it.

Open [ContractEndpoints](../../src/Atlas.Api/Endpoints/ContractEndpoints.cs) and find the collection GET. Read it in this order:

1. Validate pagination and the country-code shape.
2. Begin a query over contracts.
3. Restrict a client caller to their own ClientId.
4. Apply any supplied clientId, status, and country filters.
5. Sort by CreatedAtUtc and Id.
6. Count and take the requested page through the paging helper.
7. Convert records into the public response shape.

The query is a description until a database operation materializes it. Adding another Where builds a more specific description. It does not query every row and then grant permissions afterward.

## Say it with a small table

Assume these records already exist:

| Contract | Client | Country | State |
| --- | --- | --- | --- |
| A | Acme | PH | Active |
| B | Acme | US | Active |
| C | Other company | PH | Active |
| D | Acme | PH | Draft |

An Acme viewer requesting PH + Active should receive A only. B fails the country filter. C fails ownership. D fails the state filter. An extra filter never turns C into an allowed record.

Now remove the country parameter. Acme can receive A and B, but still not C. Remove the state parameter too. D can appear, but C remains excluded. This is what it means to compose filters with authorization.

## Validation happens before saving

Read [ApiInput](../../src/Atlas.Api/ApiInput.cs) and country creation in [CountryEndpoints](../../src/Atlas.Api/Endpoints/CountryEndpoints.cs). A country name is trimmed, then checked against 100 characters. A 101-character name receives a field-specific 400 response before entity insertion.

There are two separate responsibilities:

- API validation gives a user a useful error and avoids accepting a value outside the documented contract.
- Database constraints protect stored relationships and uniqueness even when a writer is not this HTTP endpoint.

An EF maximum-length annotation is not a substitute for the first responsibility, especially with SQLite. The test checks both the response and the absence of the rejected row. A friendly error response would not be enough if the invalid row had already been saved.

The tests use an otherwise valid request at the boundary. If the test has an invalid email, missing country, and overlong name simultaneously, a 400 response would not establish which rule worked. Keep unrelated fields valid when proving one rule.

## Shape, normalization, and identity are different concepts

Currency creation checks three ASCII letters, then normalizes accepted text to uppercase. ` usd ` becomes `USD`. `U1D` fails shape validation. A fictional `XYZ` is accepted because these stories validate shape, not membership in an external currency registry.

One subtle review point: check the original trimmed characters for ASCII membership. Uppercasing first can map a non-ASCII character into an ASCII character in some cases. If the promise is “ASCII letters only,” normalization must not quietly erase evidence that the original input violated the promise. The tests include a non-ASCII long-s example to exercise that distinction.

Identifiers are different again. An invoice payrollRunId filter accepts a syntactically valid GUID even if no such run exists; it then returns an empty matching collection. Malformed GUID text is a binding error. A detail route looking up one missing document returns 404. Neither is a database crash.

## Why enum parsing needs a second check

TryParse answers whether text can be converted to an enum's underlying representation. It can successfully parse `999` even when the enum declares only Draft, Active, and Terminated.

The shared defined-enum rule checks parsing and Enum.IsDefined. It also rejects comma-separated combinations because these state/type filters accept one value, not a flags expression. A name such as `Active` or a defined numeric representation such as `1` remains supported.

Say it another way: converting a number into a labeled container does not prove the number has a declared label. Parsing and validating the allowed set are different steps.

## Omitted, false, and true are three inputs

The country list uses a nullable Boolean `isActive`. It needs to distinguish:

| Input | Meaning |
| --- | --- |
| Omitted | Do not filter by activity |
| `true` | Return active countries |
| `false` | Return inactive countries |

A condition that runs only when the value is true loses the false case. The implementation checks whether the value was supplied, then compares to that supplied value. A test combining `currencyCode=USD&isActive=false` proves that both filters remain active even when the expected result is empty.

`requiredOnly` on onboarding has a deliberately different contract: true selects required items; omitted or false shows all. The completion summary is calculated from the full checklist before selecting the displayed items. Display filtering must not redefine whether a contract is ready.

## Prefix search is not a wildcard language

Client and worker prefix queries trim input and match the start of display names using ASCII case-insensitive behavior. The query uses StartsWith rather than concatenating a caller's text into a LIKE pattern.

A prefix containing `%` or `_` means those literal characters. The tests seed names starting with `%_` and prove that a search returns that name rather than every name. A happy-path `ac` example alone would not catch accidental wildcard behavior.

The database query uppercases stored names and compares with a normalized prefix. This implementation does not promise accent folding or international linguistic search. Knowing what a feature does not define is part of describing it accurately.

## Order must be explicit before paging

People and companies can share names. A name is not a unique sort key. The implementation sorts by name, then Id. `ThenBy` extends an order; a second `OrderBy` would replace the first order.

The tests seed equal-name records with fixed IDs and ask for one record per page. They check both page membership and total count. This proves the declared ordering on an unchanged dataset. Offset paging still does not freeze the dataset across requests; an insert between page requests can move records.

Do not “solve” tied names by making names unique. That would change valid product data to hide a query-design issue.

## An injected clock makes a date rule repeatable

Worker creation rejects a birth date later than the current UTC day. [BootcampApiFactory](../../tests/Atlas.Tests/Integration/BootcampApiFactory.cs) supplies a fixed September 1, 2026 clock. The acceptance tests can therefore prove that September 1 is allowed and September 2 is rejected, whenever you run them.

The fixed clock replaces an external dependency. It is not faking the validation itself. The production handler still executes the actual date comparison, and the test still calls the HTTP endpoint and inspects persistence.

Avoid a test that calculates “tomorrow” once and then depends on the real clock remaining on the same day while the server handles the request. Injecting time makes the boundary a controllable input.

## Nested routes need both identifiers

The new document detail GET implements the Location returned by document creation. Its query matches both document ID and parent worker ID. A real document under the wrong worker URL returns 404.

Why both? The route promises a document belonging to this worker. An ID-only lookup would ignore half that promise. A separate worker-visibility check also prevents a caller from inspecting an unrelated worker's documents.

Try the sequence: POST a document, copy its Location, then GET that Location. Next, replace just the worker ID with another worker's ID and predict the result before running it.

## Reports filter before aggregating

Read [ReportEndpoints](../../src/Atlas.Api/Endpoints/ReportEndpoints.cs) and [ReportService](../../src/Atlas.Api/Reporting/ReportService.cs). Transport code validates the optional country/status input. The service applies it before selecting and grouping records.

If headcount is filtered to PH, its total, country breakdown, and client breakdown must all describe the same PH population. Filtering the final country array alone would leave a misleading overall total and client breakdown. The test checks the sum of client counts as well as the visible country rows.

Payroll cost status filters select Draft or Completed runs before summing saved payslips. Reading this report does not change the run state, consume expenses, or recalculate salaries.

## Practice without copying the implementation

1. Predict the response to an Acme viewer requesting another client's ID together with a matching prefix. Explain why a matching name does not expand permission.
2. Find the exact query line that keeps `false` meaningful in the country filter. Rewrite its meaning in plain English.
3. In your own branch, temporarily remove the document's WorkerId predicate and run the nested-route test. Explain the failure, then restore the predicate.
4. Add a case for a blank prefix and explain why it should behave like an omitted filter.
5. Explain why the salary-history cutoff returns every qualifying entry rather than only the latest salary.
6. Return tomorrow and trace the contract-list request without opening this lesson first.

Your review note can be four sentences: “The caller is __. We validate __. The database query restricts __ before paging. The test proves __ by checking both the response and __.”
