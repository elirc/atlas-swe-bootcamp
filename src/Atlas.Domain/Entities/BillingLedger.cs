namespace Atlas.Domain.Entities;

public class InvoicePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public required string Reference { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public class InvoicePaymentReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid PaymentId { get; set; }
    public required string Reference { get; set; }
    public required string Reason { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public class InvoiceCreditNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public required string Reference { get; set; }
    public required string Reason { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
}

public class ClientBillingTerms
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public int TermDays { get; set; }
    public required string Reason { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ClientFeeSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Rate { get; set; }
    public required string Reason { get; set; }
    public Guid ActorApiUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
