using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class WorkerImportEndpoints
{
    public static IEndpointRouteBuilder MapWorkerImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/worker-imports").RequireAuthorization(AuthPolicies.PlatformAdmin);
        group.MapPost("/validate", async (HttpRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            // Bound the transport before JSON decoding; escaped JSON may be larger than decoded CSV.
            const int limit = 2 * 1024 * 1024;
            if (request.ContentLength > limit) return Results.Problem(statusCode: 413, detail: "Request exceeds 2 MiB.");
            using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await request.Body.ReadAsync(buffer, ct)) != 0)
            { if (body.Length + count > limit) return Results.Problem(statusCode: 413, detail: "Request exceeds 2 MiB."); await body.WriteAsync(buffer.AsMemory(0, count), ct); }
            CsvImportRequest? input;
            try { input = JsonSerializer.Deserialize<CsvImportRequest>(body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return ApiInput.Error("csv", "Supply a JSON object with a csv string."); }
            if (input?.Csv is null) return ApiInput.Error("csv", "CSV is required.");
            List<string[]> rows;
            try { rows = WorkerCsv.Parse(input.Csv); } catch (DomainException ex) { return ApiInput.Error("csv", ex.Message); }
            var now = clock.GetUtcNow(); var today = DateOnly.FromDateTime(now.UtcDateTime);
            var batch = new WorkerImportBatch { OwnerApiUserId = InvoiceLedgerEndpoints.Actor(user), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24), Version = 1 };
            var countries = (await db.Countries.Select(x => x.Code).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal); var number = 1;
            foreach (var row in rows)
            {
                number++; DateOnly? birth = null; var badDate = false;
                if (row[3].Length > 0) { if (DateOnly.TryParseExact(row[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) birth = parsed; else badDate = true; }
                var (value, errors) = WorkerInput.Normalize(row[0], row[1], row[2], birth, today);
                if (badDate) errors["dateOfBirth"] = ["Use YYYY-MM-DD or an empty value."];
                if (!countries.Contains(value.CountryCode)) errors["countryCode"] = ["Country is not configured."];
                if (!seen.Add(value.Email)) errors["email"] = ["Email is duplicated in this CSV."];
                batch.Rows.Add(new WorkerImportRow { BatchId = batch.Id, SourceRowNumber = number, FullName = value.FullName,
                    Email = value.Email, CountryCode = value.CountryCode, DateOfBirth = birth, ErrorsJson = JsonSerializer.Serialize(errors) });
            }
            var emails = batch.Rows.Select(x => x.Email).ToList();
            var existing = (await db.Workers.Where(x => emails.Contains(x.Email)).Select(x => x.Email).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            foreach (var row in batch.Rows.Where(x => existing.Contains(x.Email)))
            { var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(row.ErrorsJson)!; errors["email"] = ["Email already exists."]; row.ErrorsJson = JsonSerializer.Serialize(errors); }
            batch.Status = batch.Rows.Any(x => x.ErrorsJson != "{}") ? WorkerImportStatus.Invalid : WorkerImportStatus.Validated;
            db.WorkerImportBatches.Add(batch); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/worker-imports/{batch.Id}", Response(batch));
        });
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var owner = InvoiceLedgerEndpoints.Actor(user);
            var batch = await db.WorkerImportBatches.AsNoTracking().Include(x => x.Rows).SingleOrDefaultAsync(x => x.Id == id && x.OwnerApiUserId == owner);
            return batch is null ? Results.NotFound() : Results.Ok(Response(batch));
        });
        group.MapPost("/{id:guid}/commit", async (Guid id, CommitImportRequest request, ClaimsPrincipal user, AtlasDbContext db, DbContextOptions<AtlasDbContext> options, TimeProvider clock) =>
        {
            var owner = InvoiceLedgerEndpoints.Actor(user);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var batch = await db.WorkerImportBatches.Include(x => x.Rows).SingleOrDefaultAsync(x => x.Id == id && x.OwnerApiUserId == owner);
            if (batch is null) return Results.NotFound();
            if (batch.Status == WorkerImportStatus.Committed) return Results.Ok(Response(batch));
            if (clock.GetUtcNow() >= batch.ExpiresAtUtc) return Results.Problem(statusCode: 410, detail: "Import validation expired. Validate a new batch.");
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            if (batch.Status != WorkerImportStatus.Validated || batch.Version != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "Import is invalid or changed. Reload or validate a new batch.");
            var emails = batch.Rows.Select(x => x.Email).ToList(); var codes = batch.Rows.Select(x => x.CountryCode).Distinct().ToList();
            if (await db.Workers.AnyAsync(x => emails.Contains(x.Email)) || await db.Countries.CountAsync(x => codes.Contains(x.Code)) != codes.Count)
                return Results.Problem(statusCode: 409, detail: "Workers or countries changed since validation. Validate a new batch.");
            foreach (var row in batch.Rows)
            { var worker = new Worker { FullName = row.FullName, Email = row.Email, CountryCode = row.CountryCode, DateOfBirth = row.DateOfBirth, CreatedAtUtc = clock.GetUtcNow() }; db.Workers.Add(worker); row.CreatedWorkerId = worker.Id; }
            batch.Status = WorkerImportStatus.Committed;
            try { await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(Response(batch)); }
            catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
            {
                await transaction.RollbackAsync(); await using var fresh = new AtlasDbContext(options);
                var winner = await fresh.WorkerImportBatches.AsNoTracking().Include(x => x.Rows).SingleOrDefaultAsync(x => x.Id == id && x.OwnerApiUserId == owner);
                return winner?.Status == WorkerImportStatus.Committed ? Results.Ok(Response(winner)) : Results.Problem(statusCode: 409, detail: "Import competed with another write. Reload before retrying.");
            }
        });
        return app;
    }
    private static object Response(WorkerImportBatch b) => new { b.Id, b.Version, status = b.Status.ToString(), b.CreatedAtUtc, b.ExpiresAtUtc,
        rows = b.Rows.OrderBy(x => x.SourceRowNumber).Select(x => new { x.SourceRowNumber, x.FullName, x.Email, x.CountryCode, x.DateOfBirth, x.CreatedWorkerId,
            errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(x.ErrorsJson) }) };
}
public record CsvImportRequest(string? Csv);
public record CommitImportRequest(int ExpectedVersion);
