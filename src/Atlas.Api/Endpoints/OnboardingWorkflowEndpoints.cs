using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class OnboardingWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var contracts = app.MapGroup("/api/contracts/{contractId:guid}").RequireAuthorization();
        contracts.MapPost("/onboarding/complete-batch", async (Guid contractId, CompleteBatchRequest request,
            ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (request.Items is not { Count: >= 1 and <= 20 } || request.Items.Any(x => x is null)
                || request.Items.Select(x => x.ItemId).Distinct().Count() != request.Items.Count)
                return ApiInput.Error("items", "Supply 1–20 distinct checklist items.");
            if (request.Items.Any(x => x.ExpectedVersion < 0 || x.Notes?.Trim().Length > 1000))
                return ApiInput.Error("items", "Each item requires a nonnegative version and notes of at most 1,000 characters.");
            var ids = request.Items.Select(x => x.ItemId).ToList();
            var items = await db.OnboardingItems.Where(x => x.ContractId == contractId && ids.Contains(x.Id)).ToListAsync();
            if (items.Count != ids.Count) return Results.NotFound();
            if (items.Any(x => x.Version != request.Items.Single(r => r.ItemId == x.Id).ExpectedVersion || x.IsCompleted))
                return Results.Problem(statusCode: 409, detail: "A checklist item changed or is already complete. Reload the checklist.");
            var now = clock.GetUtcNow();
            foreach (var item in items) item.Complete(now, request.Items.Single(x => x.ItemId == item.Id).Notes);
            await db.SaveChangesAsync();
            var checklist = await db.OnboardingItems.Where(x => x.ContractId == contractId).OrderBy(x => x.Type).ToListAsync();
            return Results.Ok(new OnboardingChecklistResponse(contractId, checklist.All(x => !x.IsRequired || x.IsCompleted),
                checklist.Select(OnboardingEndpoints.ToResponse).ToList()));
        });
        contracts.MapGet("/notes", async (Guid contractId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok(await db.ContractNotes.AsNoTracking().Where(x => x.ContractId == contractId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToPageAsync(http, paging));
        });
        contracts.MapGet("/notes/{noteId:guid}", async (Guid contractId, Guid noteId, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            var note = await db.ContractNotes.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == contractId && x.Id == noteId);
            return note is null ? Results.NotFound() : Results.Ok(note);
        });
        contracts.MapPost("/notes", async (Guid contractId, CreateContractNoteRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 2000)
                return ApiInput.Error("body", "A note must contain 1–2,000 characters.");
            var note = new ContractNote { ContractId = contractId, Body = request.Body.Trim(),
                AuthorApiUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), CreatedAtUtc = clock.GetUtcNow() };
            db.ContractNotes.Add(note);
            await db.SaveChangesAsync();
            return Results.Created($"/api/contracts/{contractId}/notes/{note.Id}", note);
        });
        var countries = app.MapGroup("/api/countries/{countryCode}").RequireAuthorization();
        countries.MapGet("/onboarding-template", async (string countryCode, AtlasDbContext db) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            var template = await db.CountryOnboardingTemplates.Include(x => x.Items).AsNoTracking().SingleOrDefaultAsync(x => x.CountryCode == code);
            return Results.Ok(new { countryCode = code, version = template?.Version ?? 0, configured = template is not null,
                items = template is null ? OnboardingItem.CreateDefaultChecklist(Guid.Empty).Select(x => new TemplateItemResponse(x.Type.ToString(), x.Title, x.IsRequired))
                    : template.Items.OrderBy(x => x.Type).Select(x => new TemplateItemResponse(x.Type.ToString(), x.Title, x.IsRequired)) });
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        countries.MapPut("/onboarding-template", async (string countryCode, PutTemplateRequest request, AtlasDbContext db) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            if (request.ExpectedVersion < 0 || request.Items is not { Count: >= 1 and <= 5 }
                || request.Items.Any(x => x is null) || !request.Items.Any(x => x.IsRequired) || request.Items.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Title.Trim().Length > 200
                    || !ApiInput.DefinedEnum<OnboardingItemType>(x.Type, out _)))
                return ApiInput.Error("items", "Supply 1–5 defined item types, titles of 1–200 characters and at least one required item.");
            var parsed = request.Items.Select(x => { ApiInput.DefinedEnum<OnboardingItemType>(x.Type, out var type); return new OnboardingTemplateItem
                { CountryCode = code, Type = type, Title = x.Title!.Trim(), IsRequired = x.IsRequired }; }).ToList();
            if (parsed.Select(x => x.Type).Distinct().Count() != parsed.Count) return ApiInput.Error("items", "Item types must be unique.");
            var template = await db.CountryOnboardingTemplates.Include(x => x.Items).SingleOrDefaultAsync(x => x.CountryCode == code);
            if ((template?.Version ?? 0) != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The template changed. Reload it.");
            if (template is null) { template = new CountryOnboardingTemplate { CountryCode = code }; db.CountryOnboardingTemplates.Add(template); }
            else { db.RemoveRange(template.Items); db.Entry(template).Property(x => x.Version).IsModified = true; }
            template.Items = parsed;
            // These rows have fresh client-generated GUIDs; explicitly mark inserts.
            // Navigation discovery on an existing header can otherwise infer updates.
            db.AddRange(parsed);
            await db.SaveChangesAsync();
            return Results.Ok(new { countryCode = code, template.Version, configured = true,
                items = template.Items.OrderBy(x => x.Type).Select(x => new TemplateItemResponse(x.Type.ToString(), x.Title, x.IsRequired)) });
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        countries.MapPut("/hiring-status", async (string countryCode, CountryHiringRequest request, AtlasDbContext db) =>
        {
            var country = await db.Countries.FindAsync(countryCode.Trim().ToUpperInvariant());
            if (country is null) return Results.NotFound();
            if (request.ExpectedVersion != country.Version) return Results.Problem(statusCode: 409, detail: "The country changed. Reload it.");
            country.IsActive = request.IsActive;
            await db.SaveChangesAsync();
            return Results.Ok(new { country.Code, country.IsActive, country.Version });
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        return app;
    }
}

public record CompleteBatchRequest(List<CompleteBatchItem>? Items);
public record CompleteBatchItem(Guid ItemId, int ExpectedVersion, string? Notes);
public record CreateContractNoteRequest(string? Body);
public record TemplateItemResponse(string Type, string Title, bool IsRequired);
public record TemplateItemRequest(string? Type, string? Title, bool IsRequired);
public record PutTemplateRequest(int ExpectedVersion, List<TemplateItemRequest>? Items);
public record CountryHiringRequest(bool IsActive, int ExpectedVersion);
