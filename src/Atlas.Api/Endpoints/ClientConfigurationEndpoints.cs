using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ClientConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapClientConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var clients = app.MapGroup("/api/clients/{clientId:guid}").RequireAuthorization();
        clients.MapGet("/payroll-budgets/{year:int}/{month:int}/{currency}", async (Guid clientId, int year, int month, string currency, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (PeriodCurrency(year, month, currency) is { } error) return error;
            var code = currency.Trim().ToUpperInvariant();
            var budget = await db.ClientPayrollBudgets.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.Year == year && x.Month == month && x.CurrencyCode == code);
            return budget is null ? Results.NotFound() : Results.Ok(budget);
        });
        clients.MapPut("/payroll-budgets/{year:int}/{month:int}/{currency}", async (Guid clientId, int year, int month, string currency, PutBudgetRequest request,
            ClaimsPrincipal user, AtlasDbContext db, TimeProvider clock) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            if (PeriodCurrency(year, month, currency) is { } error) return error;
            var code = currency.Trim().ToUpperInvariant();
            if (!await db.Countries.AnyAsync(x => x.CurrencyCode == code)) return ApiInput.Error("currency", "Choose a configured country currency.");
            if (request.Amount is <= 0 or > 1_000_000_000m || decimal.Round(request.Amount, 2) != request.Amount)
                return ApiInput.Error("amount", "Amount must be positive, at most 1,000,000,000 and have at most two decimal places.");
            if (request.ExpectedVersion < 0) return ApiInput.Error("expectedVersion", "Version cannot be negative.");
            var budget = await db.ClientPayrollBudgets.SingleOrDefaultAsync(x => x.ClientId == clientId && x.Year == year && x.Month == month && x.CurrencyCode == code);
            if ((budget?.Version ?? 0) != request.ExpectedVersion) return Results.Problem(statusCode: 409, detail: "The budget changed. Reload it.");
            if (budget is null) { budget = new ClientPayrollBudget { ClientId = clientId, Year = year, Month = month, CurrencyCode = code }; db.ClientPayrollBudgets.Add(budget); }
            else db.Entry(budget).Property(x => x.Version).IsModified = true;
            budget.Amount = request.Amount; budget.ModifiedByApiUserId = InvoiceLedgerEndpoints.Actor(user); budget.ModifiedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(); return Results.Ok(budget);
        });
        clients.MapGet("/payroll-budget-report", async (Guid clientId, int year, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (year is < 2000 or > 2100) return ApiInput.Error("year", "Year must be between 2000 and 2100.");
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var budgets = await db.ClientPayrollBudgets.AsNoTracking().Where(x => x.ClientId == clientId && x.Year == year).ToListAsync();
            var costs = await db.Payslips.AsNoTracking().Where(x => x.ClientId == clientId && x.PayrollRun!.Year == year && x.PayrollRun.Status == PayrollRunStatus.Completed)
                .Select(x => new { x.PayrollRun!.Month, x.CurrencyCode, x.TotalCost }).ToListAsync();
            var planned = budgets.ToDictionary(x => (x.Month, x.CurrencyCode), x => x.Amount);
            var actuals = costs.GroupBy(x => (x.Month, x.CurrencyCode)).ToDictionary(g => g.Key, g => g.Sum(x => x.TotalCost));
            var rows = planned.Keys.Union(actuals.Keys).OrderBy(x => x.Month).ThenBy(x => x.CurrencyCode).Select(key =>
            {
                decimal? budget = planned.TryGetValue(key, out var value) ? value : null; var actual = actuals.GetValueOrDefault(key);
                var status = budget is null ? "no-budget" : actual > budget ? "over-budget" : actual == budget ? "at-budget"
                    : actual >= budget * .9m ? "near-budget" : "within-budget";
                return new { month = key.Month, currencyCode = key.CurrencyCode, budget, actual, variance = actual - budget,
                    utilizationPercent = budget is null ? (decimal?)null : decimal.Round(actual / budget.Value * 100m, 2, MidpointRounding.AwayFromZero), status };
            });
            return Results.Ok(new { clientId, year, basis = "completed-payslip-total-cost", rows });
        });
        clients.MapGet("/contract-field-definitions", async (Guid clientId, int? page, int? pageSize, ClaimsPrincipal user, HttpContext http, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (Pagination.Validate(page, pageSize, out var paging) is { } error) return error;
            return Results.Ok((await db.ClientContractFieldDefinitions.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Key).ThenBy(x => x.Id).ToPageAsync(http, paging)).Select(Definition));
        });
        clients.MapPost("/contract-field-definitions", async (Guid clientId, CreateFieldDefinitionRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            if (!await Visible(clientId, user, db)) return Results.NotFound();
            if (!user.CanManageClient(clientId)) return Results.Forbid();
            if (!Key(request.Key)) return ApiInput.Error("key", "Use a lowercase ASCII letter followed by at most 29 lowercase letters, digits or underscores.");
            if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 100) return ApiInput.Error("label", "Label must contain 1–100 characters.");
            if (request.Type is not ("text" or "number" or "choice")) return ApiInput.Error("type", "Choose text, number or choice.");
            if (request.Type == "choice" && (request.Options is not { Count: >= 1 and <= 20 } || request.Options.Any(x => !Key(x))
                || request.Options.Distinct(StringComparer.Ordinal).Count() != request.Options.Count)) return ApiInput.Error("options", "Supply 1–20 distinct option codes using the key grammar.");
            if (request.Type != "choice" && request.Options is { Count: > 0 }) return ApiInput.Error("options", "Only choice fields accept options.");
            if (await db.ClientContractFieldDefinitions.AnyAsync(x => x.ClientId == clientId && x.Key == request.Key)) return Results.Problem(statusCode: 409, detail: "A field with this key already exists.");
            var field = new ClientContractFieldDefinition { ClientId = clientId, Key = request.Key!, Label = request.Label.Trim(),
                Kind = Enum.Parse<ContractFieldKind>(request.Type, true), OptionsJson = JsonSerializer.Serialize(request.Options ?? []) };
            db.ClientContractFieldDefinitions.Add(field); await db.SaveChangesAsync();
            return Results.Created($"/api/clients/{clientId}/contract-field-definitions", Definition(field));
        });
        var fields = app.MapGroup("/api/contracts/{id:guid}/custom-fields").RequireAuthorization();
        fields.MapGet("/", async (Guid id, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync();
            var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            return Results.Ok(await Values(contract, db));
        });
        fields.MapPut("/", async (Guid id, ReplaceCustomFieldsRequest request, ClaimsPrincipal user, AtlasDbContext db) =>
        {
            var contract = await db.Contracts.FindAsync(id);
            if (contract is null || !user.CanViewClient(contract.ClientId)) return Results.NotFound();
            if (!user.CanManageClient(contract.ClientId)) return Results.Forbid();
            if (request.ExpectedContractVersion < 0) return ApiInput.Error("expectedContractVersion", "Version cannot be negative.");
            if (request.ExpectedContractVersion != contract.Version) return Results.Problem(statusCode: 409, detail: "The contract changed. Reload its fields.");
            if (request.Values is null || request.Values.Count > 20 || request.Values.Any(x => x is null || !Key(x.Key))
                || request.Values.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != request.Values.Count)
                return ApiInput.Error("values", "Supply at most 20 distinct field keys and values; an empty list removes all values.");
            var keys = request.Values.Select(x => x.Key).ToList();
            var definitions = await db.ClientContractFieldDefinitions.AsNoTracking().Where(x => x.ClientId == contract.ClientId && keys.Contains(x.Key)).ToDictionaryAsync(x => x.Key);
            if (definitions.Count != keys.Count) return ApiInput.Error("values", "Every key must be defined for this contract's client.");
            var replacements = new List<ContractFieldValue>();
            foreach (var supplied in request.Values)
            {
                var definition = definitions[supplied.Key!]; var value = supplied.Value;
                var row = new ContractFieldValue { ContractId = id, DefinitionId = definition.Id };
                switch (definition.Kind)
                {
                    case ContractFieldKind.Text when value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 500: row.TextValue = value.GetString(); break;
                    case ContractFieldKind.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                        && number is >= -1_000_000_000m and <= 1_000_000_000m && decimal.Round(number, 4) == number: row.NumberValue = number; break;
                    case ContractFieldKind.Choice when value.ValueKind == JsonValueKind.String
                        && JsonSerializer.Deserialize<List<string>>(definition.OptionsJson)!.Contains(value.GetString()!, StringComparer.Ordinal): row.ChoiceCode = value.GetString(); break;
                    default: return ApiInput.Error("values", $"Field '{definition.Key}' has an invalid type, length, precision or choice.");
                }
                replacements.Add(row);
            }
            db.ContractFieldValues.RemoveRange(await db.ContractFieldValues.Where(x => x.ContractId == id).ToListAsync());
            db.ContractFieldValues.AddRange(replacements); db.Entry(contract).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync(); return Results.Ok(await Values(contract, db));
        });
        return app;
    }
    private static bool Key(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-z][a-z0-9_]{0,29}\\z", RegexOptions.CultureInvariant);
    private static Task<bool> Visible(Guid id, ClaimsPrincipal user, AtlasDbContext db) => user.CanViewClient(id) ? db.Clients.AnyAsync(x => x.Id == id) : Task.FromResult(false);
    private static IResult? PeriodCurrency(int year, int month, string currency) => year is < 2000 or > 2100 || month is < 1 or > 12
        ? ApiInput.Error("year", "Use year 2000–2100 and month 1–12.") : !ApiInput.IsAsciiCode(currency.Trim(), 3) ? ApiInput.Error("currency", "Use three ASCII letters.") : null;
    private static object Definition(ClientContractFieldDefinition d) => new { d.Id, d.Key, d.Label, type = d.Kind.ToString().ToLowerInvariant(), options = JsonSerializer.Deserialize<List<string>>(d.OptionsJson) };
    private static async Task<object> Values(EmploymentContract contract, AtlasDbContext db)
    {
        var definitions = await db.ClientContractFieldDefinitions.AsNoTracking().Where(x => x.ClientId == contract.ClientId).ToDictionaryAsync(x => x.Id);
        var values = await db.ContractFieldValues.AsNoTracking().Where(x => x.ContractId == contract.Id).ToListAsync();
        return new { contractId = contract.Id, contractVersion = contract.Version, values = values.OrderBy(x => definitions[x.DefinitionId].Key).Select(x => new
        {
            key = definitions[x.DefinitionId].Key, label = definitions[x.DefinitionId].Label, type = definitions[x.DefinitionId].Kind.ToString().ToLowerInvariant(),
            value = definitions[x.DefinitionId].Kind switch { ContractFieldKind.Text => (object?)x.TextValue, ContractFieldKind.Number => x.NumberValue, _ => x.ChoiceCode },
        }) };
    }
}
public record PutBudgetRequest(decimal Amount, int ExpectedVersion);
public record CreateFieldDefinitionRequest(string? Key, string? Label, string? Type, List<string>? Options);
public record ReplaceCustomFieldsRequest(int ExpectedContractVersion, List<CustomFieldInput>? Values);
public record CustomFieldInput(string? Key, JsonElement Value);
