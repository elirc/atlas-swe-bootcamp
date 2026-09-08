using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class WorkerEndpoints
{
    public static IEndpointRouteBuilder MapWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workers").RequireAuthorization();

        group.MapGet("/", async (string? countryCode, string? namePrefix, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem)
            {
                return problem;
            }

            if (ApiInput.Prefix(namePrefix, out var prefix) is { } prefixProblem) return prefixProblem;
            var query = db.Workers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                var code = countryCode.Trim().ToUpperInvariant();
                query = query.Where(w => w.CountryCode == code);
            }
            if (!user.IsPlatformAdmin())
            {
                // Client users only see workers who have a contract with their client.
                var ownClientId = user.ClientIdOrNull();
                query = query.Where(w => db.Contracts.Any(c => c.WorkerId == w.Id && c.ClientId == ownClientId));
            }

            if (prefix is not null) query = query.Where(w => w.FullName.ToUpper().StartsWith(prefix));
            var workers = await query.OrderBy(w => w.FullName).ThenBy(w => w.Id).ToPageAsync(http, paging);
            return Results.Ok(workers.Select(ToResponse).ToList());
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var worker = await db.Workers.FindAsync(id);
            if (worker is null)
            {
                return Results.NotFound();
            }
            if (!user.IsPlatformAdmin())
            {
                var ownClientId = user.ClientIdOrNull();
                var isOwnWorker = await db.Contracts.AnyAsync(c => c.WorkerId == id && c.ClientId == ownClientId);
                if (!isOwnWorker)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(ToResponse(worker));
        });

        group.MapPost("/", async (CreateWorkerRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            var (normalized, errors) = Atlas.Domain.Services.WorkerInput.Normalize(request.FullName, request.Email, request.CountryCode, request.DateOfBirth, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var countryCode = normalized.CountryCode;
            if (!await db.Countries.AnyAsync(c => c.Code == countryCode))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["countryCode"] = [$"Country '{countryCode}' is not supported."],
                });
            }

            var email = normalized.Email;
            if (await db.Workers.AnyAsync(w => w.Email == email))
            {
                return Results.Problem(
                    detail: $"A worker with email '{email}' already exists.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var worker = new Worker
            {
                FullName = normalized.FullName,
                CreatedAtUtc = clock.GetUtcNow(),
                Email = email,
                CountryCode = countryCode,
                DateOfBirth = request.DateOfBirth,
            };
            db.Workers.Add(worker);
            await db.SaveChangesAsync();

            return Results.Created($"/api/workers/{worker.Id}", ToResponse(worker));
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);

        return app;
    }

    private static WorkerResponse ToResponse(Worker w) =>
        new(w.Id, w.FullName, w.Email, w.CountryCode, w.DateOfBirth, w.CreatedAtUtc);
}

public record CreateWorkerRequest(
    string? FullName,
    string? Email,
    string? CountryCode,
    DateOnly? DateOfBirth);

public record WorkerResponse(
    Guid Id,
    string FullName,
    string Email,
    string CountryCode,
    DateOnly? DateOfBirth,
    DateTimeOffset CreatedAtUtc);
