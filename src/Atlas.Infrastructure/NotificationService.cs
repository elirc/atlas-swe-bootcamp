using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public class NotificationService(DbContextOptions<AtlasDbContext> options, TimeProvider clock)
{
    public async Task<int> DispatchOnceAsync(CancellationToken ct = default)
    {
        await using var read = new AtlasDbContext(options);
        var events = await read.ContractDecisionEvents.AsNoTracking().Where(e => !read.ClientNotifications.Any(n => n.SourceEventId == e.Id && n.ClientId == e.ClientId))
            .OrderBy(x => x.Sequence).Take(100).ToListAsync(ct);
        var inserted = 0;
        foreach (var source in events)
        {
            await using var db = new AtlasDbContext(options);
            db.ClientNotifications.Add(new ClientNotification { ClientId = source.ClientId, SourceEventId = source.Id, SourceSequence = source.Sequence,
                Kind = source.Kind, Title = source.Kind switch { "activated" => "Contract activated", "terminated" => "Contract terminated", "amendment-applied" => "Contract amendment approved", _ => "Contract updated" },
                DetailPath = $"/api/contracts/{source.ContractId:D}", CreatedAtUtc = clock.GetUtcNow() });
            try { await db.SaveChangesAsync(ct); inserted++; }
            catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
            {
                await using var fresh = new AtlasDbContext(options);
                if (!await fresh.ClientNotifications.AnyAsync(x => x.ClientId == source.ClientId && x.SourceEventId == source.Id, ct)) throw;
            }
        }
        return inserted;
    }
}
