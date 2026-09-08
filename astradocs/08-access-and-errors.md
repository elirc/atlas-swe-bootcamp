# Access and errors: different reasons a request stops

**One objective:** use a status code to ask the next useful question.

## Begin with two separate questions

**Authentication:** who is calling? Atlas checks the `X-Api-Key` header against active API users.

**Authorization:** what may that caller do? A valid key does not automatically grant permission to every operation.

Read [ApiKeyAuthenticationHandler.cs](../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs) for the first question and [AuthorizationExtensions.cs](../src/Atlas.Api/Auth/AuthorizationExtensions.cs) for the second.

## The same idea as an office analogy

Your badge identifies you when entering an office. Being inside the office does not grant access to every employee's files. In Atlas, the accepted key establishes identity; roles and client ownership restrict operations and data.

This is an analogy, not the storage implementation. The current authentication code performs a database lookup for the API key.

## Roles in this repository

| Role | A useful starting description |
| --- | --- |
| PlatformAdmin | Operates Atlas across clients; can create countries and run payroll |
| ClientAdmin | Manages permitted resources belonging to its own client |
| ClientViewer | Reads permitted resources belonging to its own client |

Country reads require an authenticated caller. Country creation is platform-only. Contract activation checks both ownership and management permission. Payroll and reports are platform-only. Read the specific route before generalizing a rule to all routes.

## Different reasons for “it did not work”

| Response | Recurring example | Next question |
| --- | --- | --- |
| 401 | Read a country without a key | Did the server accept an identity? |
| 403 | Create a country with a client viewer key | Does this role permit this operation? |
| 404 | Fetch a missing country, or another client's contract | Is the resource present and visible to this caller? |
| 400 | Create a country with a blank name | Which input field is invalid? |
| 409 | Create a duplicate country or activate before onboarding completes | What conflicts with existing state? |
| 500 | An unexpected server failure | What does the server-side evidence show? |

These failures can all look like “the request failed,” but their fixes differ. For JSON problem responses, inspect `status`, `detail`, and any `errors` map. Use `X-Request-Id` and the matching `traceId` to connect a response with the completion log.

## Say it again

A 401 is not a missing country. A 403 is not proof that the JSON is malformed. A 409 does not always mean a duplicate: unfinished onboarding and stale updates can also conflict.

## Checkpoint

Explain why changing the country code cannot fix an absent API key. Explain why a client administrator cannot obtain another client's contracts simply by changing a `clientId` query parameter. The list handler scopes its query to the caller's client.

For existing-resource contract activation, ownership is checked before management permission. Do not assume every POST in the application uses that exact ordering; inspect its code.
