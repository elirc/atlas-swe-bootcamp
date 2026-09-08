using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atlas.Api;
using Atlas.Domain.Entities;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class MidlevelViewStoryTests
{
    [Fact]
    public async Task MS01_WorkforceCountsPeopleSeparatelyAndHonorsInclusiveEnd()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            db.Contracts.Single(c => c.Id == Id(303)).WorkerId = Id(201);
            var ending = db.Contracts.Single(c => c.Id == Id(301));
            ending.EndDate = new DateOnly(2026, 9, 1); ending.Status = ContractStatus.Terminated;
            db.SaveChanges();
        });
        var client = f.CreateClientWithApiKey("bootcamp-viewer");
        var first = await Get(client, $"/api/clients/{Id(101)}/workforce-summary?asOf=2026-09-01");
        Assert.Equal(1, first.GetProperty("workerCount").GetInt32());
        Assert.Equal(2, first.GetProperty("contractCount").GetInt32());
        var next = await Get(client, $"/api/clients/{Id(101)}/workforce-summary?asOf=2026-09-02");
        Assert.Equal(1, next.GetProperty("contractCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/clients/{Id(102)}/workforce-summary")).StatusCode);
    }

    [Fact]
    public async Task MS02_TimelineScopesContractsAfterWorkerVisibility()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            db.Contracts.Single(c => c.Id == Id(302)).WorkerId = Id(201);
            db.SalaryRecords.Add(new SalaryRecord { ContractId = Id(302), MonthlySalary = 999999, JobTitle = "Foreign title",
                EffectiveDate = new DateOnly(2026, 1, 1) }); db.SaveChanges();
        });
        var rows = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), $"/api/workers/{Id(201)}/employment-timeline");
        Assert.DoesNotContain("Foreign title", rows.GetRawText());
        Assert.DoesNotContain("999999", rows.GetRawText());
        Assert.All(rows.EnumerateArray(), r => Assert.Equal(Id(301), r.GetProperty("contractId").GetGuid()));
        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("kind").GetString() == "salary-effective");
    }

    [Fact]
    public async Task MS03_ReadinessIsReadOnlyAndSharesActivationRules()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db => { db.Contracts.Single(c => c.Id == Id(301)).Status = ContractStatus.Draft; db.SaveChanges(); });
        var client = f.CreateClientWithApiKey("bootcamp-admin");
        var path = $"/api/contracts/{Id(301)}";
        var first = await Get(client, path + "/activation-readiness");
        Assert.False(first.GetProperty("canActivate").GetBoolean());
        Assert.Equal(4, first.GetProperty("blockers").GetArrayLength());
        var version = first.GetProperty("version").GetInt32();
        Assert.Equal(version, (await Get(client, path + "/activation-readiness")).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + "/activate", null)).StatusCode);
        f.WithDb(db => { foreach (var item in db.OnboardingItems.Where(i => i.ContractId == Id(301) && i.IsRequired)) item.Complete(Now); db.SaveChanges(); });
        Assert.True((await Get(client, path + "/activation-readiness")).GetProperty("canActivate").GetBoolean());
        (await client.PostAsync(path + "/activate", null)).EnsureSuccessStatusCode();
        Assert.False((await Get(client, path + "/activation-readiness")).GetProperty("canActivate").GetBoolean());
    }

    [Fact]
    public async Task MS07_MS08_PayslipBrowserAndCsvUseSavedClientScope()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db => { db.Payslips.Single(p => p.Id == Id(703)).WorkerId = Id(201); db.Contracts.Single(c => c.Id == Id(301)).MonthlySalary = 9000; db.SaveChanges(); });
        var client = f.CreateClientWithApiKey("bootcamp-viewer");
        const string filter = "?fromPeriod=2026-06&toPeriod=2026-07";
        var rows = await Get(client, "/api/payslips" + filter);
        Assert.Equal(2, rows.GetArrayLength());
        Assert.All(rows.EnumerateArray(), r => Assert.Equal(Id(101), r.GetProperty("clientId").GetGuid()));
        Assert.Equal(100m, (await Get(client, $"/api/payslips/{Id(702)}")).GetProperty("grossSalary").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/payslips/{Id(703)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/payslips" + filter + $"&clientId={Id(102)}")).StatusCode);
        var export = await client.GetAsync("/api/payslips/export.csv" + filter);
        export.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains(Id(701).ToString(), csv); Assert.Contains(Id(702).ToString(), csv);
        Assert.DoesNotContain(Id(703).ToString(), csv);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/payslips?fromPeriod=2026-01&toPeriod=2027-01")).StatusCode);
        var empty = await client.GetStringAsync("/api/payslips/export.csv?fromPeriod=2020-01&toPeriod=2020-01");
        Assert.Single(empty.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void MS08_MS10_CsvEscapesStructureAndProtectsTextWithoutRoundingFx()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var bytes = CsvExport.Write(["Text", "Money", "FX"],
                [new object?[] { "  =SUM(1,2)\n\"quoted\"", 1234.50m, new CsvNumber(1.234567m, "0.############################") },
                 new object?[] { "\tformula", -10.25m, null }]);
            var csv = Encoding.UTF8.GetString(bytes);
            Assert.Contains("\"'  =SUM(1,2)\n\"\"quoted\"\"\"", csv);
            Assert.Contains(",1234.50,1.234567\r\n", csv);
            Assert.Contains("\"'\tformula\",-10.25,", csv);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public async Task MS09_MS10_InvoiceBreakdownShowsMismatchAndExportKeepsSnapshots()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db => { db.Invoices.Single(i => i.Id == Id(801)).PayrollSubtotal = 120.01m; db.Invoices.Single(i => i.Id == Id(801)).FxRateApplied = 1.234567m; db.SaveChanges(); });
        var client = f.CreateClientWithApiKey("bootcamp-viewer");
        var body = await Get(client, $"/api/invoices/{Id(801)}/breakdown");
        Assert.Equal(120m, body.GetProperty("calculatedPayslipSubtotal").GetDecimal());
        Assert.Equal(.01m, body.GetProperty("subtotalDifference").GetDecimal());
        Assert.Single(body.GetProperty("payslips").EnumerateArray());
        var csv = await client.GetStringAsync("/api/invoices/export.csv?issuedFrom=2026-08-01&issuedTo=2026-08-31");
        Assert.Contains("1.234567", csv); Assert.Contains(Id(801).ToString(), csv); Assert.DoesNotContain(Id(802).ToString(), csv);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{Id(802)}/breakdown")).StatusCode);
    }

    [Fact]
    public async Task MS14_LeaveCalendarClipsDisplayWithoutChangingReservation()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            db.LeaveRequests.AddRange(
                new LeaveRequest { Id = Id(1001), ContractId = Id(301), Type = LeaveType.Annual, StartDate = new(2026, 8, 28), EndDate = new(2026, 9, 4), Days = 6, Status = LeaveRequestStatus.Approved },
                new LeaveRequest { Id = Id(1002), ContractId = Id(302), Type = LeaveType.Annual, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 4), Days = 4 });
            db.SaveChanges();
        });
        var rows = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), $"/api/clients/{Id(101)}/leave-calendar?from=2026-09-01&to=2026-09-30");
        var row = Assert.Single(rows.EnumerateArray());
        Assert.Equal("2026-09-01", row.GetProperty("displayStart").GetString());
        Assert.Equal("2026-08-28", row.GetProperty("startDate").GetString());
        Assert.Equal(6, row.GetProperty("days").GetInt32());
        f.WithDb(db => Assert.Equal(6, db.LeaveRequests.Single(r => r.Id == Id(1001)).Days));
    }

    [Fact]
    public async Task MS21_ExpenseSummaryGroupsItemsRatherThanRepeatingClaims()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            var claim = new ExpenseClaim { Id = Id(1101), ContractId = Id(301), CurrencyCode = "USD", Status = ExpenseClaimStatus.Approved };
            claim.Items.AddRange([new ExpenseItem { ExpenseClaimId = claim.Id, Description = "January", Amount = 10, IncurredDate = new(2026, 1, 15) },
                new ExpenseItem { ExpenseClaimId = claim.Id, Description = "February", Amount = 20, IncurredDate = new(2026, 2, 15) }]);
            db.ExpenseClaims.Add(claim); db.SaveChanges();
        });
        var body = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), $"/api/clients/{Id(101)}/expense-summary?fromPeriod=2026-01&toPeriod=2026-02");
        Assert.Equal(new[] { 10m, 20m }, body.GetProperty("groups").EnumerateArray().Select(r => r.GetProperty("approvedAmount").GetDecimal()));
        Assert.All(body.GetProperty("groups").EnumerateArray(), r => Assert.Equal(1, r.GetProperty("distinctClaimCount").GetInt32()));
    }

    [Fact]
    public async Task MS22_MS23_BenefitSharesAndHistoricalOverlapUseExistingRules()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            db.BenefitPlans.Add(new BenefitPlan { Id = Id(1201), CountryCode = "PH", Name = "Odd cents", MonthlyCost = 10.01m, EmployerContributionRate = .5m });
            db.BenefitEnrollments.Add(new BenefitEnrollment { Id = Id(1202), ContractId = Id(301), BenefitPlanId = Id(1201),
                StartDate = new(2026, 1, 31), EndDate = new(2026, 3, 1), Status = BenefitEnrollmentStatus.Ended }); db.SaveChanges();
        });
        var client = f.CreateClientWithApiKey("bootcamp-admin");
        var quoteResponse = await client.PostAsJsonAsync($"/api/contracts/{Id(301)}/benefit-quote", new { planIds = new[] { Id(1201) } });
        quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5.01m, quote.GetProperty("employerShare").GetDecimal());
        Assert.Equal(5m, quote.GetProperty("employeeShare").GetDecimal());
        f.WithDb(db => { db.BenefitPlans.Single().IsActive = false; db.SaveChanges(); });
        var calendar = await Get(client, $"/api/contracts/{Id(301)}/benefit-calendar?fromPeriod=2026-01&toPeriod=2026-04");
        Assert.Equal(new[] { 1, 1, 1, 0 }, calendar.GetProperty("months").EnumerateArray().Select(m => m.GetProperty("enrollments").GetArrayLength()));
        f.WithDb(db => Assert.Single(db.BenefitEnrollments));
    }

    [Fact]
    public async Task MS29_QueueCountsAndGlobalOrderingStayInsideClient()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db =>
        {
            db.LeaveRequests.Add(new LeaveRequest { ContractId = Id(301), Type = LeaveType.Annual, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 1), Days = 1, RequestedAtUtc = Now });
            db.LeaveRequests.Add(new LeaveRequest { ContractId = Id(302), Type = LeaveType.Annual, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 1), Days = 1, RequestedAtUtc = Now.AddHours(1) });
            db.ExpenseClaims.Add(new ExpenseClaim { ContractId = Id(301), CurrencyCode = "USD", SubmittedAtUtc = Now });
            db.ContractAmendments.Add(new ContractAmendment { ContractId = Id(301), NewMonthlySalary = 120, EffectiveDate = new(2026, 10, 1), RequestedAtUtc = Now });
            db.TerminationRequests.Add(new TerminationRequest { ContractId = Id(301), Reason = "Private", NoticeDate = new(2026, 9, 1), ProposedEndDate = new(2026, 10, 1), RequestedAtUtc = Now });
            db.SaveChanges();
        });
        var body = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), $"/api/clients/{Id(101)}/approval-queue?limit=2");
        Assert.Equal(4, body.GetProperty("totalPending").GetInt32()); Assert.True(body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(new[] { "amendment", "expense" }, body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("kind").GetString()));
        Assert.DoesNotContain("Private", body.GetRawText());
    }

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
