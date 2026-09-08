# Answers, with the reason behind each one

These correspond to [practice and recall](12-practice-and-recall.md). Compare your explanation, not just your wording.

## Round A

1. **Atlas.Api.** `Program.cs` constructs the host and maps endpoints.
2. **EmploymentContract.** It links the client, worker, country, and employment terms.
3. **SaveChangesAsync.** `Add` marks the entity for insertion; a successful save persists it.
4. **404.** The handler selects `Results.NotFound()` for a null country. An unauthenticated request can stop earlier with 401.
5. **Draft.** `Activate` throws when the current state is not Draft. Onboarding is checked by the endpoint before calling this method.
6. **Payslip; Invoice.** They describe different recipients and purposes even when derived from the same payroll run.

Revisit [the story](01-the-story.md) for the nouns or [memory and storage](07-memory-and-storage.md) for saving.

## Round B

Receive the authorized POST → validate fields → normalize/check duplicates → construct/Add → save → return 201.

This is the successful country-creation path. Invalid input or an existing code exits earlier. Do not apply the same order mechanically to every route; other operations load existing state before some checks.

## Round C

7. **`code.ToUpperInvariant()` in the detail GET handler.** The supplied lowercase code is normalized before key lookup.
8. **The request needs an accepted identity.** The route value does not establish one. Inspect the header rather than guessing a different country.
9. **No.** The method changes the object's status and timestamp; it contains no database context or save. A domain test can run without SQLite.
10. **No.** Response mapping creates the representation sent to the caller. It does not itself change the stored entity. Investigate code that mutates a tracked entity and saves it when you want to change storage.
11. **The contract activation endpoint.** It queries onboarding items with the matching contract ID where `IsRequired` is true and `IsCompleted` is false. `Activate` itself knows only the entity's state rule.
12. **The test uses a different database.** `AtlasApiFactory` configures an in-memory SQLite connection; your Development server normally uses a file database.

## Round D: where to look

| Evidence | Source |
| --- | --- |
| `app.MapCountryEndpoints()` | [Program.cs](../src/Atlas.Api/Program.cs) |
| Group `RequireAuthorization()` | [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs) |
| POST's additional platform policy | [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs) |
| `ToResponse(Country c)` | [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs) |
| `pendingRequired` query | [ContractEndpoints.cs](../src/Atlas.Api/Endpoints/ContractEndpoints.cs) |
| `Status = ContractStatus.Active` | [EmploymentContract.cs](../src/Atlas.Domain/Entities/EmploymentContract.cs) |
| `DataSource=:memory:` and connection lifetime | [AtlasApiFactory.cs](../tests/Atlas.Tests/Integration/AtlasApiFactory.cs) |

## Round E: example explanation

“The caller requests a country by code. Authentication checks the key. The country GET handler receives the route value and a framework-supplied DbContext. It uppercases the code, finds the country, and maps it into a response. If no country is found it returns 404; otherwise it returns 200. This handler does not save country changes.”

For a shorter explanation: “An authenticated caller gives a code; the handler looks up the country and returns its public details or 404.” Both explanations are useful. Add detail when the question needs it.

## Round F

Employer cost is **240**. Deductions are **300**. Net pay is **1,700**. Base employer total is **2,240**.

The simple calculator omits expense and benefit adjustments applied by orchestration. An invoice also includes the client's management fee and can convert to a billing currency. A full run can include several workers. Do not treat the base calculator's total as the final invoice in every case.

Return to [the payroll example](09-payroll-with-small-numbers.md) if you mixed up the worker's deduction and the employer's extra cost.
