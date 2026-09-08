# 02: HTTP is a contract; authorization is a rule

**Outcome:** distinguish transport validation, business conflicts, and access control. Read [InvoiceEndpoints.cs](../../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), [AuthorizationExtensions.cs](../../src/Atlas.Api/Auth/AuthorizationExtensions.cs), and [AuthorizationMatrixTests.cs](../../tests/Atlas.Tests/Integration/AuthorizationMatrixTests.cs).

## Follow a request in order

Authentication asks who is calling. Atlas reads `X-Api-Key` and establishes claims. Authorization asks what that identity can do. Model binding converts a query or JSON field to a C# value. Endpoint validation rejects input the operation does not support. Domain rules decide whether a valid request makes sense in the current state.

Framework routing and authorization affect which failure is returned first. Do not infer authorization from a successful validation check. Reports use a platform-admin policy on the route group, so adding `asOf` does not grant client users access.

| Situation | Atlas response | Reason |
| --- | --- | --- |
| Missing or invalid key | 401 | No accepted identity |
| Client viewer attempts an admin operation | 403 | Role does not permit the operation |
| Client user reads another client's invoice | 404 | The resource is outside the visible tenant scope |
| `fromMonth=7` without `fromYear` | 400 | Incomplete period filter |
| Approve an already decided request | 409 | Operation conflicts with current business state |

An error should help a legitimate caller act. Validation problems identify fields; concurrency errors tell the caller to reload. Server failures must not disclose internal exception details or secrets.

## Tenant scoping is easy to lose in refactors

An invoice list scopes its database query to the caller's client before applying an optional `clientId` filter. The caller cannot use that parameter to expand access. A detail route checks ownership as well as existence. These are two separate behaviors to preserve.

Do not load every client's invoices and rely on a frontend to hide the unwanted ones. For every new route, explicitly identify whether it is platform-only, client-owned, or anonymous.

## Exercise: build an access matrix

Using seeded development keys, call a report as an admin, a client admin, a viewer, and anonymously. Predict each status first. Then read the integration harness and explain how tests create keys without depending on Development data.

For a proposed invoice export, write acceptance examples covering own-client access, another client's ID, a forged filter, and no key. Implement the export only after the examples agree with the existing policy.

**Completion check:** why is `GET /api/invoices?clientId=...` not itself an authorization check? Why should a regression test check returned IDs as well as status 200? Can an additive optional query parameter still change existing behavior?

Next: [domain modeling](03-domain-modeling.md).
