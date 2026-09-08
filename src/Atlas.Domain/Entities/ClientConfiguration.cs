namespace Atlas.Domain.Entities;

public class ClientCostCenter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public class ContractAllocationSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public List<ContractAllocationItem> Items { get; set; } = [];
}

public class ContractAllocationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }
    public Guid CostCenterId { get; set; }
    public int BasisPoints { get; set; }
}

public class PayslipCostAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PayslipId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? CostCenterId { get; set; }
    public required string BucketKey { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string CurrencyCode { get; set; }
    public int BasisPoints { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal EmployerCost { get; set; }
    public decimal EmployeeDeductions { get; set; }
    public decimal Reimbursements { get; set; }
    public decimal BenefitsEmployerCost { get; set; }
    public decimal BenefitsEmployeeDeduction { get; set; }
    public decimal UnusedLeavePayout { get; set; }
    public decimal NetPay { get; set; }
    public decimal TotalCost { get; set; }
}

public class CountryDocumentPolicy : IVersioned
{
    public required string CountryCode { get; set; }
    public int Version { get; set; } = 1;
    public List<CountryDocumentRequirement> Requirements { get; set; } = [];
}

public class CountryDocumentRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CountryCode { get; set; }
    public ComplianceDocumentType Type { get; set; }
    public int MinimumRemainingDays { get; set; }
    public bool ExpiryRequired { get; set; }
}

public class ClientPayrollBudget : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal Amount { get; set; }
    public int Version { get; set; } = 1;
    public Guid ModifiedByApiUserId { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
}

public enum ContractFieldKind { Text, Number, Choice }
public class ClientContractFieldDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public required string Key { get; set; }
    public required string Label { get; set; }
    public ContractFieldKind Kind { get; set; }
    public required string OptionsJson { get; set; }
}

public class ContractFieldValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public Guid DefinitionId { get; set; }
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public string? ChoiceCode { get; set; }
}

public class ExpenseApprovalPolicy : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal Threshold { get; set; }
    public int Version { get; set; } = 1;
}

public class ExpenseApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseClaimId { get; set; }
    public Guid ApproverApiUserId { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
}

public class WorkforceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid CreatorApiUserId { get; set; }
    public required string Label { get; set; }
    public DateOnly AsOf { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public int ContractCount { get; set; }
    public int WorkerCount { get; set; }
    public List<WorkforceSnapshotRow> Rows { get; set; } = [];
}

public class WorkforceSnapshotRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SnapshotId { get; set; }
    public Guid ContractId { get; set; }
    public Guid WorkerId { get; set; }
    public required string WorkerName { get; set; }
    public required string CountryCode { get; set; }
    public required string JobTitle { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
