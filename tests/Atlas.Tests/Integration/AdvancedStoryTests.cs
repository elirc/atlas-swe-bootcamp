using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class AdvancedStoryTests
{
    [Fact]
    public void SS09_LargestRemaindersPreserveEveryComponentAndRowEquations()
    {
        var money = new PayrollMoney(.07m, .01m, .02m, .01m, .02m, .01m, .01m, .05m, .11m);
        AllocationTarget[] targets = [new(Id(2), "B", "Beta", 3333), new(Id(1), "A", "Alpha", 3334), new(Id(3), "C", "Gamma", 3333)];
        var rows = CostAllocation.Calculate(money, targets);
        Assert.Equal(money, rows.Select(x => x.Amounts).Aggregate(PayrollMoney.Zero, (a, b) => a + b));
        Assert.All(rows, x => { Assert.Equal(x.Amounts.GrossSalary - x.Amounts.EmployeeDeductions - x.Amounts.BenefitsEmployeeDeduction + x.Amounts.Reimbursements, x.Amounts.NetPay);
            Assert.Equal(x.Amounts.GrossSalary + x.Amounts.EmployerCost + x.Amounts.BenefitsEmployerCost + x.Amounts.Reimbursements, x.Amounts.TotalCost); });
        Assert.Equal(.01m, rows.Single(x => x.Target.CostCenterId == Id(1)).Amounts.EmployerCost);
        Assert.Equal(rows, CostAllocation.Calculate(money, targets.Reverse().ToArray()));
        Assert.Throws<DomainException>(() => CostAllocation.Calculate(money with { EmployerCost = .001m }, targets));
        Assert.Equal(.001m, CostAllocation.Calculate(money with { EmployerCost = .001m }, [])[0].Amounts.EmployerCost);
        var huge = new PayrollMoney(decimal.MaxValue, 0, 0, 0, 0, 0, 0, decimal.MaxValue, decimal.MaxValue);
        Assert.Throws<DomainException>(() => CostAllocation.Calculate(huge, [new(Id(1), "A", "A", 5000), new(Id(2), "B", "B", 5000)]));
    }

    [Fact]
    public async Task SS09_SS10_SnapshotsKeepLabelsAndReportsReconcileLegacyAndCapturedCosts()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient(); var prefix = $"/api/clients/{Id(101)}";
        var center = await Json(await admin.PostAsJsonAsync(prefix + "/cost-centers", new { code = " eng ", name = "Engineering" }), HttpStatusCode.Created);
        var centerId = center.GetProperty("id").GetGuid(); Assert.Equal("ENG", center.GetProperty("code").GetString());
        await Json(await admin.PostAsJsonAsync($"/api/contracts/{Id(301)}/cost-allocations", new { year = 2026, month = 9, items = new[] { new { costCenterId = centerId, basisPoints = 10000 } } }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/contracts/{Id(302)}/cost-allocations", new { year = 2026, month = 9, items = new[] { new { costCenterId = centerId, basisPoints = 10000 } } })).StatusCode);
        Guid runId = default, slipId = default;
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>(); var payroll = scope.ServiceProvider.GetRequiredService<PayrollService>();
            var run = await payroll.CreateRunAsync("PH", 2026, 9); runId = run.Id; slipId = run.Payslips.Single(x => x.ContractId == Id(301)).Id;
            await payroll.CompleteRunAsync(runId);
            (await db.ClientCostCenters.SingleAsync()).Name = "Renamed later"; await db.SaveChangesAsync();
        }
        var saved = await Get(admin, $"/api/payslips/{slipId}/cost-allocations"); Assert.Equal("Engineering", saved.GetProperty("rows")[0].GetProperty("name").GetString());
        var report = await Get(admin, prefix + "/cost-center-payroll?fromYear=2026&fromMonth=7&toYear=2026&toMonth=9");
        var periods = report.GetProperty("periods").EnumerateArray().ToList(); Assert.Equal(2, periods.Count);
        Assert.All(periods, x => Assert.Equal(0m, x.GetProperty("reconciliationDifference").GetDecimal()));
        Assert.Equal(120m, periods.Single(x => x.GetProperty("month").GetInt32() == 7).GetProperty("legacyUncapturedTotal").GetDecimal());
        Assert.Contains(periods.Single(x => x.GetProperty("month").GetInt32() == 9).GetProperty("buckets").EnumerateArray(), x => x.GetProperty("bucketKey").GetString() == "unallocated");
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClientWithApiKey("bootcamp-viewer").GetAsync($"/api/clients/{Id(102)}/cost-center-payroll?fromYear=2026&fromMonth=7&toYear=2026&toMonth=9")).StatusCode);
    }

    [Fact]
    public void SS11_CsvHandlesEscapesAndRejectsMalformedOrOversizedInputs()
    {
        var rows = WorkerCsv.Parse("FullName,Email,CountryCode,DateOfBirth\r\n\"Lee, \"\"Sam\"\"\nJunior\",sam@example.test,PH,\r\n");
        Assert.Single(rows); Assert.Equal("Lee, \"Sam\"\nJunior", rows[0][0]); Assert.Equal("", rows[0][3]);
        Assert.Throws<DomainException>(() => WorkerCsv.Parse("FullName,Email,CountryCode,DateOfBirth\n\"unclosed,x,PH,"));
        Assert.Throws<DomainException>(() => WorkerCsv.Parse("FullName,Email,CountryCode,DateOfBirth\n\"closed\"junk,x,PH,"));
        Assert.Throws<DomainException>(() => WorkerCsv.Parse("FullName,Email,CountryCode,DateOfBirth\n" + string.Concat(Enumerable.Repeat("Name,x@example.test,PH,\n", 101))));
        Assert.Throws<DomainException>(() => WorkerCsv.Parse(new string('x', 1024 * 1024 + 1)));
    }

    [Fact]
    public async Task SS11_ValidationCreatesNoWorkersAndCommitReplaysItsMapping()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        var result = await Json(await admin.PostAsJsonAsync("/api/worker-imports/validate", new { csv = "FullName,Email,CountryCode,DateOfBirth\n\"Lee, Sam\", NEW@EXAMPLE.TEST ,ph,1990-01-01\nOther,other-new@example.test,PH," }), HttpStatusCode.Created);
        var id = result.GetProperty("id").GetGuid(); var version = result.GetProperty("version").GetInt32();
        Assert.Equal("Validated", result.GetProperty("status").GetString()); f.WithDb(db => Assert.Equal(4, db.Workers.Count()));
        var commit = await Json(await admin.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version }), HttpStatusCode.OK);
        var replay = await Json(await admin.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version }), HttpStatusCode.OK);
        Assert.Equal(commit.GetRawText(), replay.GetRawText()); f.WithDb(db => { Assert.Equal(6, db.Workers.Count()); Assert.Equal("Lee, Sam", db.Workers.Single(x => x.Email == "new@example.test").FullName); });
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-admin").GetAsync($"/api/worker-imports/{id}")).StatusCode);
        var invalid = await Json(await admin.PostAsJsonAsync("/api/worker-imports/validate", new { csv = "FullName,Email,CountryCode,DateOfBirth\nA,duplicate@example.test,PH,\nB,DUPLICATE@example.test,PH," }), HttpStatusCode.Created);
        Assert.Equal("Invalid", invalid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SS11_CompetingEmailAndInjectedFailureLeaveNoPartialWorkers()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        var result = await Json(await admin.PostAsJsonAsync("/api/worker-imports/validate", new { csv = "FullName,Email,CountryCode,DateOfBirth\nA,first-import@example.test,PH,\nB,last-import@example.test,PH," }), HttpStatusCode.Created);
        var id = result.GetProperty("id").GetGuid(); var version = result.GetProperty("version").GetInt32();
        f.WithDb(db => db.Database.ExecuteSqlRaw("CREATE TRIGGER fail_import BEFORE INSERT ON Workers WHEN NEW.Email = 'last-import@example.test' BEGIN SELECT RAISE(ABORT, 'simulated worker failure'); END;"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version })).StatusCode);
        f.WithDb(db => { Assert.Equal(4, db.Workers.Count()); Assert.Equal(WorkerImportStatus.Validated, db.WorkerImportBatches.Single().Status);
            db.Database.ExecuteSqlRaw("DROP TRIGGER fail_import;"); db.Workers.Add(new Worker { FullName = "Competitor", Email = "last-import@example.test", CountryCode = "PH" }); db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version })).StatusCode);
        f.WithDb(db => Assert.False(db.Workers.Any(x => x.Email == "first-import@example.test")));
    }

    [Fact]
    public async Task SS16_SS17_AuditAndInboxPreserveSourceAndPersonalReadState()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient(); var viewer = f.CreateClientWithApiKey("bootcamp-viewer"); var prefix = $"/api/clients/{Id(101)}";
        await Json(await admin.PostAsJsonAsync($"/api/contracts/{Id(301)}/terminate", new { endDate = "2026-09-15", reason = "Private reason" }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/contracts/{Id(301)}/terminate", new { endDate = "2026-09-15" })).StatusCode);
        var feed = await Get(viewer, prefix + "/contract-decisions?limit=1"); var decision = feed.GetProperty("items")[0];
        Assert.Equal("direct-termination", decision.GetProperty("sourceKind").GetString()); Assert.DoesNotContain("Private", feed.GetRawText());
        Assert.Equal("Active", decision.GetProperty("payload").GetProperty("before").GetProperty("status").GetString());
        var bookmark = feed.GetProperty("nextAfterSequence").GetInt64();
        Assert.Empty((await Get(viewer, prefix + $"/contract-decisions?afterSequence={bookmark}")).GetProperty("items").EnumerateArray());
        await using (var scope = f.Services.CreateAsyncScope()) { var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationService>(); Assert.Equal(1, await dispatcher.DispatchOnceAsync()); Assert.Equal(0, await dispatcher.DispatchOnceAsync()); }
        var inbox = await Get(viewer, prefix + "/notifications?unreadOnly=true"); Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());
        var notificationId = inbox.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync(prefix + $"/notifications/{notificationId}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync(prefix + $"/notifications/{notificationId}/read", null)).StatusCode);
        Assert.Equal(0, (await Get(viewer, prefix + "/notifications")).GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, (await Get(admin, prefix + "/notifications")).GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.PostAsync($"/api/clients/{Id(102)}/notifications/{notificationId}/read", null)).StatusCode);
    }

    [Fact]
    public async Task SS16_EventFailureRollsBackAmendmentSalaryHistoryAndContract()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        var amendment = await Json(await admin.PostAsJsonAsync("/api/contract-amendments", new { contractId = Id(301), newMonthlySalary = 140m, effectiveDate = "2026-10-01" }), HttpStatusCode.Created);
        var id = amendment.GetProperty("id").GetGuid();
        f.WithDb(db => db.Database.ExecuteSqlRaw("CREATE TRIGGER fail_decision BEFORE INSERT ON ContractDecisionEvents BEGIN SELECT RAISE(ABORT, 'simulated event failure'); END;"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync($"/api/contract-amendments/{id}/approve", new { note = "accepted" })).StatusCode);
        f.WithDb(db => { Assert.Equal(100m, db.Contracts.Single(x => x.Id == Id(301)).MonthlySalary); Assert.Empty(db.SalaryRecords.Where(x => x.AmendmentId == id));
            Assert.Equal(AmendmentStatus.Pending, db.ContractAmendments.Single().Status); Assert.Empty(db.ContractDecisionEvents); db.Database.ExecuteSqlRaw("DROP TRIGGER fail_decision;"); });
        await Json(await admin.PostAsJsonAsync($"/api/contract-amendments/{id}/approve", new { note = "accepted" }), HttpStatusCode.OK);
        f.WithDb(db => { Assert.Equal(140m, db.Contracts.Single(x => x.Id == Id(301)).MonthlySalary); Assert.Single(db.SalaryRecords.Where(x => x.AmendmentId == id)); Assert.Equal("amendment-applied", db.ContractDecisionEvents.Single().Kind); });
    }

    [Fact]
    public async Task SS19_FrozenLabelsAndSameDateComparisonsUseSavedRows()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient(); var prefix = $"/api/clients/{Id(101)}/workforce-snapshots";
        var a = await Json(await admin.PostAsJsonAsync(prefix, new { asOf = "2026-09-01", label = "Before" }), HttpStatusCode.Created); var aid = a.GetProperty("id").GetGuid();
        Assert.Equal(2, a.GetProperty("workerCount").GetInt32());
        f.WithDb(db => { db.Workers.Single(x => x.Id == Id(201)).FullName = "Changed later"; db.SaveChanges(); });
        var old = await Get(admin, prefix + $"/{aid}"); Assert.Equal("Sam Lee", old.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("workerId").GetGuid() == Id(201)).GetProperty("workerName").GetString());
        var b = await Json(await admin.PostAsJsonAsync(prefix, new { asOf = "2026-09-01", label = "After" }), HttpStatusCode.Created); var bid = b.GetProperty("id").GetGuid();
        var compared = await Json(await admin.PostAsJsonAsync(prefix + "/compare", new { baselineId = aid, comparisonId = bid }), HttpStatusCode.OK);
        var changed = compared.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("contractId").GetGuid() == Id(301));
        Assert.Equal("retained", changed.GetProperty("classification").GetString()); Assert.Equal("workerName", changed.GetProperty("changedFields")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/clients/{Id(102)}/workforce-snapshots/{aid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-viewer").PostAsJsonAsync(prefix, new { asOf = "2026-09-01", label = "No" })).StatusCode);
    }

    [Fact]
    public async Task SS16_ActivationAndTerminationApprovalRecordTheirActualSources()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        f.WithDb(db => { foreach (var item in db.OnboardingItems.Where(x => x.ContractId == Id(304))) item.IsRequired = false;
            db.Countries.Single(x => x.Code == "PH").MinimumNoticeDays = 0; db.SaveChanges(); });
        await Json(await admin.PostAsync($"/api/contracts/{Id(304)}/activate", null), HttpStatusCode.OK);
        var proposed = await Json(await admin.PostAsJsonAsync("/api/termination-requests", new { contractId = Id(301), proposedEndDate = "2026-09-15", reason = "Private" }), HttpStatusCode.Created);
        var requestId = proposed.GetProperty("id").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/termination-requests/{requestId}/approve", new { note = "Accepted" }), HttpStatusCode.OK);
        f.WithDb(db => { Assert.Equal(2, db.ContractDecisionEvents.Count()); Assert.Equal("contract-activation", db.ContractDecisionEvents.Single(x => x.ContractId == Id(304)).SourceKind);
            var decision = db.ContractDecisionEvents.Single(x => x.ContractId == Id(301)); Assert.Equal("termination-request-approval", decision.SourceKind); Assert.Equal(requestId, decision.SourceId);
            Assert.Equal(db.Contracts.Single(x => x.Id == Id(301)).Version, decision.ContractVersion); });
    }

    [Fact]
    public async Task SS11_ImportOwnerAndExactExpiryAreEnforced()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        var result = await Json(await admin.PostAsJsonAsync("/api/worker-imports/validate", new { csv = "FullName,Email,CountryCode,DateOfBirth\nA,expiry@example.test,PH," }), HttpStatusCode.Created);
        var id = result.GetProperty("id").GetGuid(); var version = result.GetProperty("version").GetInt32();
        f.WithDb(db => { db.ApiUsers.Add(new ApiUser { Name = "Other platform", Role = ApiRole.PlatformAdmin, ApiKey = "other-import-owner" });
            db.WorkerImportBatches.Single().ExpiresAtUtc = Now; db.SaveChanges(); });
        var other = f.CreateClientWithApiKey("other-import-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/worker-imports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await admin.PostAsJsonAsync($"/api/worker-imports/{id}/commit", new { expectedVersion = version })).StatusCode);
        f.WithDb(db => Assert.Equal(4, db.Workers.Count()));
    }

    [Fact]
    public async Task SS11_SharedWorkerValidationPreservesConfiguredCountryIdentifiers()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        f.WithDb(db => { db.Countries.Add(new Country { Code = "A1", Name = "Existing identifier format", CurrencyCode = "USD", EmployerCostRate = 0m, EmployeeDeductionRate = 0m }); db.SaveChanges(); });
        await Json(await admin.PostAsJsonAsync("/api/workers", new { fullName = "Compatible worker", email = "compatible@example.test", countryCode = " a1 " }), HttpStatusCode.Created);
        var imported = await Json(await admin.PostAsJsonAsync("/api/worker-imports/validate", new { csv = "FullName,Email,CountryCode,DateOfBirth\nA,compatible-import@example.test,a1," }), HttpStatusCode.Created);
        Assert.Equal("Validated", imported.GetProperty("status").GetString()); Assert.Equal("A1", imported.GetProperty("rows")[0].GetProperty("countryCode").GetString());
        var simulation = await Json(await admin.PostAsJsonAsync("/api/payroll-simulations", new { countryCode = " a1 ", year = 2026, month = 9 }), HttpStatusCode.OK);
        Assert.Equal("A1", simulation.GetProperty("countryCode").GetString());
    }

    [Fact]
    public async Task SS19_FailedSnapshotRowLeavesNoHeader()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        f.WithDb(db => db.Database.ExecuteSqlRaw("CREATE TRIGGER fail_snapshot BEFORE INSERT ON WorkforceSnapshotRow BEGIN SELECT RAISE(ABORT, 'simulated row failure'); END;"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync($"/api/clients/{Id(101)}/workforce-snapshots", new { asOf = "2026-09-01", label = "Failed" })).StatusCode);
        f.WithDb(db => { Assert.Empty(db.WorkforceSnapshots); Assert.Empty(db.Set<WorkforceSnapshotRow>()); });
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    { var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}"); return JsonDocument.Parse(body).RootElement.Clone(); }
}
