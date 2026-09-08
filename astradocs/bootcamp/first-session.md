# Your first session: run, predict, trace

The aim of this session is to explain one working request. You can stop after any step and return later. Keep the [study journal template](study-journal-template.md) open and write your prediction before running each request.

## 1. Start a separate practice database

Open PowerShell in the repository root, the folder containing `Atlas.slnx`:

```powershell
python scripts/learn.py doctor
python scripts/bootcamp.py serve --new
```

The launcher finds the local .NET SDK, creates a uniquely named database under `artifacts/bootcamp`, and runs the Development API at `http://127.0.0.1:5080`. Wait for the application to report that it is listening. Leave this terminal running. Copy the printed database filename into your journal; next time, `serve --database <that-filename.db>` resumes it. If port 5080 is occupied, use `serve --new --port 5081` and update the address below.

Development seeds fictional companies and workers plus the documented development keys. These requests use that local sample data. Existing databases are preserved; the application creates a fresh schema and does not migrate an older one.

## 2. Send a request from another PowerShell terminal

```powershell
$atlasBase = 'http://127.0.0.1:5080'
$atlasHeaders = @{ 'X-Api-Key' = 'dev-admin-key' }
Invoke-RestMethod -Uri "$atlasBase/health/ready"
```

The health request does not need a key. The ready check asks whether the database is reachable. It does not prove that payroll calculations are correct; those need behavior tests.

Now predict which countries use USD:

```powershell
$atlasCountries = Invoke-RestMethod -Uri "$atlasBase/api/countries?currencyCode=usd" -Headers $atlasHeaders
$atlasCountries | Format-Table code, name, currencyCode, isActive
```

The filter accepts lowercase input and normalizes it. Read [CountryEndpoints](../../src/Atlas.Api/Endpoints/CountryEndpoints.cs), starting at the collection `MapGet`. Find the validation, the currency predicate, and the final database read. Then read [ApiInput](../../src/Atlas.Api/ApiInput.cs) to explain why `U1D` would fail validation.

Write the same explanation twice:

> Plain language: the server returns countries matching the requested currency.
>
> Code language: the endpoint validates the optional filter, adds a Where condition, and materializes the query before building the response.

## 3. Observe access through two credentials

```powershell
$atlasViewerHeaders = @{ 'X-Api-Key' = 'dev-acme-viewer-key' }
$atlasAllClients = Invoke-RestMethod -Uri "$atlasBase/api/clients" -Headers $atlasHeaders
$atlasVisibleClients = Invoke-RestMethod -Uri "$atlasBase/api/clients" -Headers $atlasViewerHeaders
$atlasAllClients | Format-Table id, name
$atlasVisibleClients | Format-Table id, name
```

On a fresh seeded database, the platform administrator sees two clients and the Acme viewer sees Acme. Open [ClientEndpoints](../../src/Atlas.Api/Endpoints/ClientEndpoints.cs) and locate the caller's ClientId predicate. The response is a JSON array, so there is no `items` wrapper to unwrap here.

Explain why adding a name filter must never make another client's records visible. Draw this order: identify caller, restrict ownership, apply optional filters, page, respond. If your database has changed during another exercise, explain its actual results rather than forcing the seed counts to match.

## 4. Preview payroll and check that it saved nothing

```powershell
$atlasPeriod = [DateTime]::UtcNow
$atlasPayrollQuery = "countryCode=PH&year=$($atlasPeriod.Year)&month=$($atlasPeriod.Month)"
$atlasBefore = @(Invoke-RestMethod -Uri "$atlasBase/api/payroll-runs?$atlasPayrollQuery" -Headers $atlasHeaders)
$atlasRequest = @{ countryCode = 'PH'; year = $atlasPeriod.Year; month = $atlasPeriod.Month } | ConvertTo-Json
$atlasSimulation = Invoke-RestMethod -Method Post -Uri "$atlasBase/api/payroll-simulations" -Headers $atlasHeaders -ContentType 'application/json' -Body $atlasRequest
$atlasSimulation | ConvertTo-Json -Depth 12
$atlasAfter = @(Invoke-RestMethod -Uri "$atlasBase/api/payroll-runs?$atlasPayrollQuery" -Headers $atlasHeaders)
"Saved runs before: $($atlasBefore.Count); after: $($atlasAfter.Count)"
```

The two counts should match if no other caller is creating payroll concurrently. A simulation can contain blockers; inspect them instead of treating every 200 response as permission to complete payroll. The current period must be within the supported years 2000 through 2100.

Follow this source path: [PayrollLearningEndpoints](../../src/Atlas.Api/Endpoints/PayrollLearningEndpoints.cs) receives the request, [PayrollAssessmentService](../../src/Atlas.Infrastructure/PayrollAssessmentService.cs) selects inputs, and [PayrollAssessment](../../src/Atlas.Domain/Services/PayrollAssessment.cs) calculates results. Find where a simulation differs from payroll creation. [Lesson 6](06-payroll-evidence-and-durable-work.md) repeats this distinction with saved evidence and retry examples.

## 5. Finish with one explanation and one focused test

From a repository terminal:

```powershell
python scripts/bootcamp.py show JS-09
python scripts/bootcamp.py test JS-09
```

The test command runs the registered junior suite, which contains several stories. It reports the real exit status. It can take time while building and creating test databases; wait for the final result before calling it passed. Read the JS-09 cases in [JuniorStoryTests](../../tests/Atlas.Tests/Integration/JuniorStoryTests.cs) and compare their inputs with your manual request.

Write three sentences: what you predicted, what you observed, and what source line explains it. Close the notes and say those sentences again. Your next session is [Lesson 1](01-http-boundaries-and-query-composition.md), followed by the first six sessions in the [practice path](practice-path.md).

Stop the running API with Ctrl+C in its own terminal. Keep the practice database so you can return to the same data.
