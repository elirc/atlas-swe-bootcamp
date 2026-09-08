using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contracts").RequireAuthorization();

        group.MapGet("/", async (Guid? clientId, string? status, string? countryCode, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem)
            {
                return problem;
            }

            if (ApiInput.CodeFilter(countryCode, 2, "countryCode", out var country) is { } countryProblem)
                return countryProblem;
            var query = db.Contracts.AsQueryable();
            if (!user.IsPlatformAdmin())
            {
                // Client users always get their own contracts, whatever filter they pass.
                var ownClientId = user.ClientIdOrNull();
                query = query.Where(c => c.ClientId == ownClientId);
            }
            if (clientId is not null)
            {
                query = query.Where(c => c.ClientId == clientId);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!ApiInput.DefinedEnum<ContractStatus>(status, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = [$"Unknown status '{status}'. Expected Draft, Active, or Terminated."],
                    });
                }
                query = query.Where(c => c.Status == parsed);
            }

            if (country is not null) query = query.Where(c => c.CountryCode == country);
            var contracts = await query.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToPageAsync(http, paging);
            return Results.Ok(contracts.Select(ToResponse).ToList());
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.FindAsync(id);
            return contract is null || !user.CanViewClient(contract.ClientId)
                ? Results.NotFound()
                : Results.Ok(ToResponse(contract));
        });

        group.MapPost("/", async (CreateContractRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.ClientId == Guid.Empty)
            {
                errors["clientId"] = ["ClientId is required."];
            }
            if (request.WorkerId == Guid.Empty)
            {
                errors["workerId"] = ["WorkerId is required."];
            }
            if (string.IsNullOrWhiteSpace(request.JobTitle))
            {
                errors["jobTitle"] = ["JobTitle is required."];
            }
            if (request.MonthlySalary <= 0)
            {
                errors["monthlySalary"] = ["MonthlySalary must be greater than zero."];
            }
            if (request.StartDate == default)
            {
                errors["startDate"] = ["StartDate is required."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            if (!user.CanManageClient(request.ClientId))
            {
                return Results.Problem(
                    detail: "Only platform admins or the client's own admins can create contracts for this client.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var client = await db.Clients.FindAsync(request.ClientId);
            if (client is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["clientId"] = [$"Client '{request.ClientId}' does not exist."],
                });
            }

            var worker = await db.Workers.Include(w => w.Country).SingleOrDefaultAsync(w => w.Id == request.WorkerId);
            if (worker is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["workerId"] = [$"Worker '{request.WorkerId}' does not exist."],
                });
            }

            var country = worker.Country!;
            if (!country.IsActive)
            {
                return Results.Problem(statusCode: 409, detail: $"Hiring in {country.Name} is currently unavailable.");
            }

            var hasOpenContract = await db.Contracts.AnyAsync(c =>
                c.WorkerId == worker.Id && c.Status != ContractStatus.Terminated);
            if (hasOpenContract)
            {
                return Results.Problem(
                    detail: $"Worker '{worker.FullName}' already has a contract that is not terminated.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var contract = new EmploymentContract
            {
                ClientId = client.Id,
                WorkerId = worker.Id,
                CountryCode = country.Code,
                JobTitle = request.JobTitle!.Trim(),
                MonthlySalary = request.MonthlySalary,
                CurrencyCode = country.CurrencyCode,
                StartDate = request.StartDate,
            };
            db.Contracts.Add(contract);
            var template = await db.CountryOnboardingTemplates.Include(x => x.Items).AsNoTracking()
                .SingleOrDefaultAsync(x => x.CountryCode == country.Code);
            db.OnboardingItems.AddRange(template is null ? OnboardingItem.CreateDefaultChecklist(contract.Id)
                : template.Items.Select(x => new OnboardingItem { ContractId = contract.Id, Type = x.Type, Title = x.Title, IsRequired = x.IsRequired }));
            // Creating a contract and pausing hiring compete on this country version.
            db.Entry(country).Property(x => x.Version).IsModified = true;
            db.SalaryRecords.Add(new SalaryRecord
            {
                ContractId = contract.Id,
                MonthlySalary = contract.MonthlySalary,
                JobTitle = contract.JobTitle,
                EffectiveDate = contract.StartDate,
                Source = SalaryRecordSource.Initial,
            });
            await db.SaveChangesAsync();

            return Results.Created($"/api/contracts/{contract.Id}", ToResponse(contract));
        });

        group.MapPost("/{id:guid}/activate", async (Guid id, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var contract = await db.Contracts.FindAsync(id);
            if (contract is null || !user.CanViewClient(contract.ClientId))
            {
                return Results.NotFound();
            }
            if (!user.CanManageClient(contract.ClientId))
            {
                return Results.Problem(
                    detail: "Only platform admins or the client's own admins can activate contracts.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var before = ContractDecisionWriter.Capture(contract);
            var items = await db.OnboardingItems.Where(i => i.ContractId == id).ToListAsync();
            var country = await db.Countries.FindAsync(contract.CountryCode);
            var assessment = ContractReadiness.Assess(contract, items, country!.IsActive);
            if (!country.IsActive) return Results.Problem(statusCode: 409, detail: "Hiring in this country is paused.");
            var pendingRequired = assessment.Where(b => b.Code == "required-onboarding-incomplete").Select(b => b.Message).ToList();
            if (pendingRequired.Count > 0)
            {
                return Results.Problem(
                    detail: $"Contract cannot be activated: {pendingRequired.Count} required onboarding item(s) pending ({string.Join("; ", pendingRequired)}).",
                    statusCode: StatusCodes.Status409Conflict);
            }

            try
            {
                contract.Activate(clock.GetUtcNow());
                db.Entry(country).Property(x => x.Version).IsModified = true;
            }
            catch (DomainException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }

            await ContractDecisionWriter.SaveAsync(db, contract, before, user, clock, "activated", "contract-activation", id, contract.StartDate);
            return Results.Ok(ToResponse(contract));
        });

        group.MapPost("/{id:guid}/terminate", async (Guid id, TerminateContractRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var contract = await db.Contracts.FindAsync(id);
            if (contract is null || !user.CanViewClient(contract.ClientId))
            {
                return Results.NotFound();
            }
            if (!user.CanManageClient(contract.ClientId))
            {
                return Results.Problem(
                    detail: "Only platform admins or the client's own admins can terminate contracts.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var before = ContractDecisionWriter.Capture(contract);
            if (request.EndDate == default)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["endDate"] = ["EndDate is required."],
                });
            }

            try
            {
                contract.Terminate(request.EndDate, request.Reason ?? string.Empty, clock.GetUtcNow());
            }
            catch (DomainException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }

            await ContractDecisionWriter.SaveAsync(db, contract, before, user, clock, "terminated", "direct-termination", id, request.EndDate);
            return Results.Ok(ToResponse(contract));
        });

        return app;
    }

    internal static ContractResponse ToResponse(EmploymentContract c) => new(
        c.Id,
        c.ClientId,
        c.WorkerId,
        c.CountryCode,
        c.JobTitle,
        c.MonthlySalary,
        c.CurrencyCode,
        c.StartDate,
        c.EndDate,
        c.Status.ToString(),
        c.CreatedAtUtc,
        c.ActivatedAtUtc,
        c.TerminatedAtUtc,
        c.TerminationReason);
}

public record CreateContractRequest(
    Guid ClientId,
    Guid WorkerId,
    string? JobTitle,
    decimal MonthlySalary,
    DateOnly StartDate);

public record TerminateContractRequest(DateOnly EndDate, string? Reason);

public record ContractResponse(
    Guid Id,
    Guid ClientId,
    Guid WorkerId,
    string CountryCode,
    string JobTitle,
    decimal MonthlySalary,
    string CurrencyCode,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? TerminatedAtUtc,
    string? TerminationReason);
