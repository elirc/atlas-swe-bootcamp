# A file tour with a small stopping point

**One objective:** know which file to open next. You do not need to understand every line.

## First pass: only four files

| Open | Look for | Say what it does |
| --- | --- | --- |
| [Program.cs](../src/Atlas.Api/Program.cs) | `app.MapCountryEndpoints()` | Connects the country routes to the host |
| [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs) | `group.MapGet("/{code}"` | Handles a country lookup |
| [Country.cs](../src/Atlas.Domain/Entities/Country.cs) | `Code`, `Name`, `CurrencyCode` | Describes a country record |
| [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs) | `DbSet<Country> Countries` | Gives EF access to country records |

At this point you know where a request enters, where it is handled, what data it uses, and how the code accesses stored records. Stop here if the names are still unfamiliar.

## Second pass: startup versus request time

In `Program.cs`, `builder.Services` registrations tell the framework how to supply dependencies. `AddDbContext` configures database access. `AddScoped<PayrollService>()` registers payroll orchestration. The endpoint framework can later supply a registered service as a handler parameter.

`app.Use...` builds the middleware pipeline. `app.Map...` registers routes. `app.Run()` starts serving. Registering a route does not execute its handler body at startup; the matching request causes that body to run later.

Development startup also calls `EnsureCreated` and `DataSeeder.Seed`. These prepare a local example database. Tests configure a separate environment and database.

## Third pass: files for a particular question

| Your question | First useful file |
| --- | --- |
| Why did this key fail? | [ApiKeyAuthenticationHandler.cs](../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs) |
| Can this caller manage this company? | [AuthorizationExtensions.cs](../src/Atlas.Api/Auth/AuthorizationExtensions.cs) |
| Why can this contract not activate? | [ContractEndpoints.cs](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), then the entity |
| Where are payroll amounts calculated? | [PayrollCalculator.cs](../src/Atlas.Domain/Services/PayrollCalculator.cs) |
| What coordinates a whole payroll run? | [PayrollService.cs](../src/Atlas.Infrastructure/PayrollService.cs) |
| Where is report data gathered? | [ReportService.cs](../src/Atlas.Api/Reporting/ReportService.cs) |
| Why did a failed request log this status? | [RequestLoggingMiddleware.cs](../src/Atlas.Api/RequestLoggingMiddleware.cs) and pipeline order |
| How can I reproduce this behavior? | The corresponding file under [tests](../tests/Atlas.Tests) |

File names help, but the implementation is the evidence. For example, the report service lives in the API project, while payroll orchestration lives in Infrastructure. “Service” does not identify one universal layer.

## Say it again

Start at the route for an HTTP problem, at a calculator for arithmetic, and at the DbContext for persistence mapping. Follow one call at a time.

## Checkpoint

Close the files. Reopen the country handler without using your recent-files list. Find where it obtains `db`. Next: [follow a read](04-follow-a-read.md).
