# An object in memory is not a saved database row

**One objective:** distinguish “I changed a C# value” from “a later request will read the change.”

## Version 1: a draft and a saved document

Imagine editing a document without saving it. You can see your changes in the editor, but another program reading the saved file will not see them yet. This is a useful starting analogy for an entity and its stored row. The database has additional rules and concurrency behavior, so the analogy is not the full implementation.

## Version 2: the exact country sequence

| Code | In memory | In the database |
| --- | --- | --- |
| `new Country { ... }` | A new object exists | No new row from this statement |
| `db.Countries.Add(country)` | EF tracks it as a new entity | Still not inserted by this call |
| `await db.SaveChangesAsync()` succeeds | EF has saved its tracked changes | The new row is persisted |
| A later request fetches the code | Another context can load it | The saved row supplies its values |

Read [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs) and point to these steps. `CountryResponse` is a separate representation sent to the caller; changing a response object would not automatically update a database row.

## Version 3: an existing contract

A request loads a tracked contract. `contract.Activate(now)` changes the object. `SaveChangesAsync` persists the changed status and activation timestamp. If the save fails, an in-memory changed object is not evidence that the database contains those changes.

Atlas also increments a `Version` value on modified versioned entities. If another context saved first, the stale context's update can fail with a concurrency exception. The API translates that into 409. See the version code near the bottom of [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs).

You can postpone the full race explanation. First remember that reads and saves can fail and that local object state and persisted state must be distinguished.

## Two database environments

| Running locally in Development | Running the integration tests |
| --- | --- |
| Uses the configured SQLite database, normally a local file | Uses a separate in-memory SQLite connection per factory |
| Startup seeds sample business data | The test factory seeds a test admin key; tests supply their data |
| Saved records can remain after restart | Database lifetime is tied to the held-open connection |

The tests use a real SQLite provider, not a fake list pretending to be a database. Tests sharing a factory can share its rows. “In memory” does not mean “a new database for every assertion.”

## Checkpoint

Without reading above, draw three boxes: `new`, `Add`, `SaveChangesAsync`. Put a star at the step that persists changes. Explain why a successful integration test does not add data to your development database.
