using Atlas.Domain;
using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public class ExpenseApprovalService(AtlasDbContext db, TimeProvider clock)
{
    public async Task CapturePolicyAsync(ExpenseClaim claim, Guid clientId)
    {
        var policy = await db.ExpenseApprovalPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.CurrencyCode == claim.CurrencyCode);
        claim.RequiredApprovals = policy is not null && claim.TotalAmount >= policy.Threshold ? 2 : 1;
        claim.PolicyVersionApplied = policy?.Version;
        claim.TwoApprovalThresholdApplied = policy?.Threshold;
    }

    public async Task ApproveAsync(ExpenseClaim claim, Guid actor, string? note)
    {
        if (claim.Status != ExpenseClaimStatus.Pending) throw new DomainException($"Only pending expense claims can be approved; this claim is {claim.Status}.");
        if (claim.Approvals.Any(x => x.ApproverApiUserId == actor)) throw new DomainException("This API identity has already approved this claim.");
        if (claim.RequiredApprovals == 2 && claim.SubmittedByApiUserId == actor) throw new DomainException("The submitter cannot approve a claim requiring two approvals.");
        var now = clock.GetUtcNow();
        var vote = new ExpenseApproval { ExpenseClaimId = claim.Id, ApproverApiUserId = actor, ApprovedAtUtc = now };
        claim.Approvals.Add(vote);
        db.ExpenseApprovals.Add(vote);
        if (claim.Approvals.Count >= claim.RequiredApprovals) claim.Approve(now, note);
        db.Entry(claim).Property(x => x.Version).IsModified = true;
        await db.SaveChangesAsync();
    }
}
