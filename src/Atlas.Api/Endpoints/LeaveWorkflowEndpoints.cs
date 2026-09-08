using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class LeaveWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapLeaveWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var contracts = app.MapGroup("/api/contracts/{contractId:guid}").RequireAuthorization();
        contracts.MapPost("/leave-quote", async (Guid contractId, LeaveQuoteRequest request, ClaimsPrincipal user, AtlasDbContext db, LeaveService leave) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (!ApiInput.DefinedEnum<LeaveType>(request.Type, out var type)) return ApiInput.Error("type", "Choose a defined leave type.");
            if (ApiInput.DateRange(request.StartDate, request.EndDate, 36890) is { } error) return error;
            return Results.Ok(await leave.AssessAsync(contract, type, request.StartDate, request.EndDate));
        });
        contracts.MapGet("/leave-adjustments", async (Guid contractId, int year, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (year is < 2000 or > 2100) return ApiInput.Error("year", "Year must be between 2000 and 2100.");
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok(await db.LeaveAllowanceAdjustments.AsNoTracking().Where(x => x.ContractId == contractId && x.Year == year)
                .OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.Id).ToPageAsync(http, paging));
        });
        contracts.MapPost("/leave-adjustments", async (Guid contractId, LeaveAdjustmentRequest request, ClaimsPrincipal user,
            AtlasDbContext db, LeaveService leave, TimeProvider clock, DbContextOptions<AtlasDbContext> options) =>
        {
            var contract = await db.Contracts.FindAsync(contractId);
            if (contract is null) return Results.NotFound();
            if (request.Year is < 2000 or > 2100 || request.DeltaDays is < -366 or > 366 or 0)
                return ApiInput.Error("deltaDays", "Use year 2000–2100 and a nonzero whole-day adjustment between -366 and 366.");
            if (!ApiInput.DefinedEnum<LeaveType>(request.Type, out var type)) return ApiInput.Error("type", "Choose a defined leave type.");
            if (InvoiceLedgerEndpoints.TextInput(request.Reference, request.Reason) is { } error) return error;
            var reference = request.Reference!.Trim(); var reason = request.Reason!.Trim();
            var old = await db.LeaveAllowanceAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == contractId
                && x.Year == request.Year && x.Type == type && x.Reference == reference);
            if (old is not null) return old.DeltaDays == request.DeltaDays && old.Reason == reason ? Results.Ok(old)
                : Results.Problem(statusCode: 409, detail: "This reference already identifies a different adjustment.");
            if (contract.Status != ContractStatus.Active || contract.EndDate is not null)
                return Results.Problem(statusCode: 409, detail: "Adjustments require an active contract without an end date.");
            var balance = (await leave.GetBalancesAsync(contract, request.Year)).Single(x => x.Type == type);
            if (balance.AllowanceDays + request.DeltaDays < 0 || balance.RemainingDays + request.DeltaDays < 0)
                return Results.Problem(statusCode: 409, detail: "The adjustment would reduce allowance below zero or reserved days.");
            var adjustment = new LeaveAllowanceAdjustment { ContractId = contractId, Year = request.Year, Type = type,
                DeltaDays = request.DeltaDays, Reference = reference, Reason = reason,
                ActorApiUserId = InvoiceLedgerEndpoints.Actor(user), RecordedAtUtc = clock.GetUtcNow() };
            db.LeaveAllowanceAdjustments.Add(adjustment); db.Entry(contract).Property(x => x.Version).IsModified = true;
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) when (DatabaseConflicts.IsWriteConflict(ex))
            {
                await using var fresh = new AtlasDbContext(options);
                var replay = await fresh.LeaveAllowanceAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == contractId
                    && x.Year == request.Year && x.Type == type && x.Reference == reference);
                if (replay is not null && replay.DeltaDays == request.DeltaDays && replay.Reason == reason) return Results.Ok(replay);
                return Results.Problem(statusCode: 409, detail: "The contract or leave balance changed. Reload before retrying.");
            }
            return Results.Created($"/api/contracts/{contractId}/leave-adjustments?year={request.Year}", adjustment);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        app.MapPost("/api/leave-requests/{id:guid}/cancel-approved", async (Guid id, ExpectedVersionRequest request, ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            var leave = await db.LeaveRequests.Include(x => x.Contract).SingleOrDefaultAsync(x => x.Id == id);
            if (leave is null || !user.CanViewClient(leave.Contract!.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(leave.Contract!.ClientId)) return Results.Forbid();
            if (request.ExpectedVersion != leave.Version) return Results.Problem(statusCode: 409, detail: "The leave request changed. Reload it.");
            var now = clock.GetUtcNow();
            leave.CancelApproved(leave.Contract!, DateOnly.FromDateTime(now.UtcDateTime), now);
            db.Entry(leave.Contract!).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync();
            return Results.Ok(LeaveEndpoints.ToResponse(leave));
        }).RequireAuthorization();
        var countries = app.MapGroup("/api/countries/{countryCode}/holidays").RequireAuthorization();
        countries.MapGet("/", async (string countryCode, int year, AtlasDbContext db) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            if (year is < 2000 or > 2100) return ApiInput.Error("year", "Year must be between 2000 and 2100.");
            return Results.Ok(await db.CountryHolidays.AsNoTracking().Where(x => x.CountryCode == code && x.Date.Year == year).OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync());
        });
        countries.MapPost("/", async (string countryCode, CreateHolidayRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.Code == code)) return Results.NotFound();
            if (request.Date.Year is < 2000 or > 2100 || request.Date <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return ApiInput.Error("date", "Choose a future date with year 2000–2100.");
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return ApiInput.Error("name", "Name must contain 1–200 characters.");
            if (await db.CountryHolidays.AnyAsync(x => x.CountryCode == code && x.Date == request.Date))
                return Results.Problem(statusCode: 409, detail: "This date is already configured as a holiday.");
            var holiday = new CountryHoliday { CountryCode = code, Date = request.Date, Name = request.Name.Trim(), CreatedAtUtc = clock.GetUtcNow() };
            db.CountryHolidays.Add(holiday); await db.SaveChangesAsync();
            return Results.Created($"/api/countries/{code}/holidays?year={request.Date.Year}", holiday);
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        countries.MapDelete("/{id:guid}", async (string countryCode, Guid id, AtlasDbContext db, TimeProvider clock) =>
        {
            var code = countryCode.Trim().ToUpperInvariant();
            var holiday = await db.CountryHolidays.SingleOrDefaultAsync(x => x.Id == id && x.CountryCode == code);
            if (holiday is null) return Results.NotFound();
            if (holiday.Date <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Results.Problem(statusCode: 409, detail: "Only future holidays can be removed.");
            db.CountryHolidays.Remove(holiday); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.PlatformAdmin);
        return app;
    }
}
public record LeaveQuoteRequest(string? Type, DateOnly StartDate, DateOnly EndDate);
public record LeaveAdjustmentRequest(int Year, string? Type, int DeltaDays, string? Reference, string? Reason);
public record ExpectedVersionRequest(int ExpectedVersion);
public record CreateHolidayRequest(DateOnly Date, string? Name);
