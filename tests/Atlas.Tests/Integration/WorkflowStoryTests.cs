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

public class WorkflowStoryTests
{
    [Fact]
    public async Task MS04_BatchValidatesEveryMemberAndReturnsTheWholeChecklist()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        var path = $"/api/contracts/{Id(301)}/onboarding";
        var checklist = await Get(client, path);
        var ids = checklist.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        var invalid = new { items = new[] { new { itemId = ids[0], expectedVersion = 1, notes = "reviewed" }, new { itemId = Id(999), expectedVersion = 1, notes = "foreign" } } };
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path + "/complete-batch", invalid)).StatusCode);
        f.WithDb(db => Assert.DoesNotContain(db.OnboardingItems, x => x.IsCompleted));
        var valid = new { items = ids.Take(2).Select(id => new { itemId = id, expectedVersion = 1, notes = " reviewed " }).ToList() };
        var result = await Json(await client.PostAsJsonAsync(path + "/complete-batch", valid), HttpStatusCode.OK);
        Assert.Equal(5, result.GetProperty("items").GetArrayLength());
        var completed = result.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("isCompleted").GetBoolean()).ToList();
        Assert.Equal(2, completed.Count);
        Assert.All(completed, x => { Assert.Equal(Now, x.GetProperty("completedAtUtc").GetDateTimeOffset()); Assert.Equal(2, x.GetProperty("version").GetInt32()); });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/complete-batch", valid)).StatusCode);
    }

    [Fact]
    public async Task MS04_StaleBatchRollsBackItsOtherItem()
    {
        using var f = new BootcampApiFactory(); f.Seed();
        using var scope = f.Services.CreateScope(); var stale = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        var items = await stale.OnboardingItems.OrderBy(x => x.Type).Take(2).ToListAsync();
        f.WithDb(db => { db.OnboardingItems.Single(x => x.Id == items[0].Id).Complete(Now, "winner"); db.SaveChanges(); });
        foreach (var item in items) item.Complete(Now, "losing batch");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        f.WithDb(db => { Assert.Equal("winner", db.OnboardingItems.Single(x => x.Id == items[0].Id).Notes); Assert.False(db.OnboardingItems.Single(x => x.Id == items[1].Id).IsCompleted); });
    }

    [Fact]
    public async Task MS05_NotesAreAuthoredByTheCallerAndDoNotChangeContractVersion()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClientWithApiKey("bootcamp-admin");
        var path = $"/api/contracts/{Id(301)}/notes"; var version = 0;
        f.WithDb(db => version = db.Contracts.Single(x => x.Id == Id(301)).Version);
        var response = await client.PostAsJsonAsync(path, new { body = " Context for the next engineer ", authorApiUserId = Id(999) });
        var note = await Json(response, HttpStatusCode.Created);
        Assert.Equal(Id(902), note.GetProperty("authorApiUserId").GetGuid());
        Assert.Equal(Now, note.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal("Context for the next engineer", note.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/contracts/{Id(303)}/notes/{note.GetProperty("id").GetGuid()}")).StatusCode);
        var viewer = f.CreateClientWithApiKey("bootcamp-viewer");
        Assert.Single((await Get(viewer, path)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(path, new { body = "blocked" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { body = new string('n', 2001) })).StatusCode);
        f.WithDb(db => Assert.Equal(version, db.Contracts.Single(x => x.Id == Id(301)).Version));
    }

    [Fact]
    public async Task MS06_TemplatesAreCopiedAndStaleReplacementsFail()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        var path = "/api/countries/PH/onboarding-template";
        Assert.False((await Get(client, path)).GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-viewer").GetAsync(path)).StatusCode);
        var first = new { expectedVersion = 0, items = new[] { new { type = "IdentityDocument", title = "Original identity check", isRequired = true } } };
        await Json(await client.PutAsJsonAsync(path, first), HttpStatusCode.OK);
        f.WithDb(db => { db.Workers.Add(new Worker { Id = Id(211), FullName = "New worker", Email = "newworker@example.test", CountryCode = "PH" }); db.SaveChanges(); });
        var created = await Json(await client.PostAsJsonAsync("/api/contracts", new { clientId = Id(101), workerId = Id(211), jobTitle = "Engineer", monthlySalary = 100, startDate = "2026-09-01" }), HttpStatusCode.Created);
        var second = new { expectedVersion = 1, items = new[] { new { type = "IdentityDocument", title = "Updated identity check", isRequired = true } } };
        await Json(await client.PutAsJsonAsync(path, second), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, second)).StatusCode);
        var checklist = await Get(client, $"/api/contracts/{created.GetProperty("id").GetGuid()}/onboarding");
        var item = Assert.Single(checklist.GetProperty("items").EnumerateArray());
        Assert.Equal("Original identity check", item.GetProperty("title").GetString());
        Assert.False(item.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { expectedVersion = 2, items = new[] { new { type = "999", title = "Bad", isRequired = true } } })).StatusCode);
    }

    [Fact]
    public async Task MS30_PausingHiringBlocksActivationButAllowsExistingPayroll()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db => { var c = db.Contracts.Single(x => x.Id == Id(301)); c.Status = ContractStatus.Draft; foreach (var i in db.OnboardingItems) i.Complete(Now); db.SaveChanges(); });
        var country = await Get(client, "/api/countries/PH");
        var paused = await Json(await client.PutAsJsonAsync("/api/countries/PH/hiring-status", new { isActive = false, expectedVersion = country.GetProperty("version").GetInt32() }), HttpStatusCode.OK);
        var noOp = await Json(await client.PutAsJsonAsync("/api/countries/PH/hiring-status", new { isActive = false, expectedVersion = paused.GetProperty("version").GetInt32() }), HttpStatusCode.OK);
        Assert.Equal(paused.GetProperty("version").GetInt32(), noOp.GetProperty("version").GetInt32());
        Assert.False((await Get(client, $"/api/contracts/{Id(301)}/activation-readiness")).GetProperty("canActivate").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/contracts/{Id(301)}/activate", null)).StatusCode);
        await Json(await client.PostAsJsonAsync("/api/payroll-runs", new { countryCode = "PH", year = 2026, month = 9 }), HttpStatusCode.Created);
    }

    [Fact]
    public async Task MS26_MS27_ExpirationAndReplacementDoNotExtendScopeOrRevealOldSecrets()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var admin = f.CreateClient();
        f.WithDb(db => { db.ApiUsers.Add(new ApiUser { Id = Id(911), Name = "Expired", ApiKey = "expired", Role = ApiRole.ClientViewer, ClientId = Id(101), ExpiresAtUtc = Now }); db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClientWithApiKey("expired").GetAsync("/api/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/api-users", new { name = "Too long", role = "PlatformAdmin", expiresAtUtc = Now.AddDays(366) })).StatusCode);
        var created = await Json(await admin.PostAsJsonAsync("/api/api-users", new { name = "Temporary", role = "ClientViewer", clientId = Id(101), expiresAtUtc = Now.AddDays(1) }), HttpStatusCode.Created);
        var originalKey = created.GetProperty("apiKey").GetString()!; var id = created.GetProperty("id").GetGuid();
        var replaced = await Json(await admin.PostAsync($"/api/api-users/{id}/replace", null), HttpStatusCode.Created);
        Assert.Equal(created.GetProperty("expiresAtUtc").GetDateTimeOffset(), replaced.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(Id(101), replaced.GetProperty("clientId").GetGuid());
        Assert.Equal("ClientViewer", replaced.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClientWithApiKey(originalKey).GetAsync("/api/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClientWithApiKey(replaced.GetProperty("apiKey").GetString()).GetAsync("/api/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/api-users/{id}/replace", null)).StatusCode);
        var listed = await Get(admin, "/api/api-users");
        Assert.DoesNotContain(originalKey, listed.GetRawText()); Assert.DoesNotContain(replaced.GetProperty("apiKey").GetString()!, listed.GetRawText());
    }

    [Fact]
    public async Task MS11_MS12_MS13_SS06_LedgersReplayAndKeepBilledDistinctFromOutstanding()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient(); var path = $"/api/invoices/{Id(801)}";
        var paymentBody = new { amount = 40m, currencyCode = " usd ", paidAtUtc = Now.AddDays(-5), reference = " receipt-1 " };
        var payment = await Json(await client.PostAsJsonAsync(path + "/payments", paymentBody), HttpStatusCode.Created);
        Assert.Equal(payment.GetProperty("id").GetGuid(), (await Json(await client.PostAsJsonAsync(path + "/payments", paymentBody), HttpStatusCode.OK)).GetProperty("id").GetGuid());
        Assert.Equal(92m, (await Get(client, path + "/balance")).GetProperty("outstanding").GetDecimal());
        Assert.Equal(132m, (await Get(client, path + "/balance?asOf=2026-08-26")).GetProperty("outstanding").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/payments", new { amount = 93, currencyCode = "USD", paidAtUtc = Now, reference = "overpay" })).StatusCode);
        var creditBody = new { amount = 12m, currencyCode = "USD", reference = "credit-1", reason = "Price correction" };
        await Json(await client.PostAsJsonAsync(path + "/credit-notes", creditBody), HttpStatusCode.Created);
        await Json(await client.PostAsJsonAsync(path + "/credit-notes", creditBody), HttpStatusCode.OK);
        Assert.Equal(120m, (await Get(client, path + "/credit-summary")).GetProperty("netBilledAmount").GetDecimal());
        Assert.Equal(80m, (await Get(client, path + "/balance")).GetProperty("outstanding").GetDecimal());
        var reversePath = path + $"/payments/{payment.GetProperty("id").GetGuid()}/reverse";
        var reversalBody = new { reference = "reversal-1", reason = "Wrong receipt" };
        await Json(await client.PostAsJsonAsync(reversePath, reversalBody), HttpStatusCode.Created);
        await Json(await client.PostAsJsonAsync(reversePath, reversalBody), HttpStatusCode.OK);
        Assert.Equal(120m, (await Get(client, path + "/balance")).GetProperty("outstanding").GetDecimal());
        Assert.Equal(92m, (await Get(client, path + "/balance?asOf=2026-08-31")).GetProperty("outstanding").GetDecimal());
        var read = await Get(f.CreateClientWithApiKey("bootcamp-viewer"), path + "/payments");
        Assert.Equal(0m, read[0].GetProperty("effectiveNetAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClientWithApiKey("bootcamp-viewer").PostAsJsonAsync(path + "/payments", paymentBody)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClientWithApiKey("bootcamp-viewer").GetAsync($"/api/invoices/{Id(802)}/balance")).StatusCode);
        f.WithDb(db => { Assert.Equal(132m, db.Invoices.Single(x => x.Id == Id(801)).TotalInBillingCurrency); Assert.Single(db.InvoicePayments); Assert.Single(db.InvoiceCreditNotes); Assert.Single(db.InvoicePaymentReversals); });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    public async Task MS11_InvalidPaymentAmountsAreTransportErrors(string value)
    {
        using var f = new BootcampApiFactory(); f.Seed();
        var result = await f.CreateClient().PostAsJsonAsync($"/api/invoices/{Id(801)}/payments", new { amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), currencyCode = "USD", paidAtUtc = Now, reference = "bad" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public void MS11_BackdatedPaymentCannotOverdrawAnEarlierBalance()
    {
        var movements = new List<LedgerMovement> { new(Now.AddDays(-9), -100m), new(Now, 100m) };
        Assert.Equal(100m, InvoiceLedger.Outstanding(100m, movements, Now));
        Assert.Throws<DomainException>(() => InvoiceLedger.ValidateHistory(100m, movements.Append(new(Now.AddDays(-9), -100m))));
        InvoiceLedger.ValidateHistory(100m, movements.Append(new(Now, -100m)));
    }

    [Fact]
    public async Task SS07_SS08_IssuanceSnapshotsTermsAndSelectsFeesByRunPeriod()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient(); var prefix = $"/api/clients/{Id(101)}";
        await Json(await client.PostAsJsonAsync(prefix + "/billing-terms", new { effectiveDate = "2026-09-01", termDays = 45, reason = "Agreed" }), HttpStatusCode.Created);
        await Json(await client.PostAsJsonAsync(prefix + "/fee-schedule", new { effectiveYear = 2026, effectiveMonth = 9, rate = .25m, reason = "September terms" }), HttpStatusCode.Created);
        var selected = await Get(client, prefix + "/fee-schedule/effective?year=2026&month=6");
        Assert.True(selected.GetProperty("isFallback").GetBoolean());
        using var scope = f.Services.CreateScope();
        var payroll = scope.ServiceProvider.GetRequiredService<PayrollService>();
        var result = await payroll.CompleteRunAsync(Id(401));
        var invoice = Assert.Single(result.Invoices);
        Assert.Equal(45, invoice.PaymentTermDays); Assert.Equal(new DateOnly(2026, 10, 16), invoice.DueDate);
        Assert.Null(invoice.FeeScheduleId); Assert.NotNull(invoice.ManagementFeeRateApplied);
        Assert.Equal(Now, invoice.IssuedAtUtc);
        var due = await Get(client, $"/api/invoices/{invoice.Id}/due-status?asOf=2026-10-16");
        Assert.Equal("due-today", due.GetProperty("status").GetString());
        Assert.Equal("unknown-terms", (await Get(client, $"/api/invoices/{Id(801)}/due-status")).GetProperty("status").GetString());
        var future = await Get(client, prefix + "/fee-schedule/effective?year=2026&month=9");
        Assert.Equal(.25m, future.GetProperty("rate").GetDecimal());
    }

    [Fact]
    public async Task MS15_MS17_MS18_QuoteAndCreationShareAdjustedAllowanceAndHolidayDays()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db => { db.LeavePolicies.Add(new LeavePolicy { CountryCode = "PH", AnnualLeaveDays = 5, SickLeaveDays = 5 }); db.SaveChanges(); });
        var path = $"/api/contracts/{Id(301)}";
        var proposed = new { type = "Annual", startDate = "2026-09-07", endDate = "2026-09-11" };
        var first = await Json(await client.PostAsJsonAsync(path + "/leave-quote", proposed), HttpStatusCode.OK);
        Assert.Equal(5, first.GetProperty("requestedWorkingDays").GetInt32());
        await Json(await client.PostAsJsonAsync("/api/countries/PH/holidays", new { date = "2026-09-07", name = "Example holiday" }), HttpStatusCode.Created);
        var adjustment = new { year = 2026, type = "Annual", deltaDays = 2, reference = "extra", reason = "Agreed entitlement" };
        await Json(await client.PostAsJsonAsync(path + "/leave-adjustments", adjustment), HttpStatusCode.Created);
        await Json(await client.PostAsJsonAsync(path + "/leave-adjustments", adjustment), HttpStatusCode.OK);
        var quote = await Json(await client.PostAsJsonAsync(path + "/leave-quote", proposed), HttpStatusCode.OK);
        Assert.True(quote.GetProperty("eligible").GetBoolean()); Assert.Equal(7, quote.GetProperty("allowanceDays").GetInt32());
        Assert.Equal(4, quote.GetProperty("requestedWorkingDays").GetInt32());
        var created = await Json(await client.PostAsJsonAsync("/api/leave-requests", new { contractId = Id(301), proposed.type, proposed.startDate, proposed.endDate }), HttpStatusCode.Created);
        Assert.Equal(4, created.GetProperty("days").GetInt32());
        var after = await Json(await client.PostAsJsonAsync(path + "/leave-quote", proposed), HttpStatusCode.OK);
        Assert.False(after.GetProperty("eligible").GetBoolean());
        Assert.Contains(after.GetProperty("blockers").EnumerateArray(), x => x.GetProperty("code").GetString() == "overlap");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/leave-adjustments", new { year = 2026, type = "Annual", deltaDays = -4, reference = "too-much", reason = "Invalid reduction" })).StatusCode);
        await Json(await client.PostAsJsonAsync("/api/countries/PH/holidays", new { date = "2026-09-08", name = "Later calendar change" }), HttpStatusCode.Created);
        Assert.Equal(4, (await Get(client, $"/api/leave-requests/{created.GetProperty("id").GetGuid()}")).GetProperty("days").GetInt32());
        f.WithDb(db => { Assert.Single(db.LeaveRequests); Assert.Single(db.LeaveAllowanceAdjustments); });
    }

    [Fact]
    public async Task MS16_ApprovedFutureCancellationReleasesSavedDaysAndRejectsFirstDay()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var client = f.CreateClient();
        f.WithDb(db =>
        {
            db.LeavePolicies.Add(new LeavePolicy { CountryCode = "PH", AnnualLeaveDays = 10, SickLeaveDays = 5 });
            for (var i = 0; i < 2; i++) db.LeaveRequests.Add(new LeaveRequest { Id = Id(920 + i), ContractId = Id(301), Type = LeaveType.Annual,
                StartDate = new DateOnly(2026, 9, 1 + i), EndDate = new DateOnly(2026, 9, 1 + i), Days = 1, Status = LeaveRequestStatus.Approved, DecisionNote = "Keep this note" });
            db.SaveChanges();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/leave-requests/{Id(920)}/cancel-approved", new { expectedVersion = 0 })).StatusCode);
        var cancelled = await Json(await client.PostAsJsonAsync($"/api/leave-requests/{Id(921)}/cancel-approved", new { expectedVersion = 0 }), HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString()); Assert.Equal(1, cancelled.GetProperty("days").GetInt32());
        Assert.Equal("Keep this note", cancelled.GetProperty("decisionNote").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/leave-requests/{Id(921)}/cancel-approved", new { expectedVersion = 0 })).StatusCode);
        var balances = await Get(client, $"/api/contracts/{Id(301)}/leave-balances?year=2026");
        Assert.Equal(9, balances.GetProperty("balances").EnumerateArray().Single(x => x.GetProperty("type").GetString() == "Annual").GetProperty("remainingDays").GetInt32());
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
