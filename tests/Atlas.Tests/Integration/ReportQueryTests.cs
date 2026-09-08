using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Tests.Integration;

public class ReportQueryTests
{
    [Theory]
    [InlineData("fromYear=0", "fromYear")]
    [InlineData("fromYear=-1", "fromYear")]
    [InlineData("toYear=10000", "toYear")]
    [InlineData("fromYear=2147483647", "fromYear")]
    [InlineData("toYear=-2147483648", "toYear")]
    [InlineData("fromMonth=7", "fromYear")]
    [InlineData("toMonth=7", "toYear")]
    [InlineData("fromYear=2026&fromMonth=0", "fromMonth")]
    [InlineData("toYear=2026&toMonth=13", "toMonth")]
    [InlineData("fromYear=2027&toYear=2026", "toYear")]
    [InlineData("fromYear=2026&fromMonth=8&toYear=2026&toMonth=7", "toYear")]
    public async Task InvalidPeriods_ReturnFieldSpecific400(string query, string field)
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/reports/payroll-costs?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("fromYear=1&toYear=9999")]
    [InlineData("fromYear=2026&toYear=2026")]
    [InlineData("fromYear=2026&fromMonth=7&toYear=2026&toMonth=7")]
    [InlineData("toYear=2026")]
    public async Task ValidPeriods_AcceptInclusiveAndOpenBounds(string query)
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reports/payroll-costs?" + query)).StatusCode);
    }

    [Theory]
    [InlineData("/api/compliance/expiring", "withinDays=2147483647")]
    [InlineData("/api/reports/compliance-expiries", "withinDays=2147483647")]
    [InlineData("/api/compliance/expiring", "asOf=9999-12-31&withinDays=1")]
    [InlineData("/api/reports/compliance-expiries", "asOf=9999-12-31&withinDays=1")]
    [InlineData("/api/compliance/expiring", "withinDays=-1")]
    [InlineData("/api/reports/compliance-expiries", "withinDays=-1")]
    public async Task InvalidWindows_Return400InsteadOfOverflow(string route, string query)
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync(route + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("withinDays", out _));
    }

    [Fact]
    public async Task Compliance_UsesExplicitDateAndInclusiveWindow_OnBothRoutes()
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        factory.WithDb(db =>
        {
            db.Countries.Add(Country());
            var worker = new Worker { FullName = "Date learner", Email = "dates@example.com", CountryCode = "QT" };
            db.Workers.Add(worker);
            foreach (var offset in new[] { -1, 0, 30, 31 })
                db.ComplianceDocuments.Add(new ComplianceDocument
                {
                    WorkerId = worker.Id, Name = $"Day {offset}", Type = ComplianceDocumentType.Passport,
                    ExpiryDate = new DateOnly(2024, 2, 29).AddDays(offset),
                    CreatedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                });
            db.ComplianceDocuments.Add(new ComplianceDocument
            {
                WorkerId = worker.Id, Name = "No expiry", Type = ComplianceDocumentType.Other,
                CreatedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });
            db.ComplianceDocuments.Add(new ComplianceDocument
            {
                WorkerId = worker.Id, Name = "Recorded after the report date", Type = ComplianceDocumentType.Passport,
                ExpiryDate = new DateOnly(2024, 2, 29), CreatedAtUtc = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
            });
            db.SaveChanges();
        });
        var report = await client.GetFromJsonAsync<List<ComplianceExpiryRowResponse>>(
            "/api/reports/compliance-expiries?asOf=2024-02-29&withinDays=30");
        Assert.NotNull(report);
        Assert.Equal(new[] { -1, 0, 30 }, report.Select(r => r.DaysUntilExpiry));
        Assert.Equal(new[] { "Expired", "ExpiringSoon", "ExpiringSoon" }, report.Select(r => r.Status));
        var operational = await client.GetFromJsonAsync<List<ExpiringDocumentResponse>>(
            "/api/compliance/expiring?asOf=2024-02-29&withinDays=30");
        Assert.Equal(report.Select(r => r.DocumentId), operational!.Select(r => r.Id));
    }

    [Fact]
    public async Task InvoiceAging_UsesUtcBoundaries_ExcludesFutureIssues_AndSeparatesCurrencies()
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        var asOf = new DateOnly(2026, 6, 30);
        factory.WithDb(db =>
        {
            db.Countries.Add(Country());
            var company = new Client
            {
                Name = "Report learner", LegalName = "Report learner", BillingEmail = "report@example.com",
                HeadquartersCountryCode = "QT", BillingCurrencyCode = "USD",
            };
            db.Clients.Add(company);
            foreach (var (age, index) in new[] { 0, 30, 31, 60, 61, 90, 91, -1, 0 }.Select((age, index) => (age, index)))
            {
                var run = new PayrollRun { CountryCode = "QT", Year = 2026, Month = index + 1 };
                db.PayrollRuns.Add(run);
                var currency = index == 8 ? "EUR" : "USD";
                var invoice = new Invoice
                {
                    InvoiceNumber = $"LEARN-{index}", ClientId = company.Id, PayrollRunId = run.Id,
                    CurrencyCode = currency, BillingCurrencyCode = currency, PayrollSubtotal = 100.01m,
                    ManagementFee = 0, Total = 100.01m, TotalInBillingCurrency = 100.01m, FxRateApplied = 1,
                    IssuedAtUtc = new DateTimeOffset(asOf.AddDays(-age).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                };
                // Local July 1, but still June 30 in UTC; must be included.
                if (index == 8) invoice.IssuedAtUtc = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.FromHours(2));
                db.Invoices.Add(invoice);
            }
            db.SaveChanges();
        });
        var report = await client.GetFromJsonAsync<InvoiceAgingReportResponse>(
            "/api/reports/invoice-aging?asOf=2026-06-30");
        Assert.Equal(asOf, report!.AsOf);
        Assert.Equal(8, report.Rows.Sum(r => r.InvoiceCount));
        var usd = report.Rows.Where(r => r.CurrencyCode == "USD").ToList();
        Assert.Equal(new[] { "0-30", "31-60", "61-90", "90+" }, usd.Select(r => r.Bucket));
        Assert.Equal(new[] { 200.02m, 200.02m, 200.02m, 100.01m }, usd.Select(r => r.Total));
        Assert.Equal(100.01m, Assert.Single(report.Rows, r => r.CurrencyCode == "EUR").Total);
    }

    [Fact]
    public async Task Reports_DefaultToInjectedUtcClock_AndExplicitDateOverridesIt()
    {
        using var factory = new AtlasApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FixedClock())));
        using var client = configured.CreateClient();
        var headcount = await client.GetFromJsonAsync<HeadcountReportResponse>("/api/reports/headcount");
        var aging = await client.GetFromJsonAsync<InvoiceAgingReportResponse>("/api/reports/invoice-aging");
        Assert.Equal(new DateOnly(2024, 2, 29), headcount!.AsOf);
        Assert.Equal(headcount.AsOf, aging!.AsOf);
        var explicitDate = await client.GetFromJsonAsync<InvoiceAgingReportResponse>(
            "/api/reports/invoice-aging?asOf=2025-01-01");
        Assert.Equal(new DateOnly(2025, 1, 1), explicitDate!.AsOf);
    }

    [Theory]
    [InlineData("/api/reports/invoice-aging?asOf=9999-12-31")]
    [InlineData("/api/reports/invoice-aging?asOf=0001-01-01")]
    [InlineData("/api/reports/compliance-expiries?asOf=9999-12-31&withinDays=0")]
    [InlineData("/api/compliance/expiring?asOf=9999-12-31&withinDays=0")]
    public async Task DateLimits_AreSupported(string url)
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    private static Country Country() => new()
    {
        Code = "QT", Name = "Query Test", CurrencyCode = "USD", EmployerCostRate = 0, EmployeeDeductionRate = 0,
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2024, 2, 29, 23, 59, 59, TimeSpan.Zero);
    }
}
