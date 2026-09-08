using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public partial class AtlasDbContext
{
    public DbSet<ClientCostCenter> ClientCostCenters => Set<ClientCostCenter>();
    public DbSet<ContractAllocationSchedule> ContractAllocationSchedules => Set<ContractAllocationSchedule>();
    public DbSet<PayslipCostAllocation> PayslipCostAllocations => Set<PayslipCostAllocation>();
    public DbSet<WorkerImportBatch> WorkerImportBatches => Set<WorkerImportBatch>();
    public DbSet<ContractDecisionEvent> ContractDecisionEvents => Set<ContractDecisionEvent>();
    public DbSet<ClientNotification> ClientNotifications => Set<ClientNotification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<WorkforceSnapshot> WorkforceSnapshots => Set<WorkforceSnapshot>();

    private static void ConfigureAdvancedModels(ModelBuilder b)
    {
        b.Entity<ClientCostCenter>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientCostCenter>().HasIndex(x => new { x.ClientId, x.Code }).IsUnique();
        b.Entity<ContractAllocationSchedule>().HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractAllocationSchedule>().HasIndex(x => new { x.ContractId, x.Year, x.Month }).IsUnique();
        b.Entity<ContractAllocationSchedule>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ContractAllocationItem>().HasOne<ClientCostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractAllocationItem>().HasIndex(x => new { x.ScheduleId, x.CostCenterId }).IsUnique();
        b.Entity<PayslipCostAllocation>().HasOne<Payslip>().WithMany().HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayslipCostAllocation>().HasOne<ContractAllocationSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayslipCostAllocation>().HasOne<ClientCostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayslipCostAllocation>().HasIndex(x => new { x.PayslipId, x.BucketKey }).IsUnique();
        b.Entity<WorkerImportBatch>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.OwnerApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkerImportBatch>().HasMany(x => x.Rows).WithOne().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkerImportRow>().HasOne<Worker>().WithMany().HasForeignKey(x => x.CreatedWorkerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkerImportRow>().HasIndex(x => new { x.BatchId, x.SourceRowNumber }).IsUnique();
        b.Entity<ContractDecisionEvent>().HasKey(x => x.Sequence);
        b.Entity<ContractDecisionEvent>().Property(x => x.Sequence).ValueGeneratedOnAdd();
        b.Entity<ContractDecisionEvent>().HasAlternateKey(x => x.Id);
        b.Entity<ContractDecisionEvent>().HasIndex(x => new { x.ContractId, x.ContractVersion, x.Kind }).IsUnique();
        b.Entity<ContractDecisionEvent>().HasIndex(x => new { x.ClientId, x.Sequence });
        b.Entity<ContractDecisionEvent>().HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractDecisionEvent>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.ActorApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientNotification>().HasOne<ContractDecisionEvent>().WithMany().HasForeignKey(x => x.SourceEventId).HasPrincipalKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientNotification>().HasIndex(x => new { x.ClientId, x.SourceEventId }).IsUnique();
        b.Entity<ClientNotification>().HasIndex(x => new { x.ClientId, x.SourceSequence });
        b.Entity<NotificationRead>().HasKey(x => new { x.NotificationId, x.ApiUserId });
        b.Entity<NotificationRead>().HasOne<ClientNotification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NotificationRead>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.ApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkforceSnapshot>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkforceSnapshot>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.CreatorApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkforceSnapshot>().HasMany(x => x.Rows).WithOne().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkforceSnapshotRow>().HasIndex(x => new { x.SnapshotId, x.ContractId }).IsUnique();
    }
}
