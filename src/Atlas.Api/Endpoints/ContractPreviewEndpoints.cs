using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ContractPreviewEndpoints
{
    public static IEndpointRouteBuilder MapContractPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contracts/{contractId:guid}").RequireAuthorization();
        group.MapPost("/termination-preview", async (Guid contractId, TerminationPreviewRequest request, ClaimsPrincipal user, AtlasDbContext db, ContractProposalService proposals) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (request.ProposedEndDate.Year is < 2000 or > 2100) return ApiInput.Error("proposedEndDate", "Use a date with year 2000–2100.");
            return Results.Ok(await proposals.TerminationPreviewAsync(contract, request.ProposedEndDate));
        });
        group.MapPost("/amendment-preview", async (Guid contractId, AmendmentPreviewRequest request, ClaimsPrincipal user, AtlasDbContext db, ContractProposalService proposals) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            var errors = AmendmentInput(request.NewMonthlySalary, request.NewJobTitle, request.EffectiveDate);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            await proposals.CheckAmendmentAsync(contract, request.EffectiveDate);
            var salary = request.NewMonthlySalary ?? contract.MonthlySalary;
            var title = string.IsNullOrWhiteSpace(request.NewJobTitle) ? contract.JobTitle : request.NewJobTitle.Trim();
            var delta = salary - contract.MonthlySalary;
            return Results.Ok(new { contractId, currentMonthlySalary = contract.MonthlySalary, currentJobTitle = contract.JobTitle,
                proposedMonthlySalary = salary, proposedJobTitle = title, monthlySalaryDelta = delta,
                percentageDelta = decimal.Round(delta / contract.MonthlySalary * 100m, 2, MidpointRounding.AwayFromZero),
                contract.CurrencyCode, request.EffectiveDate, contract.Version, comparisonBasis = "current-contract-terms" });
        });
        return app;
    }

    internal static Dictionary<string, string[]> AmendmentInput(decimal? salary, string? title, DateOnly effectiveDate)
    {
        var errors = new Dictionary<string, string[]>();
        if (salary is null && string.IsNullOrWhiteSpace(title)) errors["newMonthlySalary"] = ["An amendment must supply salary, title, or both."];
        if (salary is <= 0) errors["newMonthlySalary"] = ["NewMonthlySalary must be greater than zero."];
        if (title?.Trim().Length > 200) errors["newJobTitle"] = ["Title must contain at most 200 characters."];
        if (effectiveDate.Year is < 2000 or > 2100) errors["effectiveDate"] = ["Use an effective date with year 2000–2100."];
        return errors;
    }
}
public record TerminationPreviewRequest(DateOnly ProposedEndDate);
public record AmendmentPreviewRequest(decimal? NewMonthlySalary, string? NewJobTitle, DateOnly EffectiveDate);
