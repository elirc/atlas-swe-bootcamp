using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class BillingConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapBillingConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients/{clientId:guid}").RequireAuthorization();
        group.MapGet("/billing-terms", async (Guid clientId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok(await db.ClientBillingTerms.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.EffectiveDate).ThenBy(x => x.Id).ToPageAsync(http, paging));
        });
        group.MapPost("/billing-terms", async (Guid clientId, CreateBillingTermsRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (request.EffectiveDate.Year is < 2000 or > 2100 || request.EffectiveDate < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return ApiInput.Error("effectiveDate", "Choose today or a future date, with year 2000–2100.");
            if (request.TermDays is < 0 or > 120) return ApiInput.Error("termDays", "Term days must be between 0 and 120.");
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return ApiInput.Error("reason", "Reason must contain 1–500 characters.");
            if (await db.ClientBillingTerms.AnyAsync(x => x.ClientId == clientId && x.EffectiveDate == request.EffectiveDate))
                return Results.Problem(statusCode: 409, detail: "Billing terms already exist for this effective date.");
            var entry = new ClientBillingTerms { ClientId = clientId, EffectiveDate = request.EffectiveDate, TermDays = request.TermDays,
                Reason = request.Reason.Trim(), ActorApiUserId = InvoiceLedgerEndpoints.Actor(user), CreatedAtUtc = clock.GetUtcNow() };
            db.ClientBillingTerms.Add(entry); await db.SaveChangesAsync();
            return Results.Created($"/api/clients/{clientId}/billing-terms", entry);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapGet("/fee-schedule", async (Guid clientId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (!user.CanViewClient(clientId) || !await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok(await db.ClientFeeSchedules.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Id).ToPageAsync(http, paging));
        });
        group.MapPost("/fee-schedule", async (Guid clientId, CreateFeeScheduleRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await db.Clients.AnyAsync(x => x.Id == clientId)) return Results.NotFound();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (request.EffectiveYear is < 2000 or > 2100 || request.EffectiveMonth is < 1 or > 12
                || request.EffectiveYear * 100 + request.EffectiveMonth < today.Year * 100 + today.Month)
                return ApiInput.Error("effectiveYear", "Choose the current or a future month, with year 2000–2100.");
            if (request.Rate is < 0 or >= 1) return ApiInput.Error("rate", "Rate must be at least zero and less than one.");
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return ApiInput.Error("reason", "Reason must contain 1–500 characters.");
            if (await db.ClientFeeSchedules.AnyAsync(x => x.ClientId == clientId && x.Year == request.EffectiveYear && x.Month == request.EffectiveMonth))
                return Results.Problem(statusCode: 409, detail: "A fee rate already exists for this effective period.");
            var entry = new ClientFeeSchedule { ClientId = clientId, Year = request.EffectiveYear, Month = request.EffectiveMonth, Rate = request.Rate,
                Reason = request.Reason.Trim(), ActorApiUserId = InvoiceLedgerEndpoints.Actor(user), CreatedAtUtc = clock.GetUtcNow() };
            db.ClientFeeSchedules.Add(entry); await db.SaveChangesAsync();
            return Results.Created($"/api/clients/{clientId}/fee-schedule", entry);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapGet("/fee-schedule/effective", async (Guid clientId, int year, int month, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var client = await db.Clients.FindAsync(clientId);
            if (client is null || !user.CanViewClient(clientId)) return Results.NotFound();
            if (year is < 2000 or > 2100 || month is < 1 or > 12) return ApiInput.Error("year", "Use year 2000–2100 and month 1–12.");
            var schedules = await db.ClientFeeSchedules.AsNoTracking().Where(x => x.ClientId == clientId).ToListAsync();
            var selected = BillingConfiguration.Fee(client, schedules, year, month);
            return Results.Ok(new { clientId, year, month, selected.Rate, selected.ScheduleId, isFallback = selected.ScheduleId is null });
        });
        app.MapGet("/api/invoices/{id:guid}/due-status", async (Guid id, DateOnly? asOf, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (invoice is null || !user.CanViewClient(invoice.ClientId)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (date.Year is < 2000 or > 2100 || date < DateOnly.FromDateTime(invoice.IssuedAtUtc.UtcDateTime)) return ApiInput.Error("asOf", "Choose a date on or after issuance, with year 2000–2100.");
            return Results.Ok(new { invoiceId = id, asOf = date, invoice.PaymentTermDays, invoice.DueDate,
                status = invoice.DueDate is null ? "unknown-terms" : date < invoice.DueDate ? "not-yet-due" : date == invoice.DueDate ? "due-today" : "past-due-date" });
        }).RequireAuthorization();
        return app;
    }
}
public record CreateBillingTermsRequest(DateOnly EffectiveDate, int TermDays, string? Reason);
public record CreateFeeScheduleRequest(int EffectiveYear, int EffectiveMonth, decimal Rate, string? Reason);
