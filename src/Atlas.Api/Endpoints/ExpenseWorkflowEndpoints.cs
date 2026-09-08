using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ExpenseWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapExpenseWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expense-claims").RequireAuthorization();
        group.MapPut("/{id:guid}", async (Guid id, ReplaceExpenseRequest request, ClaimsPrincipal user, AtlasDbContext db, ExpenseApprovalService approval) =>
        {
            var claim = await Load(id, db);
            if (claim is null || !user.CanViewClient(claim.Contract!.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(claim.Contract!.ClientId)) return Results.Forbid();
            var errors = ExpenseInput.Validate(request.Description, request.Items);
            if (request.ExpectedVersion < 0) errors["expectedVersion"] = ["Version cannot be negative."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (claim.Version != request.ExpectedVersion || claim.Status != ExpenseClaimStatus.Pending || claim.Approvals.Count > 0)
                return Results.Problem(statusCode: 409, detail: "Only an unchanged pending claim with no approval votes can be edited.");
            db.ExpenseItems.RemoveRange(claim.Items);
            claim.Items = ExpenseInput.Items(claim.Id, request.Items!);
            db.ExpenseItems.AddRange(claim.Items);
            claim.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            await approval.CapturePolicyAsync(claim, claim.Contract.ClientId);
            db.Entry(claim).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync();
            return Results.Ok(ExpenseEndpoints.ToResponse(claim));
        });
        group.MapPost("/{id:guid}/withdraw", async (Guid id, ExpectedVersionRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var claim = await Load(id, db);
            if (claim is null || !user.CanViewClient(claim.Contract!.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(claim.Contract!.ClientId)) return Results.Forbid();
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            if (claim.Version != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The claim changed. Reload it.");
            claim.Withdraw(clock.GetUtcNow()); await db.SaveChangesAsync();
            return Results.Ok(ExpenseEndpoints.ToResponse(claim));
        });
        var policies = app.MapGroup("/api/clients/{clientId:guid}/expense-approval-policies/{currency}").RequireAuthorization();
        policies.MapGet("/", async (Guid clientId, string currency, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            var code = currency.Trim().ToUpperInvariant();
            if (!ApiInput.IsAsciiCode(currency.Trim(), 3)) return ApiInput.Error("currency", "Use three ASCII letters.");
            var policy = await db.ExpenseApprovalPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.CurrencyCode == code);
            return Results.Ok(new { clientId, currencyCode = code, configured = policy is not null, version = policy?.Version ?? 0, threshold = policy?.Threshold });
        });
        policies.MapPut("/", async (Guid clientId, string currency, ExpensePolicyRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            var code = currency.Trim().ToUpperInvariant();
            if (!ApiInput.IsAsciiCode(currency.Trim(), 3) || !await db.Countries.AnyAsync(x => x.CurrencyCode == code))
                return ApiInput.Error("currency", "Choose a configured country currency.");
            if (request.Threshold is <= 0 or > 1_000_000_000m || decimal.Round(request.Threshold, 2) != request.Threshold)
                return ApiInput.Error("threshold", "Threshold must be positive, at most 1,000,000,000, and have at most two decimals.");
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            var policy = await db.ExpenseApprovalPolicies.SingleOrDefaultAsync(x => x.ClientId == clientId && x.CurrencyCode == code);
            if ((policy?.Version ?? 0) != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The policy changed. Reload it.");
            if (policy is null) { policy = new ExpenseApprovalPolicy { ClientId = clientId, CurrencyCode = code }; db.ExpenseApprovalPolicies.Add(policy); }
            else db.Entry(policy).Property(x => x.Version).IsModified = true;
            policy.Threshold = request.Threshold; await db.SaveChangesAsync();
            return Results.Ok(new { clientId, currencyCode = code, configured = true, policy.Version, policy.Threshold });
        });
        return app;
    }
    internal static Task<ExpenseClaim?> Load(Guid id, AtlasDbContext db) => db.ExpenseClaims.Include(x => x.Contract).Include(x => x.Items).Include(x => x.Approvals).SingleOrDefaultAsync(x => x.Id == id);
    internal static async Task<IResult> Approve(Guid id, DecideExpenseClaimRequest? request, ClaimsPrincipal user, AtlasDbContext db, ExpenseApprovalService service)
    {
        var claim = await Load(id, db);
        if (claim is null || !user.CanViewClient(claim.Contract!.ClientId)) return Results.NotFound();
        if (!user.CanManageClient(claim.Contract!.ClientId)) return Results.Forbid();
        await service.ApproveAsync(claim, InvoiceLedgerEndpoints.Actor(user), request?.Note);
        return Results.Ok(ExpenseEndpoints.ToResponse(claim));
    }
}
public record ReplaceExpenseRequest(int ExpectedVersion, string? Description, List<CreateExpenseItemRequest>? Items);
public record ExpensePolicyRequest(decimal Threshold, int ExpectedVersion);
