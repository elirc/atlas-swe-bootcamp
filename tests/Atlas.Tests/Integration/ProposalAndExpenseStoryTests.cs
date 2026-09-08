using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class ProposalAndExpenseStoryTests
{
    [Fact]
    public async Task MS19_MS20_ReplacementAndWithdrawalPreserveHistoryAndExcludePayroll()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClientWithApiKey("bootcamp-admin");
        var created = await Json(await client.PostAsJsonAsync("/api/expense-claims", ClaimBody(10m)), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid(); var path = $"/api/expense-claims/{id}";
        var originalItemId = created.GetProperty("items")[0].GetProperty("id").GetGuid();
        var replacement = new { expectedVersion = 0, description = "Corrected", items = new[] { Item(15m), Item(25m) } };
        var changed = await Json(await client.PutAsJsonAsync(path, replacement), HttpStatusCode.OK);
        Assert.Equal(40m, changed.GetProperty("totalAmount").GetDecimal()); Assert.Equal(1, changed.GetProperty("version").GetInt32());
        Assert.Equal(created.GetProperty("submittedAtUtc").GetDateTimeOffset(), changed.GetProperty("submittedAtUtc").GetDateTimeOffset());
        Assert.DoesNotContain(changed.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == originalItemId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, replacement)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { expectedVersion = 1, items = new[] { new { description = "Bad receipt", amount = 99, incurredDate = "2026-08-15", receiptUrl = "file:///private" } } })).StatusCode);
        Assert.Equal(40m, (await Get(client, path)).GetProperty("totalAmount").GetDecimal());
        var withdrawn = await Json(await client.PostAsJsonAsync(path + "/withdraw", new { expectedVersion = 1 }), HttpStatusCode.OK);
        Assert.Equal("Cancelled", withdrawn.GetProperty("status").GetString()); Assert.Equal(2, withdrawn.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + "/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/reject", new { note = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/withdraw", new { expectedVersion = 2 })).StatusCode);
        Assert.Single((await Get(client, "/api/expense-claims?status=Cancelled")).EnumerateArray());
        using var scope = f.Services.CreateScope(); var payroll = scope.ServiceProvider.GetRequiredService<PayrollService>();
        var run = await payroll.CreateRunAsync("PH", 2026, 9);
        Assert.All(run.Payslips, x => Assert.Equal(0m, x.Reimbursements));
    }

    [Fact]
    public async Task SS20_FirstVoteIsNotApprovalAndReviewedAmountsCannotChange()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db => { db.ApiUsers.Add(new ApiUser { Id = Id(950), Name = "Second reviewer", ApiKey = "second-reviewer", Role = ApiRole.ClientAdmin, ClientId = Id(101) }); db.SaveChanges(); });
        var submitter = f.CreateClientWithApiKey("bootcamp-admin"); var platform = f.CreateClient(); var second = f.CreateClientWithApiKey("second-reviewer");
        var policyPath = $"/api/clients/{Id(101)}/expense-approval-policies/USD";
        await Json(await submitter.PutAsJsonAsync(policyPath, new { threshold = 100m, expectedVersion = 0 }), HttpStatusCode.OK);
        var under = await Json(await submitter.PostAsJsonAsync("/api/expense-claims", ClaimBody(99.99m)), HttpStatusCode.Created);
        Assert.Equal(1, under.GetProperty("requiredApprovals").GetInt32());
        var claim = await Json(await submitter.PostAsJsonAsync("/api/expense-claims", ClaimBody(100m)), HttpStatusCode.Created);
        Assert.Equal(2, claim.GetProperty("requiredApprovals").GetInt32());
        var path = $"/api/expense-claims/{claim.GetProperty("id").GetGuid()}";
        await Json(await submitter.PutAsJsonAsync(policyPath, new { threshold = 200m, expectedVersion = 1 }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await submitter.PostAsync(path + "/approve", null)).StatusCode);
        var first = await Json(await platform.PostAsync(path + "/approve", null), HttpStatusCode.OK);
        Assert.Equal("Pending", first.GetProperty("status").GetString()); Assert.Equal(1, first.GetProperty("recordedApprovalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("requiredApprovals").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await platform.PostAsync(path + "/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await submitter.PutAsJsonAsync(path, new { expectedVersion = first.GetProperty("version").GetInt32(), items = new[] { Item(1m) } })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<PayrollService>().CreateRunAsync("PH", 2026, 9);
            Assert.All(run.Payslips, x => Assert.Equal(0m, x.Reimbursements));
        }
        var final = await Json(await second.PostAsync(path + "/approve", null), HttpStatusCode.OK);
        Assert.Equal("Approved", final.GetProperty("status").GetString()); Assert.Equal(2, final.GetProperty("recordedApprovalCount").GetInt32());
        using (var scope = f.Services.CreateScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<PayrollService>().CreateRunAsync("PH", 2026, 10);
            Assert.Equal(100m, run.Payslips.Single(x => x.ContractId == Id(301)).Reimbursements);
        }
    }

    [Fact]
    public async Task SS20_FinalApprovalLosingToRejectionCannotLeaveAnExtraVote()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        f.WithDb(db => { var claim = new ExpenseClaim { Id = Id(960), ContractId = Id(301), CurrencyCode = "USD", RequiredApprovals = 2, SubmittedByApiUserId = Id(901) };
            claim.Items.Add(new ExpenseItem { ExpenseClaimId = claim.Id, Description = "Equipment", Amount = 100, IncurredDate = new DateOnly(2026, 8, 1) });
            claim.Approvals.Add(new ExpenseApproval { ExpenseClaimId = claim.Id, ApproverApiUserId = Id(902), ApprovedAtUtc = Now }); db.ExpenseClaims.Add(claim); db.SaveChanges(); });
        using var scope = f.Services.CreateScope(); var stale = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        var claim = await stale.ExpenseClaims.Include(x => x.Items).Include(x => x.Approvals).SingleAsync(x => x.Id == Id(960));
        f.WithDb(db => { db.ExpenseClaims.Single(x => x.Id == Id(960)).Reject("Rejected before final vote", Now); db.SaveChanges(); });
        var actor = await stale.ApiUsers.Where(x => x.ApiKey == AtlasApiFactory.AdminApiKey).Select(x => x.Id).SingleAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => scope.ServiceProvider.GetRequiredService<ExpenseApprovalService>().ApproveAsync(claim, actor, null));
        f.WithDb(db => { Assert.Equal(ExpenseClaimStatus.Rejected, db.ExpenseClaims.Single(x => x.Id == Id(960)).Status); Assert.Single(db.ExpenseApprovals); });
    }

    [Fact]
    public async Task MS24_PreviewMatchesBaseComponentsOfLaterFinalPayroll()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClientWithApiKey("bootcamp-admin");
        f.WithDb(db => { db.Countries.Single(x => x.Code == "PH").MinimumNoticeDays = 0;
            db.LeavePolicies.Add(new LeavePolicy { CountryCode = "PH", AnnualLeaveDays = 2, SickLeaveDays = 0 });
            db.SalaryRecords.Single(x => x.Id == Id(603)).MonthlySalary = 3000m;
            db.SalaryRecords.Add(new SalaryRecord { ContractId = Id(301), MonthlySalary = 9000m, JobTitle = "Future", EffectiveDate = new DateOnly(2026, 10, 1) }); db.SaveChanges(); });
        var version = 0; f.WithDb(db => version = db.Contracts.Single(x => x.Id == Id(301)).Version);
        var preview = await Json(await client.PostAsJsonAsync($"/api/contracts/{Id(301)}/termination-preview", new { proposedEndDate = "2026-09-15" }), HttpStatusCode.OK);
        Assert.Equal(3000m, preview.GetProperty("selectedMonthlySalary").GetDecimal());
        Assert.Equal(1500m, preview.GetProperty("proratedBaseSalary").GetDecimal()); Assert.Equal(138.46m, preview.GetProperty("dailyRate").GetDecimal());
        Assert.Equal(276.92m, preview.GetProperty("unusedLeavePayout").GetDecimal()); Assert.Equal(1776.92m, preview.GetProperty("estimatedBaseTotal").GetDecimal());
        f.WithDb(db => { Assert.Equal(version, db.Contracts.Single(x => x.Id == Id(301)).Version); Assert.Empty(db.TerminationRequests); });
        f.WithDb(db => { db.Contracts.Single(x => x.Id == Id(301)).Terminate(new DateOnly(2026, 9, 15), "Example", Now); db.SaveChanges(); });
        using var scope = f.Services.CreateScope(); var run = await scope.ServiceProvider.GetRequiredService<PayrollService>().CreateRunAsync("PH", 2026, 9);
        var slip = run.Payslips.Single(x => x.ContractId == Id(301));
        Assert.Equal(preview.GetProperty("estimatedBaseTotal").GetDecimal(), slip.GrossSalary);
        Assert.Equal(preview.GetProperty("unusedLeavePayout").GetDecimal(), slip.UnusedLeavePayout);
    }

    [Fact]
    public async Task MS25_PreviewUsesCurrentTermsAndCreationRechecksPendingProposal()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClientWithApiKey("bootcamp-admin"); var path = $"/api/contracts/{Id(301)}/amendment-preview";
        var body = new { newMonthlySalary = 110m, effectiveDate = "2026-10-01" };
        var preview = await Json(await client.PostAsJsonAsync(path, body), HttpStatusCode.OK);
        Assert.Equal(100m, preview.GetProperty("currentMonthlySalary").GetDecimal());
        Assert.Equal(10m, preview.GetProperty("monthlySalaryDelta").GetDecimal()); Assert.Equal(10m, preview.GetProperty("percentageDelta").GetDecimal());
        f.WithDb(db => { Assert.Empty(db.ContractAmendments); Assert.Equal(100m, db.Contracts.Single(x => x.Id == Id(301)).MonthlySalary); Assert.Equal(3, db.SalaryRecords.Count()); });
        await Json(await client.PostAsJsonAsync("/api/contract-amendments", new { contractId = Id(301), body.newMonthlySalary, body.effectiveDate }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/contract-amendments", new { contractId = Id(301), body.newMonthlySalary, body.effectiveDate })).StatusCode);
    }

    [Fact]
    public async Task MS28_PresetsAreTypedOwnedVersionedAndMatchDirectReports()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        var body = new { name = " Monthly ", kind = "headcount", filters = new { asOf = (string?)null, countryCode = "PH" } };
        var preset = await Json(await admin.PostAsJsonAsync("/api/report-presets", body), HttpStatusCode.Created);
        var id = preset.GetProperty("id").GetGuid(); var path = $"/api/report-presets/{id}";
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/report-presets", new { name = "monthly", body.kind, body.filters })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/report-presets", new { name = "Bad", kind = "headcount", filters = new { sql = "SELECT *" } })).StatusCode);
        var run = await Json(await admin.PostAsync(path + "/run", null), HttpStatusCode.OK);
        Assert.Equal("2026-09-01", run.GetProperty("parameters").GetProperty("asOf").GetString());
        var direct = await Get(admin, "/api/reports/headcount?asOf=2026-09-01&countryCode=PH");
        Assert.Equal(direct.GetRawText(), run.GetProperty("result").GetRawText());
        Assert.Equal(1, (await Get(admin, path)).GetProperty("version").GetInt32());
        f.WithDb(db => { db.ApiUsers.Add(new ApiUser { Name = "Other platform admin", ApiKey = "other-platform", Role = ApiRole.PlatformAdmin }); db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClientWithApiKey("other-platform").GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-admin").GetAsync(path)).StatusCode);
        await Json(await admin.PutAsJsonAsync(path, new { name = "Renamed", body.kind, body.filters, expectedVersion = 1 }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(path + "?expectedVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(path + "?expectedVersion=2")).StatusCode);
    }

    private static object Item(decimal amount) => new { description = "Equipment", amount, incurredDate = "2026-08-15", receiptUrl = (string?)null };
    private static object ClaimBody(decimal amount) => new { contractId = Id(301), description = "Work equipment", items = new[] { Item(amount) } };
    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
