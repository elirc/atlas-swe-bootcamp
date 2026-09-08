using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class InvoiceLedgerEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices/{invoiceId:guid}").RequireAuthorization();
        group.MapGet("/payments", async (Guid invoiceId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (await VisibleInvoice(invoiceId, user, db) is null) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            var payments = await db.InvoicePayments.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
                .OrderBy(x => x.PaidAtUtc).ThenBy(x => x.Id).ToPageAsync(http, paging);
            var ids = payments.Select(x => x.Id).ToList();
            var reversals = await db.InvoicePaymentReversals.AsNoTracking().Where(x => ids.Contains(x.PaymentId)).ToListAsync();
            return Results.Ok(payments.Select(x => PaymentView(x, reversals.SingleOrDefault(r => r.PaymentId == x.Id))));
        });
        group.MapGet("/payments/{paymentId:guid}", async (Guid invoiceId, Guid paymentId, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (await VisibleInvoice(invoiceId, user, db) is null) return Results.NotFound();
            var payment = await db.InvoicePayments.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoiceId && x.Id == paymentId);
            if (payment is null) return Results.NotFound();
            return Results.Ok(PaymentView(payment, await db.InvoicePaymentReversals.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == paymentId)));
        });
        group.MapPost("/payments", async (Guid invoiceId, RecordPaymentRequest request, ClaimsPrincipal user, AtlasDbContext db, InvoiceLedgerService ledger) =>
        {
            var invoice = await VisibleInvoice(invoiceId, user, db);
            if (invoice is null) return Results.NotFound();
            if (MoneyInput(request.Amount, request.CurrencyCode, request.Reference) is { } error) return error;
            var result = await ledger.PayAsync(invoice, Actor(user), request.Amount, request.CurrencyCode!.Trim().ToUpperInvariant(),
                request.Reference!.Trim(), request.PaidAtUtc);
            return result.Replay ? Results.Ok(result.Entry) : Results.Created($"/api/invoices/{invoiceId}/payments/{result.Entry.Id}", result.Entry);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapPost("/payments/{paymentId:guid}/reverse", async (Guid invoiceId, Guid paymentId, ReversePaymentRequest request,
            ClaimsPrincipal user, AtlasDbContext db, InvoiceLedgerService ledger) =>
        {
            var invoice = await VisibleInvoice(invoiceId, user, db);
            if (invoice is null || !await db.InvoicePayments.AnyAsync(x => x.Id == paymentId && x.InvoiceId == invoiceId)) return Results.NotFound();
            if (TextInput(request.Reference, request.Reason) is { } error) return error;
            var result = await ledger.ReverseAsync(invoice, paymentId, Actor(user), request.Reference!.Trim(), request.Reason!.Trim());
            return result.Replay ? Results.Ok(result.Entry) : Results.Created($"/api/invoices/{invoiceId}/payments/{paymentId}", result.Entry);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapGet("/balance", async (Guid invoiceId, DateOnly? asOf, ClaimsPrincipal user, AtlasDbContext db, InvoiceLedgerService ledger, TimeProvider clock) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var invoice = await VisibleInvoice(invoiceId, user, db);
            if (invoice is null) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (date.Year is < 2000 or > 2100) return ApiInput.Error("asOf", "Year must be between 2000 and 2100.");
            var cutoff = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            if (invoice.IssuedAtUtc > cutoff) return ApiInput.Error("asOf", "The invoice had not been issued on this date.");
            var facts = await ledger.LoadAsync(invoiceId);
            var paid = facts.Payments.Where(x => x.PaidAtUtc <= cutoff).Sum(x => x.Amount)
                - facts.Reversals.Where(x => x.RecordedAtUtc <= cutoff).Sum(x => facts.Payments.Single(p => p.Id == x.PaymentId).Amount);
            var credited = facts.Credits.Where(x => x.IssuedAtUtc <= cutoff).Sum(x => x.Amount);
            return Results.Ok(new { invoiceId, asOf = date, currencyCode = invoice.BillingCurrencyCode,
                invoiceTotal = invoice.TotalInBillingCurrency, netPaid = paid, credited, outstanding = facts.Outstanding(invoice, cutoff) });
        });
        group.MapGet("/credit-notes", async (Guid invoiceId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (await VisibleInvoice(invoiceId, user, db) is null) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok(await db.InvoiceCreditNotes.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
                .OrderBy(x => x.IssuedAtUtc).ThenBy(x => x.Id).ToPageAsync(http, paging));
        });
        group.MapGet("/credit-notes/{creditId:guid}", async (Guid invoiceId, Guid creditId, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (await VisibleInvoice(invoiceId, user, db) is null) return Results.NotFound();
            var credit = await db.InvoiceCreditNotes.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoiceId && x.Id == creditId);
            return credit is null ? Results.NotFound() : Results.Ok(credit);
        });
        group.MapPost("/credit-notes", async (Guid invoiceId, RecordCreditRequest request, ClaimsPrincipal user, AtlasDbContext db, InvoiceLedgerService ledger) =>
        {
            var invoice = await VisibleInvoice(invoiceId, user, db);
            if (invoice is null) return Results.NotFound();
            if (MoneyInput(request.Amount, request.CurrencyCode, request.Reference) is { } error) return error;
            if (TextInput(request.Reference, request.Reason) is { } textError) return textError;
            var result = await ledger.CreditAsync(invoice, Actor(user), request.Amount, request.CurrencyCode!.Trim().ToUpperInvariant(),
                request.Reference!.Trim(), request.Reason!.Trim());
            return result.Replay ? Results.Ok(result.Entry) : Results.Created($"/api/invoices/{invoiceId}/credit-notes/{result.Entry.Id}", result.Entry);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapGet("/credit-summary", async (Guid invoiceId, DateOnly? asOf, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var invoice = await VisibleInvoice(invoiceId, user, db);
            if (invoice is null) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (date.Year is < 2000 or > 2100) return ApiInput.Error("asOf", "Year must be between 2000 and 2100.");
            var cutoff = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            if (invoice.IssuedAtUtc > cutoff) return ApiInput.Error("asOf", "The invoice had not been issued on this date.");
            var credits = await db.InvoiceCreditNotes.Where(x => x.InvoiceId == invoiceId && x.IssuedAtUtc <= cutoff).Select(x => x.Amount).ToListAsync();
            return Results.Ok(new { invoiceId, asOf = date, currencyCode = invoice.BillingCurrencyCode,
                originalTotal = invoice.TotalInBillingCurrency, credited = credits.Sum(), netBilledAmount = invoice.TotalInBillingCurrency - credits.Sum() });
        });
        return app;
    }
    private static async Task<Invoice?> VisibleInvoice(Guid id, ClaimsPrincipal user, AtlasDbContext db)
    {
        var invoice = await db.Invoices.FindAsync(id);
        return invoice is not null && user.CanViewClient(invoice.ClientId) ? invoice : null;
    }
    private static object PaymentView(InvoicePayment p, InvoicePaymentReversal? reversal) => new
    { p.Id, p.InvoiceId, p.Amount, p.CurrencyCode, p.Reference, p.ActorApiUserId, p.PaidAtUtc, p.RecordedAtUtc, reversal, effectiveNetAmount = reversal is null ? p.Amount : 0m };
    internal static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static IResult? MoneyInput(decimal amount, string? currency, string? reference)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount) return ApiInput.Error("amount", "Amount must be positive with at most two decimal places.");
        if (currency is null || !ApiInput.IsAsciiCode(currency.Trim(), 3)) return ApiInput.Error("currencyCode", "Use three ASCII letters.");
        return string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100 ? ApiInput.Error("reference", "Reference must contain 1–100 characters.") : null;
    }
    internal static IResult? TextInput(string? reference, string? reason) =>
        string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100 ? ApiInput.Error("reference", "Reference must contain 1–100 characters.")
        : string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000 ? ApiInput.Error("reason", "Reason must contain 1–1,000 characters.") : null;
}
public record RecordPaymentRequest(decimal Amount, string? CurrencyCode, DateTimeOffset PaidAtUtc, string? Reference);
public record ReversePaymentRequest(string? Reference, string? Reason);
public record RecordCreditRequest(decimal Amount, string? CurrencyCode, string? Reference, string? Reason);
