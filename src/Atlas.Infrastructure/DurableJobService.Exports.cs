using System.Security.Cryptography;
using System.Text.Json;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public record ClientExportPayload(Guid ClientId, Guid OwnerApiUserId, int FromYear, int FromMonth, int ToYear, int ToMonth);
public record PreparedExport(byte[] Bytes, string Checksum, DateTimeOffset GeneratedAtUtc);

public partial class DurableJobService
{
    public async Task<(BackgroundJob Job, bool Created)> SubmitExportAsync(ClientExportPayload payload, string reference, CancellationToken ct = default)
    {
        var operation = $"client-export:{payload.OwnerApiUserId:D}:{payload.ClientId:D}:{reference}";
        var fingerprint = JsonSerializer.Serialize(payload);
        await using var db = new AtlasDbContext(options);
        var old = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operation, ct);
        if (old is not null) return old.Fingerprint == fingerprint ? (old, false) : throw new DomainException("This export reference was used for a different period.");
        var job = new BackgroundJob { Kind = BackgroundJobKind.ClientExport, OperationKey = operation, Fingerprint = fingerprint, PayloadJson = fingerprint,
            RequesterApiUserId = payload.OwnerApiUserId, ClientId = payload.ClientId, State = BackgroundJobState.Queued,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.BackgroundJobs.Add(job);
        try { await db.SaveChangesAsync(ct); return (job, true); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
        {
            await using var fresh = new AtlasDbContext(options);
            var winner = await fresh.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operation, ct);
            if (winner is not null && winner.Fingerprint == fingerprint) return (winner, false);
            throw new DomainException("Export submission competed with another request. Reload before retrying.");
        }
    }

    public static bool ExportAccess(ApiUser? user, Guid clientId, DateTimeOffset now) => user is { IsActive: true }
        && (user.ExpiresAtUtc is null || now < user.ExpiresAtUtc)
        && (user.Role == ApiRole.PlatformAdmin || user.Role == ApiRole.ClientAdmin && user.ClientId == clientId);

