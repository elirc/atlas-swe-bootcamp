using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Api.Reporting;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ReportPresetEndpoints
{
    public static IEndpointRouteBuilder MapReportPresetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/report-presets").RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapGet("/", async (int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            var owner = InvoiceLedgerEndpoints.Actor(user);
            var rows = await db.ReportPresets.AsNoTracking().Where(x => x.OwnerApiUserId == owner).OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).ToPageAsync(http, paging);
            return Results.Ok(rows.Select(Response));
        });
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var row = await Owned(id, user, db); return row is null ? Results.NotFound() : Results.Ok(Response(row));
        });
        group.MapPost("/", async (CreatePresetRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (NameError(request.Name) is { } error) return error;
            if (ReportPresetRunner.Validate(request.Kind, request.Filters, clock, out var kind, out _, out var json) is { } filterError) return filterError;
            var owner = InvoiceLedgerEndpoints.Actor(user); var name = request.Name!.Trim(); var normalized = name.ToUpperInvariant();
            if (await db.ReportPresets.AnyAsync(x => x.OwnerApiUserId == owner && x.NormalizedName == normalized)) return DuplicateName();
            var preset = new ReportPreset { OwnerApiUserId = owner, Name = name, NormalizedName = normalized, Kind = kind, FiltersJson = json, Version = 1 };
            db.ReportPresets.Add(preset); await db.SaveChangesAsync();
            return Results.Created($"/api/report-presets/{preset.Id}", Response(preset));
        });
        group.MapPut("/{id:guid}", async (Guid id, ReplacePresetRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            var preset = await Owned(id, user, db);
            if (preset is null) return Results.NotFound();
            if (NameError(request.Name) is { } error) return error;
            if (request.ExpectedVersion < 1) return ApiInput.Error("expectedVersion", "Use a positive version.");
            if (preset.Version != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The preset changed. Reload it.");
            if (ReportPresetRunner.Validate(request.Kind, request.Filters, clock, out var kind, out _, out var json) is { } filterError) return filterError;
            var name = request.Name!.Trim(); var normalized = name.ToUpperInvariant();
            if (await db.ReportPresets.AnyAsync(x => x.OwnerApiUserId == preset.OwnerApiUserId && x.Id != id && x.NormalizedName == normalized)) return DuplicateName();
            preset.Name = name; preset.NormalizedName = normalized; preset.Kind = kind; preset.FiltersJson = json;
            db.Entry(preset).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync(); return Results.Ok(Response(preset));
        });
        group.MapDelete("/{id:guid}", async (Guid id, int expectedVersion, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var preset = await Owned(id, user, db);
            if (preset is null) return Results.NotFound();
            if (expectedVersion < 1) return ApiInput.Error("expectedVersion", "Use a positive version.");
            if (preset.Version != expectedVersion) return Results.Problem(statusCode: 409, detail: "The preset changed. Reload it.");
            db.ReportPresets.Remove(preset); await db.SaveChangesAsync(); return Results.NoContent();
        });
        group.MapPost("/{id:guid}/run", async (Guid id, ClaimsPrincipal user, AtlasDbContext db, ReportService reports, TimeProvider clock, CancellationToken ct) =>
        {
            var preset = await Owned(id, user, db);
            if (preset is null) return Results.NotFound();
            JsonDocument document;
            try { document = JsonDocument.Parse(preset.FiltersJson); }
            catch (JsonException) { return ApiInput.Error("filters", "Stored filters are invalid. Replace the preset before running it."); }
            using (document)
            {
                if (!Enum.IsDefined(preset.Kind)) return ApiInput.Error("kind", "Stored report kind is invalid.");
                if (ReportPresetRunner.Validate(ReportPresetRunner.KindName(preset.Kind), document.RootElement, clock, out _, out var filters, out _) is { } error) return error;
                return Results.Ok(await ReportPresetRunner.ExecuteAsync(filters!, reports, clock, ct));
            }
        });
        return app;
    }
    private static Task<ReportPreset?> Owned(Guid id, ClaimsPrincipal user, AtlasDbContext db)
    { var owner = InvoiceLedgerEndpoints.Actor(user); return db.ReportPresets.SingleOrDefaultAsync(x => x.Id == id && x.OwnerApiUserId == owner); }
    private static IResult? NameError(string? name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 ? ApiInput.Error("name", "Name must contain 1–100 characters.") : null;
    private static IResult DuplicateName() => Results.Problem(statusCode: 409, detail: "You already have a preset with this name.");
    private static object Response(ReportPreset p) => new { p.Id, p.Name, kind = ReportPresetRunner.KindName(p.Kind), filters = JsonSerializer.Deserialize<JsonElement>(p.FiltersJson), p.Version };
}
public record CreatePresetRequest(string? Name, string? Kind, JsonElement Filters);
public record ReplacePresetRequest(string? Name, string? Kind, JsonElement Filters, int ExpectedVersion);
