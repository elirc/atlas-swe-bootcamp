using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public record InvoiceLedgerSnapshot(List<InvoicePayment> Payments, List<InvoicePaymentReversal> Reversals, List<InvoiceCreditNote> Credits)
{
    public List<LedgerMovement> Movements => Payments.Select(x => new LedgerMovement(x.PaidAtUtc, -x.Amount))
        .Concat(Reversals.Select(x => new LedgerMovement(x.RecordedAtUtc, Payments.Single(p => p.Id == x.PaymentId).Amount)))
        .Concat(Credits.Select(x => new LedgerMovement(x.IssuedAtUtc, -x.Amount))).ToList();
    public decimal Outstanding(Invoice invoice, DateTimeOffset cutoff) => InvoiceLedger.Outstanding(invoice.TotalInBillingCurrency, Movements, cutoff);
}

/// <summary>Append-only billing movements. Every writer touches the same invoice concurrency token.</summary>
public class InvoiceLedgerService(AtlasDbContext db, TimeProvider clock, DbContextOptions<AtlasDbContext> options)
{
    public async Task<InvoiceLedgerSnapshot> LoadAsync(Guid invoiceId) => new(
        await db.InvoicePayments.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(),
        await db.InvoicePaymentReversals.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(),
        await db.InvoiceCreditNotes.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync());

    public async Task<(InvoicePayment Entry, bool Replay)> PayAsync(Invoice invoice, Guid actor, decimal amount, string currency,
        string reference, DateTimeOffset paidAt)
    {
        var ledger = await LoadAsync(invoice.Id);
        var old = ledger.Payments.SingleOrDefault(x => x.Reference == reference);
        if (old is not null)
        {
            if (old.Amount != amount || old.CurrencyCode != currency || old.PaidAtUtc != paidAt)
                throw new DomainException("This payment reference already identifies a different payment.");
            return (old, true);
        }
        if (currency != invoice.BillingCurrencyCode) throw new DomainException("Payment currency must match the invoice billing currency.");
        if (paidAt < invoice.IssuedAtUtc || paidAt > clock.GetUtcNow()) throw new DomainException("Payment time must fall between issue time and now.");
        var payment = new InvoicePayment { InvoiceId = invoice.Id, ActorApiUserId = actor, Amount = amount, CurrencyCode = currency,
            Reference = reference, PaidAtUtc = paidAt.ToUniversalTime(), RecordedAtUtc = clock.GetUtcNow() };
        ledger.Payments.Add(payment);
        InvoiceLedger.ValidateHistory(invoice.TotalInBillingCurrency, ledger.Movements);
        db.InvoicePayments.Add(payment);
        db.Entry(invoice).Property(x => x.Version).IsModified = true;
        try { await db.SaveChangesAsync(); return (payment, false); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
        {
            await using var fresh = new AtlasDbContext(options);
            var replay = await fresh.InvoicePayments.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id && x.Reference == reference);
            if (replay is not null && replay.Amount == amount && replay.CurrencyCode == currency && replay.PaidAtUtc == paidAt) return (replay, true);
            throw new DomainException("The invoice ledger changed. Reload its balance before retrying.");
        }
    }

    public async Task<(InvoicePaymentReversal Entry, bool Replay)> ReverseAsync(Invoice invoice, Guid paymentId, Guid actor, string reference, string reason)
    {
        var ledger = await LoadAsync(invoice.Id);
        var old = ledger.Reversals.SingleOrDefault(x => x.Reference == reference);
        if (old is not null)
        {
            if (old.PaymentId != paymentId || old.Reason != reason) throw new DomainException("This reversal reference already identifies a different reversal.");
            return (old, true);
        }
        if (!ledger.Payments.Any(x => x.Id == paymentId)) throw new DomainException("The payment does not belong to this invoice.");
        if (ledger.Reversals.Any(x => x.PaymentId == paymentId)) throw new DomainException("The payment is already reversed.");
        var reversal = new InvoicePaymentReversal { InvoiceId = invoice.Id, PaymentId = paymentId, ActorApiUserId = actor,
            Reference = reference, Reason = reason, RecordedAtUtc = clock.GetUtcNow() };
        db.InvoicePaymentReversals.Add(reversal);
        db.Entry(invoice).Property(x => x.Version).IsModified = true;
        try { await db.SaveChangesAsync(); return (reversal, false); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
        {
            await using var fresh = new AtlasDbContext(options);
            var replay = await fresh.InvoicePaymentReversals.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id && x.Reference == reference);
            if (replay is not null && replay.PaymentId == paymentId && replay.Reason == reason) return (replay, true);
            throw new DomainException("The invoice ledger changed. Reload its balance before retrying.");
        }
    }

    public async Task<(InvoiceCreditNote Entry, bool Replay)> CreditAsync(Invoice invoice, Guid actor, decimal amount, string currency, string reference, string reason)
    {
        var ledger = await LoadAsync(invoice.Id);
        var old = ledger.Credits.SingleOrDefault(x => x.Reference == reference);
        if (old is not null)
        {
            if (old.Amount != amount || old.CurrencyCode != currency || old.Reason != reason)
                throw new DomainException("This credit reference already identifies a different credit.");
            return (old, true);
        }
        if (currency != invoice.BillingCurrencyCode) throw new DomainException("Credit currency must match the invoice billing currency.");
        var credit = new InvoiceCreditNote { InvoiceId = invoice.Id, ActorApiUserId = actor, Amount = amount, CurrencyCode = currency,
            Reference = reference, Reason = reason, IssuedAtUtc = clock.GetUtcNow() };
        ledger.Credits.Add(credit);
        InvoiceLedger.ValidateHistory(invoice.TotalInBillingCurrency, ledger.Movements);
        db.InvoiceCreditNotes.Add(credit);
        db.Entry(invoice).Property(x => x.Version).IsModified = true;
        try { await db.SaveChangesAsync(); return (credit, false); }
        catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
        {
            await using var fresh = new AtlasDbContext(options);
            var replay = await fresh.InvoiceCreditNotes.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id && x.Reference == reference);
            if (replay is not null && replay.Amount == amount && replay.CurrencyCode == currency && replay.Reason == reason) return (replay, true);
            throw new DomainException("The invoice ledger changed. Reload its balance before retrying.");
        }
    }
}
