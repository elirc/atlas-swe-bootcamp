using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Atlas.Domain;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Atlas.Tests.Integration.BootcampApiFactory;

namespace Atlas.Tests.Integration;

public class ExportStoryTests
{
    [Fact]
    public async Task SS18_ExportCopiesAllowlistedClientDataAndFencesArtifactCommit()
    {
        using var fixture = new PayrollReliabilityStoryTests.JobDatabase();
        await using (var setup = fixture.Open())
        {
            setup.Clients.Add(new Client { Id = Id(5), Name = "Other client", LegalName = "Other client", BillingEmail = "other@example.test", HeadquartersCountryCode = "PH", BillingCurrencyCode = "USD" });
            setup.Contracts.Add(new EmploymentContract { ClientId = Id(5), WorkerId = Id(3), CountryCode = "PH", CurrencyCode = "USD", MonthlySalary = 999m,
                JobTitle = "Foreign captured title", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Status = ContractStatus.Terminated });
            setup.Workers.Add(new Worker { FullName = "Unrelated worker", Email = "unrelated@example.test", CountryCode = "PH" });
            setup.Workers.Single(x => x.Id == Id(3)).DateOfBirth = new DateOnly(1990, 1, 1);
            await setup.SaveChangesAsync();
        }
        var payload = new ClientExportPayload(Id(2), Id(1), 2026, 1, 2026, 12);
        var submitted = await fixture.Jobs.SubmitExportAsync(payload, "review");
        Assert.False((await fixture.Jobs.SubmitExportAsync(payload, "review")).Created);
        await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.SubmitExportAsync(payload with { ToMonth = 11 }, "review"));
        var first = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        var export = await fixture.Jobs.PrepareExportAsync(first);
        using var json = JsonDocument.Parse(export.Bytes); var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32()); Assert.Single(root.GetProperty("contracts").EnumerateArray());
        Assert.Single(root.GetProperty("workers").EnumerateArray()); Assert.DoesNotContain("Foreign captured title", root.GetRawText());
        Assert.Equal(new[] { "id", "fullName", "email" }, root.GetProperty("workers")[0].EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("job-test-only", root.GetRawText()); Assert.False(root.TryGetProperty("expenseClaims", out _));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(export.Bytes)).ToLowerInvariant(), export.Checksum);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); var second = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.CommitExportAsync(first, export));
        await using (var check = fixture.Open()) Assert.Empty(check.ExportArtifacts);
        await fixture.Jobs.CommitExportAsync(second, export);
        await using (var check = fixture.Open()) { Assert.Single(check.ExportArtifacts); Assert.Equal(BackgroundJobState.Succeeded, check.BackgroundJobs.Single().State); }
        fixture.Clock.Advance(TimeSpan.FromHours(24)); Assert.Equal(1, await fixture.Jobs.ClearExpiredExportsAsync());
        await using (var check = fixture.Open()) { var saved = check.ExportArtifacts.Single(); Assert.Null(saved.Bytes); Assert.Equal(export.Checksum, saved.Checksum); }
    }

    [Fact]
    public async Task SS18_RevocationAndArtifactFailureProduceNoSuccessfulArtifact()
    {
        using var fixture = new PayrollReliabilityStoryTests.JobDatabase();
        var submitted = await fixture.Jobs.SubmitExportAsync(new(Id(2), Id(1), 2026, 1, 2026, 12), "failure");
        var claim = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!; var export = await fixture.Jobs.PrepareExportAsync(claim);
        await using (var edit = fixture.Open()) await edit.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_artifact BEFORE INSERT ON ExportArtifacts BEGIN SELECT RAISE(ABORT, 'simulated artifact failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Jobs.CommitExportAsync(claim, export));
        await using (var edit = fixture.Open()) { Assert.Empty(edit.ExportArtifacts); Assert.Equal(BackgroundJobState.Running, edit.BackgroundJobs.Single().State);
            await edit.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_artifact;"); edit.ApiUsers.Single().IsActive = false; await edit.SaveChangesAsync(); }
        Assert.Equal("export-access-revoked", (await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.PrepareExportAsync(claim))).Message);
        Assert.Equal("export-access-revoked", (await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.CommitExportAsync(claim, export))).Message);
        await using var check = fixture.Open(); Assert.Empty(check.ExportArtifacts);
    }

    [Fact]
    public async Task SS18_DispatcherCompletesExportsAndReportsRevokedRequests()
    {
        using var fixture = new PayrollReliabilityStoryTests.JobDatabase();
        var first = await fixture.Jobs.SubmitExportAsync(new(Id(2), Id(1), 2026, 1, 2026, 12), "first");
        Assert.True(await fixture.Jobs.ProcessOneAsync());
        await using (var check = fixture.Open()) Assert.Equal(BackgroundJobState.Succeeded, check.BackgroundJobs.Single(x => x.Id == first.Job.Id).State);
        var second = await fixture.Jobs.SubmitExportAsync(new(Id(2), Id(1), 2026, 1, 2026, 12), "second");
        await using (var edit = fixture.Open()) { edit.ApiUsers.Single().IsActive = false; await edit.SaveChangesAsync(); }
        Assert.True(await fixture.Jobs.ProcessOneAsync());
        await using var final = fixture.Open(); Assert.Equal("export-access-revoked", final.BackgroundJobs.Single(x => x.Id == second.Job.Id).ErrorCode); Assert.Single(final.ExportArtifacts);
    }

    [Fact]
    public async Task SS18_PollAndDownloadAreOwnerScopedAndEnforceExactExpiry()
    {
        using var f = new BootcampApiFactory(); f.Seed(); var manager = f.CreateClientWithApiKey("bootcamp-admin"); var prefix = $"/api/clients/{Id(101)}/data-exports";
        var request = new { fromPeriod = "2026-07", toPeriod = "2026-09", reference = " quarterly " };
        var response = await manager.PostAsJsonAsync(prefix, request); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement; var id = json.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(prefix, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(prefix, new { fromPeriod = "2026-06", toPeriod = "2026-09", reference = "quarterly" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync(prefix + $"/{id}")).StatusCode);
        f.WithDb(db => { db.ExportArtifacts.Add(new ExportArtifact { JobId = id, Bytes = "{}"u8.ToArray(), Checksum = "fixture-checksum", GeneratedAtUtc = Now.AddHours(-24), ExpiresAtUtc = Now }); db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Gone, (await manager.GetAsync(prefix + $"/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await manager.GetAsync(prefix + $"/{id}")).StatusCode);
        f.WithDb(db => { db.ExportArtifacts.Single().Bytes = null; db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Gone, (await manager.GetAsync(prefix + $"/{id}/download")).StatusCode);
    }

    [Fact]
    public async Task SS18_RowLimitCountsAcrossCollectionsAndLeavesNoArtifact()
    {
        using var fixture = new PayrollReliabilityStoryTests.JobDatabase();
        await using (var edit = fixture.Open())
        {
            // 5,000 contracts plus their one referenced worker exceeds the total-row cap.
            edit.Contracts.AddRange(Enumerable.Range(0, 4999).Select(_ => new EmploymentContract { ClientId = Id(2), WorkerId = Id(3), CountryCode = "PH",
                CurrencyCode = "USD", JobTitle = "Historical contract", MonthlySalary = 100m, StartDate = new DateOnly(2025, 1, 1),
                EndDate = new DateOnly(2025, 12, 31), Status = ContractStatus.Terminated }));
            await edit.SaveChangesAsync();
        }
        var submitted = await fixture.Jobs.SubmitExportAsync(new(Id(2), Id(1), 2026, 1, 2026, 12), "row-cap");
        var claim = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        Assert.Equal("export-row-limit", (await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.PrepareExportAsync(claim))).Message);
        await using var check = fixture.Open(); Assert.Empty(check.ExportArtifacts);
    }

    [Fact]
    public async Task SS18_SerializedByteLimitIsIndependentFromRowCount()
    {
        using var fixture = new PayrollReliabilityStoryTests.JobDatabase();
        await using (var edit = fixture.Open()) { edit.Contracts.Single().JobTitle = new string('x', 5 * 1024 * 1024); await edit.SaveChangesAsync(); }
        var submitted = await fixture.Jobs.SubmitExportAsync(new(Id(2), Id(1), 2026, 1, 2026, 12), "byte-cap");
        var claim = (await fixture.Jobs.ClaimAsync(submitted.Job.Id))!;
        Assert.Equal("export-byte-limit", (await Assert.ThrowsAsync<DomainException>(() => fixture.Jobs.PrepareExportAsync(claim))).Message);
        await using var check = fixture.Open(); Assert.Empty(check.ExportArtifacts);
    }
}
