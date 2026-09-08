using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ContractDecisionEndpoints
{
    public static IEndpointRouteBuilder MapContractDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        var clients = app.MapGroup("/api/clients/{clientId:guid}").RequireAuthorization();
        clients.MapGet("/contract-decisions", async (Guid clientId, long? afterSequence, int? limit, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            return await Feed(db.ContractDecisionEvents.Where(x => x.ClientId == clientId), afterSequence, limit);
        });
        app.MapGet("/api/contracts/{id:guid}/contract-decisions", async (Guid id, long? afterSequence, int? limit, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            return await Feed(db.ContractDecisionEvents.Where(x => x.ContractId == id && x.ClientId == contract.ClientId), afterSequence, limit);
        }).RequireAuthorization();
        clients.MapGet("/notifications", async (Guid clientId, long? beforeSequence, bool? unreadOnly, int? limit, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (limit is < 1 or > 100 || beforeSequence is < 1) return ApiInput.Error("limit", "Use limit 1–100 and a positive sequence bookmark.");
            var owner = InvoiceLedgerEndpoints.Actor(user); var size = limit ?? 25;
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var all = db.ClientNotifications.AsNoTracking().Where(x => x.ClientId == clientId);
            var unreadCount = await all.CountAsync(x => !db.NotificationReads.Any(r => r.NotificationId == x.Id && r.ApiUserId == owner));
            var query = all.Where(x => beforeSequence == null || x.SourceSequence < beforeSequence);
            if (unreadOnly == true) query = query.Where(x => !db.NotificationReads.Any(r => r.NotificationId == x.Id && r.ApiUserId == owner));
            var rows = await query.OrderByDescending(x => x.SourceSequence).Take(size + 1).Select(x => new { x.Id, x.SourceSequence, x.Kind, x.Title, x.DetailPath, x.CreatedAtUtc,
                isRead = db.NotificationReads.Any(r => r.NotificationId == x.Id && r.ApiUserId == owner) }).ToListAsync();
            var page = rows.Take(size).ToList();
            return Results.Ok(new { unreadCount, hasMore = rows.Count > size, nextBeforeSequence = page.LastOrDefault()?.SourceSequence, items = page });
        });
        clients.MapPost("/notifications/{notificationId:guid}/read", async (Guid clientId, Guid notificationId, ClaimsPrincipal user, AtlasDbContext db, DbContextOptions<AtlasDbContext> options, TimeProvider clock) =>
        {
            if (!user.CanViewClient(clientId) || !await db.ClientNotifications.AnyAsync(x => x.ClientId == clientId && x.Id == notificationId)) return Results.NotFound();
            var owner = InvoiceLedgerEndpoints.Actor(user);
            if (await db.NotificationReads.AnyAsync(x => x.NotificationId == notificationId && x.ApiUserId == owner)) return Results.Ok(new { notificationId, isRead = true });
            db.NotificationReads.Add(new NotificationRead { NotificationId = notificationId, ApiUserId = owner, ReadAtUtc = clock.GetUtcNow() });
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
            {
                await using var fresh = new AtlasDbContext(options);
                if (!await fresh.NotificationReads.AnyAsync(x => x.NotificationId == notificationId && x.ApiUserId == owner)) throw;
            }
            return Results.Ok(new { notificationId, isRead = true });
        });
        return app;
    }
    private static async Task<IResult> Feed(IQueryable<ContractDecisionEvent> query, long? after, int? limit)
    {
        if (after is < 0 || limit is < 1 or > 100) return ApiInput.Error("afterSequence", "Use a nonnegative sequence and limit 1–100.");
        var rows = await query.AsNoTracking().Where(x => x.Sequence > (after ?? 0)).OrderBy(x => x.Sequence).Take((limit ?? 50) + 1).ToListAsync();
        var page = rows.Take(limit ?? 50).ToList();
        return Results.Ok(new { hasMore = rows.Count > (limit ?? 50), nextAfterSequence = page.LastOrDefault()?.Sequence ?? after ?? 0,
            items = page.Select(x => new { x.Id, x.Sequence, x.ClientId, x.ContractId, x.ActorApiUserId, x.ActorRole, x.Kind, x.SourceKind, x.SourceId,
                x.ContractVersion, x.SchemaVersion, x.OccurredAtUtc, payload = JsonSerializer.Deserialize<JsonElement>(x.PayloadJson) }) });
    }
}
