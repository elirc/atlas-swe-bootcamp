# Read a test as an example with a result

**One objective:** use an existing test to explain behavior before changing implementation.

## A three-part story

**Arrange:** prepare an input and any needed state.

**Act:** perform the operation.

**Assert:** compare the observed outcome with the expected one.

Read `Calculate_RoundNumbers_ProducesExpectedBreakdown` in [PayrollCalculatorTests.cs](../tests/Atlas.Tests/Domain/PayrollCalculatorTests.cs). Preparing country rates is Arrange. Calling `Calculate` is Act. Checking `NetPay` is Assert.

This domain test does not start the API or a database. It directly exercises the calculator.

## The same pattern through HTTP

Read `CreateCountry_ThenFetchIt_RoundTrips` in [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs).

1. Submit a country using lowercase `nl` and currency `eur`.
2. Expect creation to return 201 and uppercase codes.
3. Fetch `/api/countries/NL`.
4. Expect the stored country's name in the result.

The test covers more than a return value: routing, JSON, normalization, persistence, and reading the saved record. It uses a test factory with its own SQLite database and admin key.

## Run only the example you are reading

From the repository root:

```powershell
dotnet test Atlas.slnx --filter FullyQualifiedName~Calculate_RoundNumbers_ProducesExpectedBreakdown
dotnet test Atlas.slnx --filter FullyQualifiedName~CreateCountry_ThenFetchIt_RoundTrips
```

You do not need to run the development server first. The integration test hosts the API itself. The first run may restore/build projects, so it can take longer than later runs.

The optional helper runs broader lesson-related groups: `python scripts/learn.py test 00` runs the country endpoint suite.

## Debug the same example slowly

Set a breakpoint on the country POST handler's validation block. Run the integration test under your debugger. Inspect `request.Code`, then step to normalization, `Add`, and `SaveChangesAsync`. After saving, compare the entity and response values.

If you are debugging a running API instead, send the matching HTTP request. Starting the host alone will not execute the POST handler.

## A short troubleshooting note

```text
I expected:
I observed:
The exact request or test command was:
The first relevant failing line/assertion was:
My current explanation is:
One thing that would disprove it is:
```

Avoid changing several files before you know which layer failed. Build errors, assertion failures, and HTTP failures are different starting points.

## Checkpoint

Explain what the round-trip test proves that a direct `new Country` test would not. Then identify something it does not prove, such as every authorization role or simultaneous duplicate creation. Next: [your first small change](11-first-small-change.md).
