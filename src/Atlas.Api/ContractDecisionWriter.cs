using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api;

public record ContractDecisionValues(string Status, decimal MonthlySalary, string JobTitle, DateOnly StartDate, DateOnly? EndDate);
public static class ContractDecisionWriter
{
    public static ContractDecisionValues Capture(EmploymentContract c) => new(c.Status.ToString(), c.MonthlySalary, c.JobTitle, c.StartDate, c.EndDate);
    public static async Task SaveAsync(AtlasDbContext db, EmploymentContract contract, ContractDecisionValues before,
        ClaimsPrincipal actor, TimeProvider clock, string kind, string sourceKind, Guid sourceId, DateOnly effectiveDate)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Even an approved amendment with identical values is a decision on this aggregate.
        db.Entry(contract).Property(x => x.Version).IsModified = true;
        await db.SaveChangesAsync();
        var json = JsonSerializer.Serialize(new { before, after = Capture(contract), effectiveDate }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(json) > 8192) throw new DomainException("Contract decision evidence exceeds 8 KiB.");
        db.ContractDecisionEvents.Add(new ContractDecisionEvent { ClientId = contract.ClientId, ContractId = contract.Id,
            ActorApiUserId = InvoiceLedgerEndpoints.Actor(actor), ActorRole = actor.FindFirstValue(ClaimTypes.Role)!,
            Kind = kind, SourceKind = sourceKind, SourceId = sourceId, ContractVersion = contract.Version,
            PayloadJson = json, OccurredAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
