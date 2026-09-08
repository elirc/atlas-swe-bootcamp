using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Domain.Services;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class PayrollReliabilityStoryTests
{
    [Fact]
    public async Task SS01_SS02_SimulationDoesNotConsumeInputsAndEvidenceSurvivesSourceChanges()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db =>
        {
            var claim = new ExpenseClaim { Id = Id(970), ContractId = Id(301), CurrencyCode = "USD", Status = ExpenseClaimStatus.Approved, Description = "Private expense narrative" };
            claim.Items.Add(new ExpenseItem { ExpenseClaimId = claim.Id, Description = "Private item", Amount = 50, IncurredDate = new DateOnly(2026, 8, 1), ReceiptUrl = "https://private.example/receipt" });
            db.ExpenseClaims.Add(claim);
            db.BenefitPlans.Add(new BenefitPlan { Id = Id(971), CountryCode = "PH", Name = "Health", MonthlyCost = 10.01m, EmployerContributionRate = .5m });
            db.BenefitEnrollments.Add(new BenefitEnrollment { ContractId = Id(301), BenefitPlanId = Id(971), StartDate = new DateOnly(2026, 1, 1) }); db.SaveChanges();
        });
        var request = new { countryCode = "PH", year = 2026, month = 9 };
        var first = await Json(await client.PostAsJsonAsync("/api/payroll-simulations", request), HttpStatusCode.OK);
        var second = await Json(await client.PostAsJsonAsync("/api/payroll-simulations", request), HttpStatusCode.OK);
        Assert.Equal(first.GetProperty("contracts").GetRawText(), second.GetProperty("contracts").GetRawText());
        f.WithDb(db => { Assert.Equal(4, db.PayrollRuns.Count()); Assert.Equal(ExpenseClaimStatus.Approved, db.ExpenseClaims.Single().Status); Assert.Empty(db.PayslipCalculationEvidence); });
        var created = await Json(await client.PostAsJsonAsync("/api/payroll-runs", request), HttpStatusCode.Created);
        foreach (var slip in created.GetProperty("payslips").EnumerateArray())
        {
            var input = first.GetProperty("contracts").EnumerateArray().Single(x => x.GetProperty("inputs").GetProperty("contractId").GetGuid() == slip.GetProperty("contractId").GetGuid());
            foreach (var amount in input.GetProperty("amounts").EnumerateObject()) Assert.Equal(amount.Value.GetDecimal(), slip.GetProperty(amount.Name).GetDecimal());
        }
        var target = created.GetProperty("payslips").EnumerateArray().Single(x => x.GetProperty("contractId").GetGuid() == Id(301)).GetProperty("id").GetGuid();
        var viewer = f.CreateClientWithApiKey("bootcamp-viewer"); var evidencePath = $"/api/payslips/{target}/calculation-evidence";
        var evidence = await Get(viewer, evidencePath);
        Assert.True(evidence.GetProperty("matchesSavedPayslip").GetBoolean()); Assert.True(evidence.GetProperty("matchesCapturedOutputs").GetBoolean());
        Assert.DoesNotContain("Private", evidence.GetRawText()); Assert.DoesNotContain("receipt", evidence.GetRawText()); Assert.DoesNotContain("ApiKey", evidence.GetRawText());
        f.WithDb(db => { db.Countries.Single(x => x.Code == "PH").EmployerCostRate = .8m; db.SalaryRecords.Single(x => x.Id == Id(603)).MonthlySalary = 9999m;
            db.BenefitPlans.Single().MonthlyCost = 500m; db.SaveChanges(); });
        Assert.Equal(evidence.GetRawText(), (await Get(viewer, evidencePath)).GetRawText());
        Assert.False((await Get(viewer, $"/api/payslips/{Id(701)}/calculation-evidence")).GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/payslips/{Id(703)}/calculation-evidence")).StatusCode);
        var again = await Json(await client.PostAsJsonAsync("/api/payroll-simulations", request), HttpStatusCode.OK);
        Assert.Contains(again.GetProperty("creationBlockers").EnumerateArray(), x => x.GetProperty("code").GetString() == "run-already-exists");
        f.WithDb(db => { db.PayslipCalculationEvidence.Single(x => x.PayslipId == target).CalculatorVersion = "future-version"; db.SaveChanges(); });
        Assert.False((await Get(viewer, evidencePath)).GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task SS03_VarianceUsesSavedFactsAndReconcilesBothSides()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        var report = await Get(client, $"/api/reports/payroll-variance?baselineRunId={Id(401)}&comparisonRunId={Id(402)}");
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal("retained", row.GetProperty("classification").GetString()); Assert.Equal(60m, row.GetProperty("delta").GetProperty("totalCost").GetDecimal());
        Assert.Equal(60m, report.GetProperty("totals")[0].GetProperty("delta").GetProperty("totalCost").GetDecimal());
        Assert.Equal("Draft", report.GetProperty("baseline").GetProperty("status").GetString());
        f.WithDb(db => { db.Contracts.Single(x => x.Id == Id(301)).MonthlySalary = 9999m; db.SaveChanges(); });
        Assert.Equal(report.GetRawText(), (await Get(client, $"/api/reports/payroll-variance?baselineRunId={Id(401)}&comparisonRunId={Id(402)}")).GetRawText());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/reports/payroll-variance?baselineRunId={Id(402)}&comparisonRunId={Id(401)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/reports/payroll-variance?baselineRunId={Id(401)}&comparisonRunId={Id(403)}")).StatusCode);
    }

    [Fact]
    public async Task SS04_FailedFxDoesNotConsumeKeyAndSuccessfulRetryReturnsExactResponse()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db => { db.Clients.Single(x => x.Id == Id(101)).BillingCurrencyCode = "GBP"; db.FxRates.RemoveRange(db.FxRates); db.SaveChanges(); });
        client.DefaultRequestHeaders.Add("Idempotency-Key", " close-june ");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/payroll-runs/{Id(401)}/complete", null)).StatusCode);
        f.WithDb(db => { Assert.Empty(db.OperationReceipts); Assert.Equal(PayrollRunStatus.Draft, db.PayrollRuns.Single(x => x.Id == Id(401)).Status);
            db.FxRates.Add(new FxRate { BaseCurrencyCode = "USD", QuoteCurrencyCode = "GBP", Rate = .5m, EffectiveDate = new DateOnly(2026, 1, 1) }); db.SaveChanges(); });
        var first = await client.PostAsync($"/api/payroll-runs/{Id(401)}/complete", null); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await client.PostAsync($"/api/payroll-runs/{Id(401)}/complete", null); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/payroll-runs/{Id(402)}/complete", null)).StatusCode);
        f.WithDb(db => { Assert.Single(db.OperationReceipts); Assert.Single(db.Invoices.Where(x => x.PayrollRunId == Id(401))); });
    }

    [Fact]
    public async Task SS04_ReceiptFailureRollsBackCompletionAndInvoices()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db => db.Database.ExecuteSqlRaw("CREATE TRIGGER fail_receipt BEFORE INSERT ON OperationReceipts BEGIN SELECT RAISE(ABORT, 'simulated receipt failure'); END;"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "rollback-test");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync($"/api/payroll-runs/{Id(401)}/complete", null)).StatusCode);
        f.WithDb(db => { Assert.Empty(db.OperationReceipts); Assert.Equal(PayrollRunStatus.Draft, db.PayrollRuns.Single(x => x.Id == Id(401)).Status); Assert.Empty(db.Invoices.Where(x => x.PayrollRunId == Id(401))); });
    }

    [Fact]
    public async Task SS05_ExpiredWorkerCannotCommitAfterAnotherWorkerClaims()
    {
        using var fixture = new JobDatabase(); var jobs = fixture.Jobs;
        var submitted = await jobs.SubmitPayrollAsync("PH", 2026, 9, Id(1));
        Assert.False((await jobs.SubmitPayrollAsync("PH", 2026, 9, Id(1))).Created);
        var a = (await jobs.ClaimAsync(submitted.Job.Id))!;
        var assessment = await fixture.Assessment();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var b = (await jobs.ClaimAsync(submitted.Job.Id))!;
        Assert.NotEqual(a.Token, b.Token);
        await Assert.ThrowsAsync<DomainException>(() => jobs.CommitPayrollAsync(a, assessment));
        await using (var check = fixture.Open()) { Assert.Empty(check.PayrollRuns); Assert.Equal(ExpenseClaimStatus.Approved, check.ExpenseClaims.Single().Status); }
        var runId = await jobs.CommitPayrollAsync(b, assessment);
        Assert.False(await jobs.RenewAsync(a.Id, a.Token));
        await using (var check = fixture.Open())
        {
            var saved = await check.BackgroundJobs.SingleAsync(); Assert.Equal(BackgroundJobState.Succeeded, saved.State); Assert.Equal(runId, saved.ResultRunId);
            Assert.Single(check.PayrollRuns); Assert.Single(check.Payslips); Assert.Single(check.PayslipCalculationEvidence);
            Assert.Equal(ExpenseClaimStatus.Reimbursed, check.ExpenseClaims.Single().Status);
        }
    }

    [Fact]
    public async Task SS02_SS05_EvidenceFailureRollsBackTheFencedCommit()
    {
        using var fixture = new JobDatabase();
        var submitted = await fixture.Jobs.SubmitPayrollAsync("PH", 2026, 9, Id(1)); var claim = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        var assessment = await fixture.Assessment();
        await using (var setup = fixture.Open()) await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_evidence BEFORE INSERT ON PayslipCalculationEvidence BEGIN SELECT RAISE(ABORT, 'simulated evidence failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Jobs.CommitPayrollAsync(claim, assessment));
        await using var check = fixture.Open();
        Assert.Empty(check.PayrollRuns); Assert.Empty(check.Payslips); Assert.Empty(check.PayslipCalculationEvidence);
        Assert.Equal(ExpenseClaimStatus.Approved, check.ExpenseClaims.Single().Status);
        Assert.Equal(BackgroundJobState.Running, check.BackgroundJobs.Single().State);
    }

    [Fact]
    public async Task SS05_ExpiredClaimsHaveABoundedCycleAndExplicitRetryRetainsTotalAttempts()
    {
        using var fixture = new JobDatabase(); var submitted = await fixture.Jobs.SubmitPayrollAsync("PH", 2026, 9, Id(1));
        for (var attempt = 0; attempt < 3; attempt++) { Assert.NotNull(await fixture.Jobs.ClaimAsync(submitted.Job.Id)); fixture.Clock.Advance(TimeSpan.FromSeconds(61)); }
        Assert.Null(await fixture.Jobs.ClaimAsync(submitted.Job.Id));
        await using (var edit = fixture.Open())
        {
            var job = await edit.BackgroundJobs.SingleAsync(); Assert.Equal(BackgroundJobState.Failed, job.State); Assert.Equal(3, job.TotalAttempts);
            job.Retry(fixture.Clock.GetUtcNow()); await edit.SaveChangesAsync();
        }
        Assert.NotNull(await fixture.Jobs.ClaimAsync(submitted.Job.Id));
        await using var check = fixture.Open(); Assert.Equal(4, check.BackgroundJobs.Single().TotalAttempts); Assert.Equal(1, check.BackgroundJobs.Single().CycleAttempts);
    }

    [Fact]
    public async Task SS05_DeferredInputSnapshotAllowsLeaseRenewalAndKeepsItsOriginalView()
    {
        using var fixture = new JobDatabase();
        var submitted = await fixture.Jobs.SubmitPayrollAsync("PH", 2026, 9, Id(1));
        var claim = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        int capturedVersion;
        await using (var reading = fixture.Open())
        {
            await using var snapshot = await SqliteReadSnapshot.BeginAsync(reading);
            capturedVersion = (await reading.BackgroundJobs.AsNoTracking().SingleAsync()).Version;
            Assert.True(await fixture.Jobs.RenewAsync(claim.Id, claim.Token));
            Assert.Equal(capturedVersion, (await reading.BackgroundJobs.AsNoTracking().SingleAsync()).Version);
        }
        await using var fresh = fixture.Open();
        Assert.True((await fresh.BackgroundJobs.AsNoTracking().SingleAsync()).Version > capturedVersion);
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    { var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}"); return JsonDocument.Parse(body).RootElement.Clone(); }

    internal sealed class MutableClock : TimeProvider
    {
        private long ticks = Now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }

    internal sealed class JobDatabase : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), $"atlas-bootcamp-job-{Guid.NewGuid():N}.db");
        private readonly DbContextOptions<AtlasDbContext> options;
        public MutableClock Clock { get; } = new();
        public DurableJobService Jobs { get; }
        public JobDatabase()
        {
            options = new DbContextOptionsBuilder<AtlasDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options;
            Jobs = new(options, Clock);
            using var db = Open(); db.Database.EnsureCreated(); db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.ApiUsers.Add(new ApiUser { Id = Id(1), Name = "Job operator", ApiKey = "job-test-only", Role = ApiRole.PlatformAdmin });
            db.Countries.Add(new Country { Code = "PH", Name = "Example", CurrencyCode = "USD", EmployerCostRate = .2m, EmployeeDeductionRate = .1m });
            db.Clients.Add(new Client { Id = Id(2), Name = "Example", LegalName = "Example", BillingEmail = "jobs@example.test", HeadquartersCountryCode = "PH", BillingCurrencyCode = "USD" });
            db.Workers.Add(new Worker { Id = Id(3), FullName = "Example Worker", Email = "worker@example.test", CountryCode = "PH" });
            db.Contracts.Add(new EmploymentContract { Id = Id(4), ClientId = Id(2), WorkerId = Id(3), CountryCode = "PH", CurrencyCode = "USD", MonthlySalary = 100m,
                JobTitle = "Engineer", StartDate = new DateOnly(2026, 1, 1), Status = ContractStatus.Active });
            var expense = new ExpenseClaim { ContractId = Id(4), CurrencyCode = "USD", Status = ExpenseClaimStatus.Approved };
            expense.Items.Add(new ExpenseItem { ExpenseClaimId = expense.Id, Description = "Supplies", Amount = 10m, IncurredDate = new DateOnly(2026, 8, 1) });
            db.ExpenseClaims.Add(expense); db.SaveChanges();
        }
        public AtlasDbContext Open() => new(options);
        public async Task<PayrollAssessment> Assessment()
        { await using var db = Open(); await using var snapshot = await db.Database.BeginTransactionAsync(); return await new PayrollAssessmentService(db, Clock).AssessAsync("PH", 2026, 9); }
        public void Dispose()
        { foreach (var ownedFile in new[] { file, file + "-wal", file + "-shm" }) if (File.Exists(ownedFile)) File.Delete(ownedFile); }
    }
}
