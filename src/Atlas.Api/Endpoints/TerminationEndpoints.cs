using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class TerminationEndpoints
{
    public static IEndpointRouteBuilder MapTerminationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/termination-requests").RequireAuthorization();

        group.MapGet("/", async (Guid? contractId, string? status, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem)
            {
                return problem;
            }

            var query = db.TerminationRequests.AsQueryable();
            if (!user.IsPlatformAdmin())
            {
                var ownClientId = user.ClientIdOrNull();
                query = query.Where(t => t.Contract!.ClientId == ownClientId);
            }
            if (contractId is not null)
            {
                query = query.Where(t => t.ContractId == contractId);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<TerminationRequestStatus>(status, ignoreCase: true, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = [$"Unknown status '{status}'. Expected {string.Join(", ", Enum.GetNames<TerminationRequestStatus>())}."],
                    });
                }
                query = query.Where(t => t.Status == parsed);
            }

            var requests = await query.OrderBy(t => t.RequestedAtUtc).ToPageAsync(http, paging);
            return Results.Ok(requests.Select(ToResponse).ToList());
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var request = await db.TerminationRequests.Include(t => t.Contract).SingleOrDefaultAsync(t => t.Id == id);
            return request is null || !user.CanViewClient(request.Contract!.ClientId)
                ? Results.NotFound()
                : Results.Ok(ToResponse(request));
        });

        group.MapPost("/", async (CreateTerminationRequestRequest request, ClaimsPrincipal user, AtlasDbContext db, ContractProposalService proposals, TimeProvider clock) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.ContractId == Guid.Empty)
            {
                errors["contractId"] = ["ContractId is required."];
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                errors["reason"] = ["Reason is required."];
            }
            if (request.ProposedEndDate.Year is < 2000 or > 2100)
            {
                errors["proposedEndDate"] = ["ProposedEndDate is required."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var contract = await db.Contracts.Include(c => c.Country).SingleOrDefaultAsync(c => c.Id == request.ContractId);
            if (contract is null || !user.CanViewClient(contract.ClientId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["contractId"] = [$"Contract '{request.ContractId}' does not exist."],
                });
            }
            if (!user.CanManageClient(contract.ClientId))
            {
                return Results.Problem(
                    detail: "Only platform admins or the client's own admins can request terminations.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var eligibility = await proposals.CheckTerminationAsync(contract, request.ProposedEndDate);

            var termination = new TerminationRequest
            {
                ContractId = contract.Id,
                Reason = request.Reason!.Trim(),
                NoticeDate = eligibility.NoticeDate,
                RequestedAtUtc = clock.GetUtcNow(),
                ProposedEndDate = request.ProposedEndDate,
            };
            db.TerminationRequests.Add(termination);
            db.Entry(contract).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync();

            return Results.Created($"/api/termination-requests/{termination.Id}", ToResponse(termination));
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id, DecideTerminationRequest? request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var termination = await db.TerminationRequests.Include(t => t.Contract).SingleOrDefaultAsync(t => t.Id == id);
            if (termination is null || !user.CanViewClient(termination.Contract!.ClientId))
            {
                return Results.NotFound();
            }
            if (!user.CanManageClient(termination.Contract!.ClientId))
            {
                return Results.Problem(
                    detail: "Only platform admins or the client's own admins can decide termination requests.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var before = ContractDecisionWriter.Capture(termination.Contract!);
            try
            {
                // Terminating the contract validates it is still active before the
                // request itself transitions, so a failed apply leaves it pending.
                termination.Contract!.Terminate(termination.ProposedEndDate, termination.Reason, clock.GetUtcNow());
                termination.Approve(clock.GetUtcNow(), request?.Note);
            }
            catch (DomainException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }

            await ContractDecisionWriter.SaveAsync(db, termination.Contract!, before, user, clock, "terminated", "termination-request-approval", id, termination.ProposedEndDate);
            return Results.Ok(ToResponse(termination));
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id, DecideTerminationRequest? request, ClaimsPrincipal user, AtlasDbContext db) =>
                await DecideAsync(id, user, db, t => t.Reject(request?.Note ?? string.Empty, DateTimeOffset.UtcNow)));

        group.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
            await DecideAsync(id, user, db, t => t.Cancel(DateTimeOffset.UtcNow)));

        return app;
    }

    /// <summary>Shared load + authorize + transition + save pipeline for reject/cancel.</summary>
    private static async Task<IResult> DecideAsync(
        Guid id, ClaimsPrincipal user, AtlasDbContext db, Action<TerminationRequest> transition)
    {
        var termination = await db.TerminationRequests.Include(t => t.Contract).SingleOrDefaultAsync(t => t.Id == id);
        if (termination is null || !user.CanViewClient(termination.Contract!.ClientId))
        {
            return Results.NotFound();
        }
        if (!user.CanManageClient(termination.Contract!.ClientId))
        {
            return Results.Problem(
                detail: "Only platform admins or the client's own admins can decide termination requests.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            transition(termination);
        }
        catch (DomainException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(termination));
    }

    private static TerminationRequestResponse ToResponse(TerminationRequest t) => new(
        t.Id,
        t.ContractId,
        t.Reason,
        t.NoticeDate,
        t.ProposedEndDate,
        t.Status.ToString(),
        t.RequestedAtUtc,
        t.DecidedAtUtc,
        t.DecisionNote);
}

public record CreateTerminationRequestRequest(
    Guid ContractId,
    string? Reason,
    DateOnly ProposedEndDate);

public record DecideTerminationRequest(string? Note);

public record TerminationRequestResponse(
    Guid Id,
    Guid ContractId,
    string Reason,
    DateOnly NoticeDate,
    DateOnly ProposedEndDate,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote);
