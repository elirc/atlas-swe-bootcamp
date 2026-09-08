using System.Text.Json;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public record PayrollJobPayload(string CountryCode, int Year, int Month);
public record ClaimedJob(Guid Id, Guid Token, BackgroundJobKind Kind, string PayloadJson);

/// <summary>Every phase uses its own context; a final database fence protects all effects.</summary>
public partial class DurableJobService(DbContextOptions<AtlasDbContext> options, TimeProvider clock)
{
    public async Task<(BackgroundJob Job, bool Created)> SubmitPayrollAsync(string country, int year, int month, Guid requester, CancellationToken ct = default)
    {
        var operation = $"payroll-create:{country}:{year}-{month:00}";
        await using var db = new AtlasDbContext(options);
        var old = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operation, ct);
        if (old is not null) return (old, false);
        var job = new BackgroundJob { Kind = BackgroundJobKind.PayrollCreate, OperationKey = operation, Fingerprint = operation,
            PayloadJson = JsonSerializer.Serialize(new PayrollJobPayload(country, year, month)), RequesterApiUserId = requester,
            State = BackgroundJobState.Queued, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.BackgroundJobs.Add(job);
        try { await db.SaveChangesAsync(ct); return (job, true); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
        {
            await using var fresh = new AtlasDbContext(options);
            var winner = await fresh.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operation, ct);
            if (winner is not null) return (winner, false);
            throw;
        }
    }

    public async Task<ClaimedJob?> ClaimAsync(Guid? id = null, CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options);
        var now = clock.GetUtcNow();
        var job = await db.BackgroundJobs.Where(x => (id == null || x.Id == id)
            && (x.State == BackgroundJobState.Queued || x.State == BackgroundJobState.Running && x.LeaseExpiresAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (job is null) return null;
        job.Claim(clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex)) { return null; }
        return job.State == BackgroundJobState.Running ? new(job.Id, job.LeaseToken!.Value, job.Kind, job.PayloadJson) : null;
    }

    public async Task<bool> RenewAsync(Guid id, Guid token, CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options); var now = clock.GetUtcNow();
        return await db.BackgroundJobs.Where(x => x.Id == id && x.State == BackgroundJobState.Running && x.LeaseToken == token && x.LeaseExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAtUtc, now.AddSeconds(60))
                .SetProperty(x => x.UpdatedAtUtc, now).SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }

    public async Task<Guid> CommitPayrollAsync(ClaimedJob claimed, PayrollAssessment assessment, CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await FenceAsync(db, claimed, ct);
        var run = await new PayrollService(db, clock).PrepareRunAsync(assessment);
        job.Succeed(claimed.Token, clock.GetUtcNow(), run.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return run.Id;
    }

    private async Task<BackgroundJob> FenceAsync(AtlasDbContext db, ClaimedJob claimed, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // This conditional write takes the write lock before any payroll/artifact effects.
        var changed = await db.BackgroundJobs.Where(x => x.Id == claimed.Id && x.State == BackgroundJobState.Running
                && x.LeaseToken == claimed.Token && x.LeaseExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new DomainException("job-lease-lost");
        return await db.BackgroundJobs.SingleAsync(x => x.Id == claimed.Id, ct);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await ClearExpiredExportsAsync(ct);
        var claimed = await ClaimAsync(ct: ct);
        if (claimed is null) return false;
        using var renewCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renewer = RenewLoopAsync(claimed, renewCancellation.Token);
        try
        {
            if (claimed.Kind == BackgroundJobKind.ClientExport)
            {
                var artifact = await PrepareExportAsync(claimed, ct);
                await StopRenewalAsync(renewCancellation, renewer);
                if (await RenewAsync(claimed.Id, claimed.Token, ct)) await CommitExportAsync(claimed, artifact, ct);
                return true;
            }
            if (claimed.Kind != BackgroundJobKind.PayrollCreate) throw new DomainException("unsupported-job-kind");
            var payload = JsonSerializer.Deserialize<PayrollJobPayload>(claimed.PayloadJson) ?? throw new DomainException("invalid-job-payload");
            PayrollAssessment assessment;
            await using (var reading = new AtlasDbContext(options))
            {
                await using var snapshot = await SqliteReadSnapshot.BeginAsync(reading, ct);
                assessment = await new PayrollAssessmentService(reading, clock).AssessAsync(payload.CountryCode, payload.Year, payload.Month, ct);
            }
            await StopRenewalAsync(renewCancellation, renewer);
            if (!await RenewAsync(claimed.Id, claimed.Token, ct)) return true;
            await CommitPayrollAsync(claimed, assessment, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Lease recovery handles interrupted work. */ }
        catch (Exception ex)
        {
            await StopRenewalAsync(renewCancellation, renewer);
            if (ex is DomainException { Message: "job-lease-lost" }) return true;
            var transient = ex is SqliteException { SqliteErrorCode: 5 or 6 or 10 }
                || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 or 10 } }
                || DatabaseConflicts.IsWriteConflict(ex);
            var code = ex is DomainException && ex.Message is "export-access-revoked" or "export-row-limit" or "export-byte-limit" or "invalid-job-payload" or "unsupported-job-kind"
                ? ex.Message : ex is DomainException ? "business-rule" : "processing-error";
            await FailOwnedAsync(claimed, transient, code, ct);
        }
        finally { await StopRenewalAsync(renewCancellation, renewer); }
        return true;
    }

    private async Task RenewLoopAsync(ClaimedJob job, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (!await RenewAsync(job.Id, job.Token, ct)) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { /* Final fencing still rejects lost or expired ownership. */ }
    }
    private static async Task StopRenewalAsync(CancellationTokenSource source, Task task)
    { await source.CancelAsync(); await task; }

    private async Task FailOwnedAsync(ClaimedJob claimed, bool transient, string code, CancellationToken ct)
    {
        await using var db = new AtlasDbContext(options); var now = clock.GetUtcNow();
        var job = await db.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == claimed.Id && x.State == BackgroundJobState.Running
            && x.LeaseToken == claimed.Token && x.LeaseExpiresAtUtc > now, ct);
        if (job is null) return;
        Guid? existingRun = null;
        if (claimed.Kind == BackgroundJobKind.PayrollCreate)
        {
            var payload = JsonSerializer.Deserialize<PayrollJobPayload>(claimed.PayloadJson);
            if (payload is not null) existingRun = await db.PayrollRuns.Where(x => x.CountryCode == payload.CountryCode && x.Year == payload.Year && x.Month == payload.Month)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        }
        if (existingRun is not null) { job.Fail("run-already-exists", now); job.ResultRunId = existingRun; }
        else if (transient && job.CycleAttempts < 3)
        {
            job.State = BackgroundJobState.Queued; job.LeaseToken = null; job.LeaseExpiresAtUtc = null;
            job.ErrorCode = "transient-retry"; job.UpdatedAtUtc = now;
        }
        else job.Fail(transient ? "attempt-limit" : code, now);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex)) { /* A new owner/state wins. */ }
    }
}
