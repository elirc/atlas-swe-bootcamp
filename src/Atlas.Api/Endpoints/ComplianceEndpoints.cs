using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Api.Reporting;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ComplianceEndpoints
{
    public static IEndpointRouteBuilder MapComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var workerDocs = app.MapGroup("/api/workers/{workerId:guid}/documents").RequireAuthorization();

        workerDocs.MapGet("/", async (Guid workerId, string? type, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await db.Workers.AnyAsync(w => w.Id == workerId))
            {
                return Results.NotFound();
            }
            if (!user.IsPlatformAdmin())
            {
                var ownClientId = user.ClientIdOrNull();
                var isOwnWorker = await db.Contracts.AnyAsync(c => c.WorkerId == workerId && c.ClientId == ownClientId);
                if (!isOwnWorker)
                {
                    return Results.NotFound();
                }
            }

            var query = db.ComplianceDocuments.Where(d => d.WorkerId == workerId);
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!ApiInput.DefinedEnum<ComplianceDocumentType>(type, out var parsed))
                    return ApiInput.Error("type", "Type must be a defined document type.");
                query = query.Where(d => d.Type == parsed);
            }
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var documents = await query
                .OrderBy(d => d.ExpiryDate)
                .ThenBy(d => d.Id)
                .ToListAsync();

            return Results.Ok(documents.Select(d => ToResponse(d, today)).ToList());
        });

        workerDocs.MapGet("/{documentId:guid}", async (
            Guid workerId, Guid documentId, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!user.IsPlatformAdmin()
                && !await db.Contracts.AnyAsync(c => c.WorkerId == workerId && c.ClientId == user.ClientIdOrNull()))
                return Results.NotFound();
            var document = await db.ComplianceDocuments.AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == documentId && d.WorkerId == workerId);
            return document is null ? Results.NotFound()
                : Results.Ok(ToResponse(document, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
        });

        workerDocs.MapPost("/", async (Guid workerId, CreateComplianceDocumentRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await db.Workers.AnyAsync(w => w.Id == workerId))
            {
                return Results.NotFound();
            }

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                errors["name"] = ["Name is required."];
            }
            else if (request.Name.Trim().Length > 200)
            {
                errors["name"] = ["Name must be at most 200 characters."];
            }
            if (string.IsNullOrWhiteSpace(request.Type)
                || !ApiInput.DefinedEnum<ComplianceDocumentType>(request.Type, out _))
            {
                errors["type"] = [$"Type must be one of: {string.Join(", ", Enum.GetNames<ComplianceDocumentType>())}."];
            }
            if (request.IssuedDate is not null && request.ExpiryDate is not null
                && request.ExpiryDate < request.IssuedDate)
            {
                errors["expiryDate"] = ["ExpiryDate cannot be before IssuedDate."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var document = new ComplianceDocument
            {
                WorkerId = workerId,
                Type = Enum.Parse<ComplianceDocumentType>(request.Type!, ignoreCase: true),
                Name = request.Name!.Trim(),
                IssuedDate = request.IssuedDate,
                ExpiryDate = request.ExpiryDate,
                CreatedAtUtc = clock.GetUtcNow(),
            };
            db.ComplianceDocuments.Add(document);
            await db.SaveChangesAsync();

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Created($"/api/workers/{workerId}/documents/{document.Id}", ToResponse(document, today));
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);

        app.MapGet("/api/compliance/expiring", async (int? withinDays, DateOnly? asOf,
            TimeProvider clock, AtlasDbContext db, CancellationToken cancellationToken) =>
        {
            var window = withinDays ?? ComplianceDocument.DefaultExpiringSoonWindowDays;
            var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (!ReportQueryValidation.TryCutoff(today, window, out var cutoff))
                return ReportQueryValidation.InvalidWindow();

            var expiring = await db.CurrentDocumentsAt(today)
                .Include(d => d.Worker)
                .Where(d => d.ExpiryDate != null && d.ExpiryDate <= cutoff)
                .OrderBy(d => d.ExpiryDate)
                .ThenBy(d => d.Id)
                .ToListAsync(cancellationToken);

            return Results.Ok(expiring
                .Select(d => new ExpiringDocumentResponse(
                    d.Id,
                    d.WorkerId,
                    d.Worker!.FullName,
                    d.Type.ToString(),
                    d.Name,
                    d.ExpiryDate!.Value,
                    d.GetStatus(today, window).ToString()))
                .ToList());
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);

        return app;
    }

    internal static ComplianceDocumentResponse ToResponse(ComplianceDocument d, DateOnly asOf) =>
        new(d.Id, d.WorkerId, d.Type.ToString(), d.Name, d.IssuedDate, d.ExpiryDate, d.GetStatus(asOf).ToString(), d.CreatedAtUtc, d.SupersedesDocumentId);
}

public record CreateComplianceDocumentRequest(
    string? Type,
    string? Name,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate);

public record ComplianceDocumentResponse(
    Guid Id,
    Guid WorkerId,
    string Type,
    string Name,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesDocumentId = null);

public record ExpiringDocumentResponse(
    Guid Id,
    Guid WorkerId,
    string WorkerName,
    string Type,
    string Name,
    DateOnly ExpiryDate,
    string Status);
