# Follow writes: create a record, then understand a transition

**One objective:** separate checking input, changing an object, and saving changes.

## First example: create a country

Read the POST handler in [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs). Unlike the country GET handlers, creating a country requires the platform-admin policy.

| Stage | What the current handler does |
| --- | --- |
| Receive | Bind JSON to `CreateCountryRequest` |
| Validate | Check required text, code lengths, fractional rates, and notice-day bounds |
| Normalize | Trim and uppercase country/currency codes |
| Check existing state | Ask whether that country code already exists |
| Construct | Build a `Country` object |
| Track | Call `db.Countries.Add(country)` |
| Persist | Await `db.SaveChangesAsync()` |
| Reply | Return 201 with the response and new resource location |

Validation returns 400. An already-existing code returns 409. Successful creation returns 201. The length check does not establish that a code is a real assigned country code; read what the condition checks instead of assuming the error message proves more.

`Add` and `SaveChangesAsync` are different steps. Remember that distinction; it appears again on [page 07](07-memory-and-storage.md).

Use `CreateCountry_ThenFetchIt_RoundTrips` in [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs) as a repeatable example. Running the test will not create a country in your development database.

## Second example: activate an existing contract

Find `group.MapPost("/{id:guid}/activate"` in [ContractEndpoints.cs](../src/Atlas.Api/Endpoints/ContractEndpoints.cs).

1. Load the contract. Return 404 if missing or invisible to this caller.
2. Check management permission. A caller who can only view it receives 403.
3. Query incomplete required onboarding items. Return 409 if any remain.
4. Call `contract.Activate(now)`. Its domain rule requires `Draft`.
5. Save the change and return 200 with the updated response.

The endpoint checks onboarding because that requires other records. The entity's `Activate` method checks its own state. The entity does not query the checklist itself.

## Say it as a conversation

**Caller:** “Activate this contract.”

**Endpoint:** “Can you see it? Can you manage it? Is required onboarding complete?”

**Contract:** “I can activate only if I am Draft. I will change my in-memory status.”

**DbContext:** “SaveChanges sends the tracked changes to storage.”

**Endpoint:** “Here is the updated response.”

## Checkpoint

Point to the onboarding check, the state change, and the save. They occur in different places. Explain what remains unchanged when a request is rejected before `Activate` runs.

A useful repeated sentence: **passing a check is not saving a record; changing an object is not saving a record; a successful save persists the tracked changes.**
