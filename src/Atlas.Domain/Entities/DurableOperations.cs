namespace Atlas.Domain.Entities;

public class OperationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerApiUserId { get; set; }
    public required string Operation { get; set; }
    public required string Key { get; set; }
    public required string Fingerprint { get; set; }
    public required string ResponseJson { get; set; }
    public int ResponseSchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum BackgroundJobKind { PayrollCreate, ClientExport }
public enum BackgroundJobState { Queued, Running, Succeeded, Failed }

public class BackgroundJob : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public BackgroundJobKind Kind { get; set; }
    public required string OperationKey { get; set; }
    public required string Fingerprint { get; set; }
    public required string PayloadJson { get; set; }
    public Guid RequesterApiUserId { get; set; }
    public Guid? ClientId { get; set; }
    public BackgroundJobState State { get; set; }
    public int TotalAttempts { get; set; }
    public int CycleAttempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public Guid? ResultRunId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public void Claim(DateTimeOffset now)
    {
        if (State != BackgroundJobState.Queued && !(State == BackgroundJobState.Running && LeaseExpiresAtUtc <= now))
            throw new DomainException("This job cannot be claimed in its current state.");
        if (CycleAttempts >= 3) { Fail("attempt-limit", now); return; }
        State = BackgroundJobState.Running;
        LeaseToken = Guid.NewGuid(); LeaseExpiresAtUtc = now.AddSeconds(60);
        CycleAttempts++; TotalAttempts++; UpdatedAtUtc = now; ErrorCode = null;
    }

    public void RequireLease(Guid token, DateTimeOffset now)
    {
        if (State != BackgroundJobState.Running || LeaseToken != token || LeaseExpiresAtUtc is null || LeaseExpiresAtUtc <= now)
            throw new DomainException("The job lease is no longer owned by this worker.");
    }

    public void Succeed(Guid token, DateTimeOffset now, Guid? runId = null)
    {
        RequireLease(token, now); State = BackgroundJobState.Succeeded; ResultRunId = runId;
        LeaseToken = null; LeaseExpiresAtUtc = null; UpdatedAtUtc = now; ErrorCode = null;
    }

    public void Fail(string code, DateTimeOffset now)
    {
        State = BackgroundJobState.Failed; ErrorCode = code; LeaseToken = null; LeaseExpiresAtUtc = null; UpdatedAtUtc = now;
    }

    public void Retry(DateTimeOffset now)
    {
        if (State != BackgroundJobState.Failed) throw new DomainException("Only failed jobs can be retried.");
        State = BackgroundJobState.Queued; CycleAttempts = 0; ErrorCode = null; UpdatedAtUtc = now;
    }
}

public class ExportArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public byte[]? Bytes { get; set; }
    public required string Checksum { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public class PayslipCalculationEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PayslipId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string CalculatorVersion { get; set; }
    public required string EvidenceJson { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}

public enum WorkerImportStatus { Validated, Invalid, Committed }
public class WorkerImportBatch : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerApiUserId { get; set; }
    public int Version { get; set; }
    public WorkerImportStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public List<WorkerImportRow> Rows { get; set; } = [];
}

public class WorkerImportRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public int SourceRowNumber { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string CountryCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public required string ErrorsJson { get; set; }
    public Guid? CreatedWorkerId { get; set; }
}

public class ContractDecisionEvent
{
    public long Sequence { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid ContractId { get; set; }
    public Guid ActorApiUserId { get; set; }
    public required string ActorRole { get; set; }
    public required string Kind { get; set; }
    public required string SourceKind { get; set; }
    public Guid SourceId { get; set; }
    public int ContractVersion { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string PayloadJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public class ClientNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid SourceEventId { get; set; }
    public long SourceSequence { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string DetailPath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class NotificationRead
{
    public Guid NotificationId { get; set; }
    public Guid ApiUserId { get; set; }
    public DateTimeOffset ReadAtUtc { get; set; }
}
