using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ComplianceWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapComplianceWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/api/workers/{workerId:guid}/documents/{documentId:guid}").RequireAuthorization();
        documents.MapPost("/renew", async (Guid workerId, Guid documentId, RenewDocumentRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            var predecessor = await db.ComplianceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.WorkerId == workerId);
            if (predecessor is null) return Results.NotFound();
            var now = clock.GetUtcNow(); var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return ApiInput.Error("name", "Name must contain 1–200 characters.");
            if (request.IssuedDate.Year is < 2000 or > 2100 || request.IssuedDate > today) return ApiInput.Error("issuedDate", "Use an issued date on or before today, with year 2000–2100.");
            if (request.ExpiryDate is { } expiry && (expiry.Year is < 2000 or > 2100 || expiry < request.IssuedDate || expiry < today))
                return ApiInput.Error("expiryDate", "Expiry must be on or after issuance and today, with year 2000–2100.");
            if (await db.ComplianceDocuments.AnyAsync(x => x.SupersedesDocumentId == documentId)) return Results.Problem(statusCode: 409, detail: "This document already has a successor. Renew the current leaf.");
            var next = new ComplianceDocument { WorkerId = workerId, Type = predecessor.Type, Name = request.Name.Trim(),
                IssuedDate = request.IssuedDate, ExpiryDate = request.ExpiryDate, CreatedAtUtc = now, SupersedesDocumentId = documentId };
            db.ComplianceDocuments.Add(next); await db.SaveChangesAsync();
            return Results.Created($"/api/workers/{workerId}/documents/{next.Id}", ComplianceEndpoints.ToResponse(next, today));
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        documents.MapGet("/renewal-history", async (Guid workerId, Guid documentId, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!user.IsPlatformAdmin() && !await db.Contracts.AnyAsync(x => x.WorkerId == workerId && x.ClientId == user.ClientIdOrNull())) return Results.NotFound();
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var target = await db.ComplianceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.WorkerId == workerId);
            if (target is null) return Results.NotFound();
            var visited = new HashSet<Guid>(); var root = target;
            while (root.SupersedesDocumentId is { } previous)
            {
                if (!visited.Add(root.Id) || visited.Count >= 100) throw new DomainException("The renewal chain contains a cycle or exceeds 100 records.");
                root = await db.ComplianceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == previous && x.WorkerId == workerId && x.Type == target.Type)
                    ?? throw new DomainException("The renewal chain contains an invalid predecessor.");
            }
            visited.Clear(); var chain = new List<ComplianceDocument>(); ComplianceDocument? current = root;
            while (current is not null)
            {
                if (!visited.Add(current.Id) || visited.Count > 100) throw new DomainException("The renewal chain contains a cycle or exceeds 100 records.");
                if (current.WorkerId != workerId || current.Type != target.Type) throw new DomainException("The renewal chain changes worker or document type.");
                chain.Add(current); var currentId = current.Id;
                current = await db.ComplianceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.SupersedesDocumentId == currentId);
            }
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Ok(new { workerId, currentId = chain[^1].Id, documents = chain.Select(x => ComplianceEndpoints.ToResponse(x, today)) });
        });
        var policies = app.MapGroup("/api/countries/{countryCode}/document-policy").RequireAuthorization();
        policies.MapGet("/", async (string countryCode, AtlasDbContext db) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            var policy = await db.CountryDocumentPolicies.AsNoTracking().Include(x => x.Requirements).SingleOrDefaultAsync(x => x.CountryCode == code);
            return Results.Ok(PolicyResponse(code, policy));
        });
        policies.MapPut("/", async (string countryCode, PutDocumentPolicyRequest request, AtlasDbContext db) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            if (request.ExpectedVersion < 0 || request.Requirements is not { Count: >= 1 and <= 5 } || request.Requirements.Any(x => x is null
                || !ApiInput.DefinedEnum<ComplianceDocumentType>(x.Type, out _) || x.MinimumRemainingDays is < 0 or > 365))
                return ApiInput.Error("requirements", "Supply 1–5 defined document types with minimum remaining days between 0 and 365, and a nonnegative version.");
            var requirements = request.Requirements.Select(x => { ApiInput.DefinedEnum<ComplianceDocumentType>(x.Type, out var type); return new CountryDocumentRequirement
                { CountryCode = code, Type = type, MinimumRemainingDays = x.MinimumRemainingDays, ExpiryRequired = x.ExpiryRequired }; }).ToList();
            if (requirements.Select(x => x.Type).Distinct().Count() != requirements.Count) return ApiInput.Error("requirements", "Document types must be distinct.");
            var policy = await db.CountryDocumentPolicies.Include(x => x.Requirements).SingleOrDefaultAsync(x => x.CountryCode == code);
            if ((policy?.Version ?? 0) != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The policy changed. Reload it.");
            if (policy is null) { policy = new CountryDocumentPolicy { CountryCode = code }; db.CountryDocumentPolicies.Add(policy); }
            else { db.RemoveRange(policy.Requirements); db.Entry(policy).Property(x => x.Version).IsModified = true; }
            policy.Requirements = requirements; db.AddRange(requirements); await db.SaveChangesAsync();
            return Results.Ok(PolicyResponse(code, policy));
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        app.MapGet("/api/contracts/{id:guid}/document-coverage", async (Guid id, DateOnly? asOf, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (date.Year is < 2000 or > 2100) return ApiInput.Error("asOf", "Year must be between 2000 and 2100.");
            var policy = await db.CountryDocumentPolicies.Include(x => x.Requirements).AsNoTracking().SingleOrDefaultAsync(x => x.CountryCode == contract.CountryCode);
            var candidates = await db.CurrentDocumentsAt(date).Where(x => x.WorkerId == contract.WorkerId).ToListAsync();
            var rows = policy?.Requirements.OrderBy(x => x.Type).Select(x => DocumentCoverage.Assess(x, candidates, date)).ToList() ?? [];
            return Results.Ok(new { contractId = id, contract.CountryCode, asOf = date, policyVersion = policy?.Version ?? 0,
                assessment = policy is null ? "not-configured" : rows.All(x => x.Covered) ? "covered" : "not-covered",
                requirements = rows, scope = "Metadata coverage under the current policy; not legal verification." });
        }).RequireAuthorization();
        return app;
    }
    private static object PolicyResponse(string code, CountryDocumentPolicy? p) => new { countryCode = code, configured = p is not null, version = p?.Version ?? 0,
        requirements = (p?.Requirements ?? []).OrderBy(x => x.Type).Select(x => new { type = x.Type.ToString(), x.MinimumRemainingDays, x.ExpiryRequired }) };
}
public record RenewDocumentRequest(string? Name, DateOnly IssuedDate, DateOnly? ExpiryDate);
public record PutDocumentPolicyRequest(int ExpectedVersion, List<DocumentRequirementRequest>? Requirements);
public record DocumentRequirementRequest(string? Type, int MinimumRemainingDays, bool ExpiryRequired);
