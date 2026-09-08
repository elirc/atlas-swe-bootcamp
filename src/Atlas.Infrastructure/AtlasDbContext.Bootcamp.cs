using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public partial class AtlasDbContext
{
    public DbSet<ContractNote> ContractNotes => Set<ContractNote>();
    public DbSet<CountryOnboardingTemplate> CountryOnboardingTemplates => Set<CountryOnboardingTemplate>();
    public DbSet<CountryHoliday> CountryHolidays => Set<CountryHoliday>();
    public DbSet<LeaveAllowanceAdjustment> LeaveAllowanceAdjustments => Set<LeaveAllowanceAdjustment>();
    public DbSet<ReportPreset> ReportPresets => Set<ReportPreset>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<InvoicePaymentReversal> InvoicePaymentReversals => Set<InvoicePaymentReversal>();
    public DbSet<InvoiceCreditNote> InvoiceCreditNotes => Set<InvoiceCreditNote>();
    public DbSet<ClientBillingTerms> ClientBillingTerms => Set<ClientBillingTerms>();
    public DbSet<ClientFeeSchedule> ClientFeeSchedules => Set<ClientFeeSchedule>();
    public DbSet<ExpenseApprovalPolicy> ExpenseApprovalPolicies => Set<ExpenseApprovalPolicy>();
    public DbSet<ExpenseApproval> ExpenseApprovals => Set<ExpenseApproval>();
    public DbSet<PayslipCalculationEvidence> PayslipCalculationEvidence => Set<PayslipCalculationEvidence>();
    public DbSet<OperationReceipt> OperationReceipts => Set<OperationReceipt>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<ExportArtifact> ExportArtifacts => Set<ExportArtifact>();
    public DbSet<CountryDocumentPolicy> CountryDocumentPolicies => Set<CountryDocumentPolicy>();
    public DbSet<ClientPayrollBudget> ClientPayrollBudgets => Set<ClientPayrollBudget>();
    public DbSet<ClientContractFieldDefinition> ClientContractFieldDefinitions => Set<ClientContractFieldDefinition>();
    public DbSet<ContractFieldValue> ContractFieldValues => Set<ContractFieldValue>();

    private static void ConfigureBootcampModels(ModelBuilder b)
    {
        ConfigureAdvancedModels(b);
        b.Entity<ComplianceDocument>().HasOne<ComplianceDocument>().WithMany().HasForeignKey(x => x.SupersedesDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ComplianceDocument>().HasIndex(x => x.SupersedesDocumentId).IsUnique().HasFilter("SupersedesDocumentId IS NOT NULL");
        b.Entity<CountryDocumentPolicy>().HasKey(x => x.CountryCode);
        b.Entity<CountryDocumentPolicy>().HasOne<Country>().WithMany().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CountryDocumentPolicy>().HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CountryDocumentRequirement>().HasIndex(x => new { x.CountryCode, x.Type }).IsUnique();
        b.Entity<ClientPayrollBudget>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientPayrollBudget>().HasIndex(x => new { x.ClientId, x.Year, x.Month, x.CurrencyCode }).IsUnique();
        b.Entity<ClientContractFieldDefinition>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientContractFieldDefinition>().HasIndex(x => new { x.ClientId, x.Key }).IsUnique();
        b.Entity<ContractFieldValue>().HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractFieldValue>().HasOne<ClientContractFieldDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractFieldValue>().HasIndex(x => new { x.ContractId, x.DefinitionId }).IsUnique();
        b.Entity<ContractFieldValue>().ToTable(t => t.HasCheckConstraint("CK_ContractFieldValue_OneValue", "(TextValue IS NOT NULL) + (NumberValue IS NOT NULL) + (ChoiceCode IS NOT NULL) = 1"));
        b.Entity<PayslipCalculationEvidence>().HasOne<Payslip>().WithMany().HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayslipCalculationEvidence>().HasIndex(x => x.PayslipId).IsUnique();
        b.Entity<OperationReceipt>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.OwnerApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OperationReceipt>().HasIndex(x => new { x.OwnerApiUserId, x.Operation, x.Key }).IsUnique();
        b.Entity<BackgroundJob>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.RequesterApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BackgroundJob>().HasIndex(x => x.OperationKey).IsUnique();
        b.Entity<BackgroundJob>().HasIndex(x => new { x.State, x.LeaseExpiresAtUtc });
        b.Entity<ExportArtifact>().HasOne<BackgroundJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExportArtifact>().HasIndex(x => x.JobId).IsUnique();
        b.Entity<ExpenseApprovalPolicy>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseApprovalPolicy>().HasIndex(x => new { x.ClientId, x.CurrencyCode }).IsUnique();
        b.Entity<ExpenseClaim>().HasMany(x => x.Approvals).WithOne().HasForeignKey(x => x.ExpenseClaimId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseClaim>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.SubmittedByApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseApproval>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.ApproverApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseApproval>().HasIndex(x => new { x.ExpenseClaimId, x.ApproverApiUserId }).IsUnique();
        b.Entity<ContractNote>().HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractNote>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.AuthorApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContractNote>().HasIndex(x => new { x.ContractId, x.CreatedAtUtc, x.Id });
        b.Entity<CountryOnboardingTemplate>().HasKey(x => x.CountryCode);
        b.Entity<CountryOnboardingTemplate>().HasOne<Country>().WithMany().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CountryOnboardingTemplate>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OnboardingTemplateItem>().HasIndex(x => new { x.CountryCode, x.Type }).IsUnique();
        b.Entity<CountryHoliday>().HasOne<Country>().WithMany().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CountryHoliday>().HasIndex(x => new { x.CountryCode, x.Date }).IsUnique();
        b.Entity<LeaveAllowanceAdjustment>().HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LeaveAllowanceAdjustment>().HasIndex(x => new { x.ContractId, x.Year, x.Type, x.Reference }).IsUnique();
        b.Entity<ReportPreset>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.OwnerApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReportPreset>().HasIndex(x => new { x.OwnerApiUserId, x.NormalizedName }).IsUnique();
        b.Entity<ApiUser>().HasIndex(x => x.ReplacesApiUserId).IsUnique();
        b.Entity<ApiUser>().HasOne<ApiUser>().WithMany().HasForeignKey(x => x.ReplacesApiUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InvoicePayment>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InvoicePayment>().HasIndex(x => new { x.InvoiceId, x.Reference }).IsUnique();
        b.Entity<InvoicePaymentReversal>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InvoicePaymentReversal>().HasOne<InvoicePayment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InvoicePaymentReversal>().HasIndex(x => x.PaymentId).IsUnique();
        b.Entity<InvoicePaymentReversal>().HasIndex(x => new { x.InvoiceId, x.Reference }).IsUnique();
        b.Entity<InvoiceCreditNote>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InvoiceCreditNote>().HasIndex(x => new { x.InvoiceId, x.Reference }).IsUnique();
        b.Entity<ClientBillingTerms>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientBillingTerms>().HasIndex(x => new { x.ClientId, x.EffectiveDate }).IsUnique();
        b.Entity<ClientFeeSchedule>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClientFeeSchedule>().HasIndex(x => new { x.ClientId, x.Year, x.Month }).IsUnique();
        b.Entity<Invoice>().HasOne<ClientBillingTerms>().WithMany().HasForeignKey(x => x.BillingTermsId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Invoice>().HasOne<ClientFeeSchedule>().WithMany().HasForeignKey(x => x.FeeScheduleId).OnDelete(DeleteBehavior.Restrict);
    }
}
