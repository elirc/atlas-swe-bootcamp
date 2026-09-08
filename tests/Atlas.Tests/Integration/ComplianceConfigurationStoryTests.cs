using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain.Entities;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class ComplianceConfigurationStoryTests
{
    [Fact]
    public async Task SS12_RenewalKeepsHistoryAndChangesExpirySelectionOnlyAfterRecording()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient(); var path = $"/api/workers/{Id(201)}/documents/{Id(501)}";
        var renewed = await Json(await admin.PostAsJsonAsync(path + "/renew", new { name = "Renewed passport", issuedDate = "2026-09-01", expiryDate = "2027-09-01" }), HttpStatusCode.Created);
        var newId = renewed.GetProperty("id").GetGuid(); Assert.NotEqual(Id(501), newId);
        Assert.Equal(Id(501), renewed.GetProperty("supersedesDocumentId").GetGuid()); Assert.Equal("Passport", renewed.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/renew", new { name = "Another renewal", issuedDate = "2026-09-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/workers/{Id(202)}/documents/{Id(501)}/renew", new { name = "Wrong parent", issuedDate = "2026-09-01" })).StatusCode);
        var history = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), path + "/renewal-history");
        Assert.Equal(newId, history.GetProperty("currentId").GetGuid()); Assert.Equal(2, history.GetProperty("documents").GetArrayLength());
        var before = await Get(admin, "/api/compliance/expiring?asOf=2026-08-31&withinDays=30");
        Assert.Contains(before.EnumerateArray(), x => x.GetProperty("id").GetGuid() == Id(501));
        var after = await Get(admin, "/api/compliance/expiring?asOf=2026-09-01&withinDays=30");
        Assert.DoesNotContain(after.EnumerateArray(), x => x.GetProperty("id").GetGuid() == Id(501));
        var report = await Get(admin, "/api/reports/compliance-expiries?asOf=2026-09-01&withinDays=30");
        Assert.DoesNotContain(report.EnumerateArray(), x => x.GetProperty("documentId").GetGuid() == Id(501));
        var original = await Get(admin, path); Assert.Equal("2026-09-15", original.GetProperty("expiryDate").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/workers/{Id(201)}/documents/{newId}/renew", new { name = "Future-issued", issuedDate = "2026-09-02" })).StatusCode);
    }

    [Fact]
    public async Task SS13_CoverageStartsFromRequirementsAndAcceptsOneSufficientCandidate()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient(); var path = $"/api/contracts/{Id(301)}/document-coverage?asOf=2026-09-01";
        Assert.Equal("not-configured", (await Get(admin, path)).GetProperty("assessment").GetString());
        var requirements = new[] { new { type = "Passport", minimumRemainingDays = 30, expiryRequired = true }, new { type = "WorkPermit", minimumRemainingDays = 0, expiryRequired = true } };
        await Json(await admin.PutAsJsonAsync("/api/countries/PH/document-policy", new { expectedVersion = 0, requirements }), HttpStatusCode.OK);
        var missing = await Get(admin, $"/api/contracts/{Id(303)}/document-coverage?asOf=2026-09-01");
        Assert.Equal(2, missing.GetProperty("requirements").GetArrayLength());
        Assert.All(missing.GetProperty("requirements").EnumerateArray(), x => Assert.Equal("missing-type", x.GetProperty("blockers")[0].GetProperty("code").GetString()));
        var insufficient = await Get(admin, path); Assert.Equal("not-covered", insufficient.GetProperty("assessment").GetString());
        Assert.Contains(insufficient.GetProperty("requirements").EnumerateArray().SelectMany(x => x.GetProperty("blockers").EnumerateArray()), x => x.GetProperty("code").GetString() == "insufficient-remaining-days");
        await Json(await admin.PostAsJsonAsync($"/api/workers/{Id(201)}/documents", new { type = "Passport", name = "Another valid root", issuedDate = "2026-08-01", expiryDate = "2026-10-01" }), HttpStatusCode.Created);
        Assert.Equal("covered", (await Get(admin, path)).GetProperty("assessment").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/countries/PH/document-policy", new { expectedVersion = 0, requirements })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClientWithApiKey("bootcamp-viewer").GetAsync($"/api/contracts/{Id(302)}/document-coverage")).StatusCode);
    }

    [Fact]
    public async Task SS14_BudgetThresholdUsesExactAmountsAndExcludesDraftPayroll()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var manager = f.CreateClientWithApiKey("bootcamp-admin"); var prefix = $"/api/clients/{Id(101)}";
        f.WithDb(db => { db.Payslips.Single(x => x.Id == Id(702)).TotalCost = 899.999m; db.SaveChanges(); });
        await Json(await manager.PutAsJsonAsync(prefix + "/payroll-budgets/2026/7/USD", new { amount = 1000m, expectedVersion = 0 }), HttpStatusCode.OK);
        await Json(await manager.PutAsJsonAsync(prefix + "/payroll-budgets/2026/9/USD", new { amount = 500m, expectedVersion = 0 }), HttpStatusCode.OK);
        var rows = (await Get(manager, prefix + "/payroll-budget-report?year=2026")).GetProperty("rows");
        var july = rows.EnumerateArray().Single(x => x.GetProperty("month").GetInt32() == 7);
        Assert.Equal(899.999m, july.GetProperty("actual").GetDecimal()); Assert.Equal(90m, july.GetProperty("utilizationPercent").GetDecimal());
        Assert.Equal("within-budget", july.GetProperty("status").GetString());
        Assert.DoesNotContain(rows.EnumerateArray(), x => x.GetProperty("month").GetInt32() == 6);
        Assert.Equal(0m, rows.EnumerateArray().Single(x => x.GetProperty("month").GetInt32() == 9).GetProperty("actual").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(prefix + "/payroll-budgets/2026/7/USD", new { amount = 500m, expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-viewer").PutAsJsonAsync(prefix + "/payroll-budgets/2026/7/USD", new { amount = 500m, expectedVersion = 1 })).StatusCode);
    }

    [Fact]
    public async Task SS15_CustomFieldsPreserveDecimalTypesAndRejectTheWholeInvalidReplacement()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var manager = f.CreateClientWithApiKey("bootcamp-admin"); var definitions = $"/api/clients/{Id(101)}/contract-field-definitions";
        await Json(await manager.PostAsJsonAsync(definitions, new { key = "team", label = "Team", type = "choice", options = new[] { "ops", "eng" } }), HttpStatusCode.Created);
        await Json(await manager.PostAsJsonAsync(definitions, new { key = "allowance", label = "Allowance", type = "number" }), HttpStatusCode.Created);
        var path = $"/api/contracts/{Id(301)}/custom-fields";
        var initial = await Get(manager, path); var version = initial.GetProperty("contractVersion").GetInt32();
        object[] values = [new { key = "team", value = (object)"eng" }, new { key = "allowance", value = (object)12.3456m }];
        var saved = await Json(await manager.PutAsJsonAsync(path, new { expectedContractVersion = version, values }), HttpStatusCode.OK);
        var amount = saved.GetProperty("values").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "allowance").GetProperty("value");
        Assert.Equal(JsonValueKind.Number, amount.ValueKind); Assert.Equal(12.3456m, amount.GetDecimal());
        var nextVersion = saved.GetProperty("contractVersion").GetInt32();
        object[] invalid = [new { key = "team", value = (object)"ops" }, new { key = "allowance", value = (object)12.34567m }];
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(path, new { expectedContractVersion = nextVersion, values = invalid })).StatusCode);
        Assert.Equal(saved.GetRawText(), (await Get(manager, path)).GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(path, new { expectedContractVersion = version, values })).StatusCode);
        var cleared = await Json(await manager.PutAsJsonAsync(path, new { expectedContractVersion = nextVersion, values = Array.Empty<object>() }), HttpStatusCode.OK);
        Assert.Empty(cleared.GetProperty("values").EnumerateArray());
        f.WithDb(db => Assert.Equal(100m, db.Contracts.Single(x => x.Id == Id(301)).MonthlySalary));
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    { var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}"); return JsonDocument.Parse(body).RootElement.Clone(); }
}
