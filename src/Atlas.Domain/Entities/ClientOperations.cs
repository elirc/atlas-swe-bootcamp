namespace Atlas.Domain.Entities;

public class ContractNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public Guid AuthorApiUserId { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class CountryOnboardingTemplate : IVersioned
{
    public required string CountryCode { get; set; }
    public int Version { get; set; } = 1;
    public List<OnboardingTemplateItem> Items { get; set; } = [];
}

public class OnboardingTemplateItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CountryCode { get; set; }
    public OnboardingItemType Type { get; set; }
    public required string Title { get; set; }
    public bool IsRequired { get; set; }
}

public class CountryHoliday
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CountryCode { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class LeaveAllowanceAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public int Year { get; set; }
    public LeaveType Type { get; set; }
    public int DeltaDays { get; set; }
    public required string Reason { get; set; }
    public required string Reference { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public enum SavedReportKind { Headcount, PayrollCosts, ComplianceExpiries, InvoiceAging }
public class ReportPreset : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerApiUserId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public SavedReportKind Kind { get; set; }
    public required string FiltersJson { get; set; }
    public int Version { get; set; }
}
