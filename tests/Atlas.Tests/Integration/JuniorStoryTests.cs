using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain.Entities;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class JuniorStoryTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task JS01_CountryName_TrimmedBoundary(int length, bool accepted)
    {
        using var f = new BootcampApiFactory();
        var client = f.CreateClient();
        var response = await client.PostAsJsonAsync("/api/countries", new
        { code = "GB", name = "  " + new string('A', length) + "  ", currencyCode = "USD", employerCostRate = .1m, employeeDeductionRate = .1m });
        await CheckWrite(response, accepted, "name");
        f.WithDb(db => Assert.Equal(accepted, db.Countries.Any(c => c.Code == "GB")));
        if (accepted) Assert.Equal(length, (await Json(response)).GetProperty("name").GetString()!.Length);
    }

    [Theory]
    [InlineData(" usd ", true)]
    [InlineData("XYZ", true)]
    [InlineData("U1D", false)]
    [InlineData("US!", false)]
    [InlineData("U\u00e9D", false)]
    [InlineData("U\u017fD", false)]
    public async Task JS02_CountryCurrency_AsciiShape(string currency, bool accepted)
    {
        using var f = new BootcampApiFactory();
        var response = await f.CreateClient().PostAsJsonAsync("/api/countries", new
        { code = "GB", name = "Test", currencyCode = currency, employerCostRate = .1m, employeeDeductionRate = .1m });
        await CheckWrite(response, accepted, "currencyCode");
        f.WithDb(db => Assert.Equal(accepted, db.Countries.Any(c => c.Code == "GB")));
        if (accepted) Assert.Equal(currency.Trim().ToUpperInvariant(), (await Json(response)).GetProperty("currencyCode").GetString());
    }

    [Theory]
    [InlineData(200, 200, true, "")]
    [InlineData(201, 20, false, "name")]
    [InlineData(20, 201, false, "legalName")]
    [InlineData(200, 0, true, "")]
    public async Task JS03_ClientNames_BoundsAndLegalFallback(int nameLength, int legalLength, bool accepted, string field)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var response = await f.CreateClient().PostAsJsonAsync("/api/clients", new
        { name = " " + new string('N', nameLength) + " ", legalName = " " + new string('L', legalLength) + " ",
            billingEmail = "new@example.test", headquartersCountryCode = "PH" });
        await CheckWrite(response, accepted, field);
        f.WithDb(db => Assert.Equal(accepted, db.Clients.Any(c => c.BillingEmail == "new@example.test")));
        if (accepted && legalLength == 0)
        {
            var body = await Json(response);
            Assert.Equal(body.GetProperty("name").GetString(), body.GetProperty("legalName").GetString());
        }
    }

    [Theory]
    [InlineData(200, "2026-09-01", true, "")]
    [InlineData(201, "1990-01-01", false, "fullName")]
    [InlineData(20, "2026-09-02", false, "dateOfBirth")]
    [InlineData(20, null, true, "")]
    public async Task JS04_JS08_WorkerNameAndBirthDate(int length, string? birth, bool accepted, string field)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var response = await f.CreateClient().PostAsJsonAsync("/api/workers", new
        { fullName = " " + new string('W', length) + " ", email = "new@example.test", countryCode = "PH", dateOfBirth = birth });
        await CheckWrite(response, accepted, field);
        f.WithDb(db => Assert.Equal(accepted, db.Workers.Any(w => w.Email == "new@example.test")));
    }

    [Theory]
    [InlineData("passport", 200, true, "")]
    [InlineData("0", 200, true, "")]
    [InlineData("-1", 20, false, "type")]
    [InlineData("999", 20, false, "type")]
    [InlineData("Passport", 201, false, "name")]
    public async Task JS05_JS06_DocumentValidationAndLocation(string type, int length, bool accepted, string field)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var client = f.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/workers/{Id(201)}/documents", new { name = " " + new string('D', length) + " ", type });
        await CheckWrite(response, accepted, field);
        f.WithDb(db => Assert.Equal(accepted ? 4 : 3, db.ComplianceDocuments.Count(d => d.WorkerId == Id(201))));
        if (accepted)
        {
            var saved = await Json(response);
            var detail = await client.GetAsync(response.Headers.Location);
            Assert.Equal(saved.GetProperty("id").GetGuid(), (await Json(detail)).GetProperty("id").GetGuid());
        }
    }

    [Theory]
    [InlineData("/api/contracts?status=999", "status")]
    [InlineData("/api/contracts?status=-1", "status")]
    [InlineData("/api/contracts?countryCode=P1", "countryCode")]
    [InlineData("/api/countries?currencyCode=U1D", "currencyCode")]
    [InlineData("/api/payroll-runs?month=7", "year")]
    [InlineData("/api/payroll-runs?year=1999", "year")]
    [InlineData("/api/payroll-runs?year=2026&month=13", "month")]
    [InlineData("/api/fx-rates?effectiveFrom=2026-03-01&effectiveTo=2026-02-01", "effectiveTo")]
    [InlineData("/api/reports/headcount?countryCode=BAD", "countryCode")]
    [InlineData("/api/reports/compliance-expiries?countryCode=P1", "countryCode")]
    [InlineData("/api/reports/payroll-costs?runStatus=999", "runStatus")]
    public async Task JuniorFilters_RejectMalformedDomainInputs(string route, string field)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var response = await f.CreateClient().GetAsync(route);
        await CheckWrite(response, false, field);
    }

    [Theory]
    [InlineData("/api/countries?currencyCode=%20usd%20", 1)]
    [InlineData("/api/countries?isActive=false", 1)]
    [InlineData("/api/countries?currencyCode=USD&isActive=false", 0)]
    [InlineData("/api/countries?currencyCode=ZZZ", 0)]
    [InlineData("/api/countries", 2)]
    public async Task JS09_JS10_CountryFilters_CountSameSelection(string route, int count)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        Assert.Equal(count, (await Page(f.CreateClient(), route)).Count);
    }

    [Theory]
    [InlineData("clients", "ac", 2)]
    [InlineData("workers", "sa", 2)]
    [InlineData("clients", "%_", 1)]
    [InlineData("workers", "%_", 1)]
    public async Task JS11_JS12_PrefixIsLiteralAndScoped(string resource, string prefix, int count)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var route = $"/api/{resource}?namePrefix={Uri.EscapeDataString(" " + prefix + " ")}";
        Assert.Equal(count, (await Page(f.CreateClient(), route)).Count);
        var own = await Page(f.CreateClientWithApiKey("bootcamp-viewer"), route);
        Assert.Equal(prefix == "%_" ? 0 : 1, own.Count);
        var invalid = await f.CreateClient().GetAsync($"/api/{resource}?namePrefix={new string('a', 201)}");
        await CheckWrite(invalid, false, "namePrefix");
    }

    [Theory]
    [InlineData("clients", 100)]
    [InlineData("workers", 200)]
    public async Task JS13_JS14_NameTiesHaveExplicitIdentifierOrder(string resource, int offset)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            if (resource == "clients") foreach (var c in db.Clients.Where(c => c.Id != Id(104))) c.Name = "Twin";
            else foreach (var w in db.Workers.Where(w => w.Id != Id(204))) w.FullName = "Twin";
            db.SaveChanges();
        });
        for (var page = 1; page <= 3; page++)
        {
            var rows = await Page(f.CreateClient(), $"/api/{resource}?namePrefix=Twin&page={page}&pageSize=1", 3);
            Assert.Equal(Id(offset + page), Assert.Single(rows).GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task JS07_JS15_ContractFilters_ComposeWithScope()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var own = f.CreateClientWithApiKey("bootcamp-viewer");
        Assert.Equal(2, (await Page(own, "/api/contracts?countryCode=%20ph%20&status=1")).Count);
        Assert.Empty(await Page(own, "/api/contracts?countryCode=US&status=Active"));
        Assert.Empty(await Page(own, $"/api/contracts?countryCode=PH&clientId={Id(102)}"));
        Assert.Single(await Page(f.CreateClient(), "/api/contracts?countryCode=US&status=active"));
    }

    [Fact]
    public async Task JS16_JS17_InvoiceRunAndPayrollPeriodFilters()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        Assert.Equal(2, (await Page(f.CreateClient(), $"/api/invoices?payrollRunId={Id(402)}")).Count);
        Assert.Single(await Page(f.CreateClientWithApiKey("bootcamp-viewer"), $"/api/invoices?payrollRunId={Id(402)}"));
        Assert.Empty(await Page(f.CreateClient(), $"/api/invoices?payrollRunId={Guid.Empty}"));
        Assert.Equal(3, (await Page(f.CreateClient(), "/api/payroll-runs?year=2026")).Count);
        Assert.Single(await Page(f.CreateClient(), "/api/payroll-runs?countryCode=PH&year=2026&month=7"));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-viewer").GetAsync("/api/payroll-runs?year=2026")).StatusCode);
    }

    [Theory]
    [InlineData("effectiveFrom=2026-02-01&effectiveTo=2026-03-01", 2)]
    [InlineData("effectiveFrom=2026-02-01", 2)]
    [InlineData("effectiveTo=2026-02-01", 2)]
    [InlineData("effectiveFrom=2026-02-01&effectiveTo=2026-02-01", 1)]
    public async Task JS18_FxBoundsAreInclusive(string filter, int count)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        Assert.Equal(count, (await Page(f.CreateClient(), "/api/fx-rates?baseCurrency=USD&quoteCurrency=EUR&" + filter)).Count);
    }

    [Fact]
    public async Task JS19_RequiredView_DoesNotChangeCompletionMeaning()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var client = f.CreateClientWithApiKey("bootcamp-admin");
        var route = $"/api/contracts/{Id(301)}/onboarding";
        var all = await Json(await client.GetAsync(route));
        var required = await Json(await client.GetAsync(route + "?requiredOnly=true"));
        Assert.Equal(5, all.GetProperty("items").GetArrayLength());
        Assert.Equal(4, required.GetProperty("items").GetArrayLength());
        Assert.False(required.GetProperty("isComplete").GetBoolean());
        foreach (var item in required.GetProperty("items").EnumerateArray())
            (await client.PostAsJsonAsync(route + $"/{item.GetProperty("id").GetGuid()}/complete", new { })).EnsureSuccessStatusCode();
        foreach (var suffix in new[] { "", "?requiredOnly=true", "?requiredOnly=false" })
            Assert.True((await Json(await client.GetAsync(route + suffix))).GetProperty("isComplete").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/contracts/{Id(302)}/onboarding?requiredOnly=true")).StatusCode);
    }

    [Fact]
    public async Task JS20_JS21_Documents_RespectBothParentAndType()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var viewer = f.CreateClientWithApiKey("bootcamp-viewer");
        foreach (var type in new[] { "Passport", "passport", "0" })
        {
            var body = await Json(await viewer.GetAsync($"/api/workers/{Id(201)}/documents?type={type}"));
            Assert.Equal(Id(501), Assert.Single(body.EnumerateArray()).GetProperty("id").GetGuid());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/workers/{Id(202)}/documents?type=Passport")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/workers/{Id(201)}/documents/{Id(504)}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/workers/{Id(201)}/documents/{Id(501)}")).StatusCode);
        await CheckWrite(await viewer.GetAsync($"/api/workers/{Id(201)}/documents?type=999"), false, "type");
    }

    [Fact]
    public async Task JS22_SalaryCutoff_ReturnsHistoryNotJustLatest()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var client = f.CreateClientWithApiKey("bootcamp-viewer");
        var route = $"/api/contracts/{Id(301)}/salary-history";
        Assert.Equal(3, (await Json(await client.GetAsync(route))).GetArrayLength());
        Assert.Equal(2, (await Json(await client.GetAsync(route + "?effectiveOnOrBefore=2026-06-01"))).GetArrayLength());
        Assert.Equal(0, (await Json(await client.GetAsync(route + "?effectiveOnOrBefore=2025-12-31"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/contracts/{Id(302)}/salary-history?effectiveOnOrBefore=2026-06-01")).StatusCode);
    }

    [Fact]
    public async Task JS23_JS24_JS25_ReportsFilterBeforeAggregation()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var client = f.CreateClient();
        var headcount = await Json(await client.GetAsync("/api/reports/headcount?asOf=2026-09-01&countryCode=%20ph%20"));
        Assert.Equal(2, headcount.GetProperty("total").GetInt32());
        Assert.Equal(2, headcount.GetProperty("byClient").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        Assert.Single(headcount.GetProperty("byCountry").EnumerateArray());
        var docs = await Json(await client.GetAsync("/api/reports/compliance-expiries?asOf=2026-09-01&countryCode=PH"));
        Assert.Equal(2, docs.GetArrayLength());
        Assert.All(docs.EnumerateArray(), d => Assert.Equal("PH", d.GetProperty("countryCode").GetString()));
        var draft = await Json(await client.GetAsync("/api/reports/payroll-costs?runStatus=0"));
        Assert.Equal(50m, Assert.Single(draft.EnumerateArray()).GetProperty("totalGross").GetDecimal());
        var completed = await Json(await client.GetAsync("/api/reports/payroll-costs?runStatus=completed&countryCode=PH"));
        Assert.Equal(100m, Assert.Single(completed.EnumerateArray()).GetProperty("totalGross").GetDecimal());
    }

    [Theory]
    [InlineData("/api/countries?isActive=maybe")]
    [InlineData("/api/invoices?payrollRunId=wrong")]
    [InlineData("/api/fx-rates?effectiveFrom=not-a-date")]
    public async Task BindingErrorsRemainBadRequests(string route)
    {
        using var f = new BootcampApiFactory();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateClient().GetAsync(route)).StatusCode);
    }

    private static async Task<List<JsonElement>> Page(HttpClient client, string route, int? total = null)
    {
        var response = await client.GetAsync(route); response.EnsureSuccessStatusCode();
        var rows = (await Json(response)).EnumerateArray().ToList();
        Assert.Equal((total ?? rows.Count).ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
        return rows;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task CheckWrite(HttpResponseMessage response, bool accepted, string field)
    {
        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        if (!accepted)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.True((await Json(response)).GetProperty("errors").TryGetProperty(field, out _));
        }
    }
}
