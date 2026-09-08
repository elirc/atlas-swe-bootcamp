using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api;

internal static class PayrollCompletion
{
    private const string Operation = "payroll.complete";
    public static async Task<IResult> ExecuteAsync(Guid runId, HttpContext http, ClaimsPrincipal user, AtlasDbContext db,
        PayrollService payroll, TimeProvider clock, DbContextOptions<AtlasDbContext> options)
    {
        var hasKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var values);
        var key = hasKey ? values.ToString().Trim() : null;
        if (hasKey && (values.Count != 1 || key!.Length is < 1 or > 100)) return ApiInput.Error("idempotencyKey", "Supply one key of 1–100 characters.");
        var owner = InvoiceLedgerEndpoints.Actor(user); var fingerprint = $"{Operation}:{runId:D}";
        if (key is not null)
        {
            var previous = await db.OperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerApiUserId == owner && x.Operation == Operation && x.Key == key);
            if (previous is not null) return Replay(previous, fingerprint);
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var (run, invoices) = await payroll.CompleteRunAsync(runId);
            var response = new PayrollRunCompletedResponse(PayrollEndpoints.ToSummary(run), invoices.Select(InvoiceEndpoints.ToResponse).ToList());
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (key is not null)
            {
                db.OperationReceipts.Add(new OperationReceipt { OwnerApiUserId = owner, Operation = Operation, Key = key,
                    Fingerprint = fingerprint, ResponseSchemaVersion = 1, ResponseJson = json, CreatedAtUtc = clock.GetUtcNow() });
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return Results.Content(json, "application/json", statusCode: 200);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (Exception ex) when (ex is DomainException || DatabaseConflicts.IsWriteConflict(ex))
        {
            await transaction.RollbackAsync();
            if (key is not null)
            {
                await using var fresh = new AtlasDbContext(options);
                var receipt = await fresh.OperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerApiUserId == owner && x.Operation == Operation && x.Key == key);
                if (receipt is not null) return Replay(receipt, fingerprint);
            }
            return Results.Problem(statusCode: 409, detail: ex is DomainException ? ex.Message : "Completion changed concurrently. Retry with the same idempotency key.");
        }
    }
    private static IResult Replay(OperationReceipt receipt, string fingerprint)
    {
        if (receipt.Fingerprint != fingerprint) return Results.Problem(statusCode: 409, detail: "This idempotency key already identifies a different completion command.");
        if (receipt.ResponseSchemaVersion != 1) return Results.Problem(statusCode: 409, detail: "The saved response version is unsupported.");
        return Results.Content(receipt.ResponseJson, "application/json", statusCode: 200);
    }
}