    public async Task<PreparedExport> PrepareExportAsync(ClaimedJob claimed, CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options);
        await using var snapshot = await SqliteReadSnapshot.BeginAsync(db, ct);
        var job = await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == claimed.Id, ct);
        job.RequireLease(claimed.Token, clock.GetUtcNow());
        if (job.Kind != BackgroundJobKind.ClientExport || job.ClientId is null) throw new DomainException("invalid-job-payload");
        var payload = JsonSerializer.Deserialize<ClientExportPayload>(job.PayloadJson) ?? throw new DomainException("invalid-job-payload");
        if (payload.ClientId != job.ClientId || payload.OwnerApiUserId != job.RequesterApiUserId) throw new DomainException("invalid-job-payload");
        var requester = await db.ApiUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.RequesterApiUserId, ct);
        var now = clock.GetUtcNow();
        if (!ExportAccess(requester, payload.ClientId, now)) throw new DomainException("export-access-revoked");
        var start = payload.FromYear * 12 + payload.FromMonth; var end = payload.ToYear * 12 + payload.ToMonth;
        if (payload.FromYear is < 2000 or > 2100 || payload.ToYear is < 2000 or > 2100 || payload.FromMonth is < 1 or > 12 || payload.ToMonth is < 1 or > 12 || end - start is < 0 or > 11)
            throw new DomainException("invalid-job-payload");
        var contracts = await db.Contracts.AsNoTracking().Where(x => x.ClientId == payload.ClientId).OrderBy(x => x.Id).Take(5001)
            .Select(x => new { x.Id, x.WorkerId, x.CountryCode, x.JobTitle, x.MonthlySalary, x.CurrencyCode, x.StartDate, x.EndDate, status = x.Status.ToString() }).ToListAsync(ct);
        var count = contracts.Count; CheckRows(count);
        var workerIds = contracts.Select(x => x.WorkerId).Distinct().ToList();
        var workers = await db.Workers.AsNoTracking().Where(x => workerIds.Contains(x.Id)).OrderBy(x => x.Id).Take(5001 - count)
            .Select(x => new { x.Id, x.FullName, x.Email }).ToListAsync(ct);
        count += workers.Count; CheckRows(count);
        var payslips = await db.Payslips.AsNoTracking().Where(x => x.ClientId == payload.ClientId && x.PayrollRun!.Year * 12 + x.PayrollRun.Month >= start
            && x.PayrollRun.Year * 12 + x.PayrollRun.Month <= end).OrderBy(x => x.Id).Take(5001 - count)
            .Select(x => new { x.Id, x.ContractId, x.WorkerId, x.PayrollRunId, x.PayrollRun!.Year, x.PayrollRun.Month,
                status = x.PayrollRun.Status.ToString(), x.CurrencyCode, x.GrossSalary, x.EmployerCost, x.EmployeeDeductions,
                x.Reimbursements, x.BenefitsEmployerCost, x.BenefitsEmployeeDeduction, x.UnusedLeavePayout, x.NetPay, x.TotalCost }).ToListAsync(ct);
        count += payslips.Count; CheckRows(count);
        var invoices = await db.Invoices.AsNoTracking().Where(x => x.ClientId == payload.ClientId && x.PayrollRun!.Year * 12 + x.PayrollRun.Month >= start
            && x.PayrollRun.Year * 12 + x.PayrollRun.Month <= end).OrderBy(x => x.Id).Take(5001 - count)
            .Select(x => new { x.Id, x.InvoiceNumber, x.PayrollRunId, x.PayrollRun!.Year, x.PayrollRun.Month, x.CurrencyCode,
                x.PayrollSubtotal, x.ManagementFee, x.Total, x.BillingCurrencyCode, x.FxRateApplied, x.TotalInBillingCurrency, x.IssuedAtUtc, x.DueDate }).ToListAsync(ct);
        count += invoices.Count; CheckRows(count);
        using var output = new LimitedExportStream();
        JsonSerializer.Serialize(output, new { schemaVersion = 1, clientId = payload.ClientId, generatedAtUtc = now,
            fromPeriod = $"{payload.FromYear}-{payload.FromMonth:00}", toPeriod = $"{payload.ToYear}-{payload.ToMonth:00}", contracts, workers, payslips, invoices }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = output.ToArray();
        return new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), now);
    }

    public async Task CommitExportAsync(ClaimedJob claimed, PreparedExport export, CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options); await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await FenceAsync(db, claimed, ct);
        if (job.Kind != BackgroundJobKind.ClientExport || job.ClientId is null) throw new DomainException("invalid-job-payload");
        if (!ExportAccess(await db.ApiUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.RequesterApiUserId, ct), job.ClientId.Value, clock.GetUtcNow()))
            throw new DomainException("export-access-revoked");
        if (export.Bytes.Length > 5 * 1024 * 1024) throw new DomainException("export-byte-limit");
        db.ExportArtifacts.Add(new ExportArtifact { JobId = job.Id, Bytes = export.Bytes, Checksum = export.Checksum,
            GeneratedAtUtc = export.GeneratedAtUtc, ExpiresAtUtc = export.GeneratedAtUtc.AddHours(24) });
        job.Succeed(claimed.Token, clock.GetUtcNow()); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task<int> ClearExpiredExportsAsync(CancellationToken ct = default)
    {
        await using var db = new AtlasDbContext(options); var now = clock.GetUtcNow();
        var ids = await db.ExportArtifacts.Where(x => x.ExpiresAtUtc <= now && x.Bytes != null).OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id).Take(100).Select(x => x.Id).ToListAsync(ct);
        return await db.ExportArtifacts.Where(x => ids.Contains(x.Id) && x.ExpiresAtUtc <= now).ExecuteUpdateAsync(update => update.SetProperty(x => x.Bytes, (byte[]?)null), ct);
    }
    private static void CheckRows(int count) { if (count > 5000) throw new DomainException("export-row-limit"); }
    private sealed class LimitedExportStream : MemoryStream
    {
        private void Check(int count) { if (Length + count > 5 * 1024 * 1024) throw new DomainException("export-byte-limit"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    }
}
