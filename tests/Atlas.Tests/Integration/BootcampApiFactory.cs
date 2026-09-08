using Atlas.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atlas.Tests.Integration;

/// <summary>A fixed clock and deliberately small dataset for story acceptance tests.</summary>
public class BootcampApiFactory : AtlasApiFactory
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    public static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        });
    }

    public void Seed()
    {
        WithDb(db =>
        {
            db.Countries.AddRange(
                new Country { Code = "PH", Name = "Philippines", CurrencyCode = "USD", EmployerCostRate = .2m, EmployeeDeductionRate = .1m },
                new Country { Code = "US", Name = "United States", CurrencyCode = "EUR", EmployerCostRate = .2m, EmployeeDeductionRate = .1m, IsActive = false });
            string[] clientNames = ["Acme Robotics", "AC Tools", "Beta Acme", "%_ Tools"];
            string[] workerNames = ["Sam Lee", "SARA Jones", "Alex Sam", "%_ Person"];
            for (var i = 1; i <= 4; i++)
            {
                db.Clients.Add(new Client
                {
                    Id = Id(100 + i), Name = clientNames[i - 1], LegalName = clientNames[i - 1],
                    BillingEmail = $"client{i}@example.test", HeadquartersCountryCode = "PH", BillingCurrencyCode = "USD",
                });
                db.Workers.Add(new Worker
                {
                    Id = Id(200 + i), FullName = workerNames[i - 1], Email = $"worker{i}@example.test",
                    CountryCode = i == 2 ? "US" : "PH",
                });
                db.Contracts.Add(new EmploymentContract
                {
                    Id = Id(300 + i), ClientId = Id(i == 3 ? 101 : 100 + i), WorkerId = Id(200 + i),
                    CountryCode = i == 2 ? "US" : "PH", CurrencyCode = i == 2 ? "EUR" : "USD",
                    JobTitle = "Engineer", MonthlySalary = 100m, StartDate = new DateOnly(2026, 1, 1),
                    Status = i == 4 ? ContractStatus.Draft : ContractStatus.Active,
                });
            }
            db.ApiUsers.AddRange(
                new ApiUser { Id = Id(901), Name = "A viewer", ApiKey = "bootcamp-viewer", Role = ApiRole.ClientViewer, ClientId = Id(101) },
                new ApiUser { Id = Id(902), Name = "A admin", ApiKey = "bootcamp-admin", Role = ApiRole.ClientAdmin, ClientId = Id(101) });
            db.OnboardingItems.AddRange(OnboardingItem.CreateDefaultChecklist(Id(301)));
            db.ComplianceDocuments.AddRange(
                Document(501, 201, ComplianceDocumentType.Passport, new DateOnly(2026, 9, 15)),
                Document(502, 201, ComplianceDocumentType.WorkPermit, new DateOnly(2026, 9, 20)),
                Document(503, 201, ComplianceDocumentType.Other, null),
                Document(504, 202, ComplianceDocumentType.Passport, new DateOnly(2026, 9, 15)));
            for (var i = 0; i < 3; i++)
                db.SalaryRecords.Add(new SalaryRecord
                {
                    Id = Id(601 + i), ContractId = Id(301), JobTitle = "Engineer", MonthlySalary = 100 + i * 10,
                    EffectiveDate = new DateOnly(2026, new[] { 1, 6, 9 }[i], 1),
                });
            db.PayrollRuns.AddRange(
                Run(401, "PH", 2026, 6, PayrollRunStatus.Draft),
                Run(402, "PH", 2026, 7, PayrollRunStatus.Completed),
                Run(403, "US", 2026, 7, PayrollRunStatus.Completed),
                Run(404, "PH", 2025, 7, PayrollRunStatus.Draft));
            db.Payslips.AddRange(Slip(701, 401, 301, 101, 201, "USD", 50),
                Slip(702, 402, 301, 101, 201, "USD", 100), Slip(703, 403, 302, 102, 202, "EUR", 200));
            db.Invoices.AddRange(Invoice(801, 402, 101, "USD"), Invoice(802, 402, 102, "USD"), Invoice(803, 403, 102, "EUR"));
            for (var month = 1; month <= 3; month++)
                db.FxRates.Add(new FxRate
                {
                    Id = Id(810 + month), BaseCurrencyCode = "USD", QuoteCurrencyCode = "EUR", Rate = month,
                    EffectiveDate = new DateOnly(2026, month, 1),
                });
            db.FxRates.Add(new FxRate { Id = Id(815), BaseCurrencyCode = "USD", QuoteCurrencyCode = "GBP", Rate = 1,
                EffectiveDate = new DateOnly(2026, 2, 1) });
            db.SaveChanges();
        });
    }

    private static ComplianceDocument Document(int id, int worker, ComplianceDocumentType type, DateOnly? expiry) =>
        new() { Id = Id(id), WorkerId = Id(worker), Type = type, Name = type.ToString(), ExpiryDate = expiry, CreatedAtUtc = Now.AddDays(-30) };
    private static PayrollRun Run(int id, string country, int year, int month, PayrollRunStatus status) =>
        new() { Id = Id(id), CountryCode = country, Year = year, Month = month, Status = status };
    private static Payslip Slip(int id, int run, int contract, int client, int worker, string currency, decimal gross) =>
        new() { Id = Id(id), PayrollRunId = Id(run), ContractId = Id(contract), ClientId = Id(client), WorkerId = Id(worker),
            CurrencyCode = currency, GrossSalary = gross, EmployerCost = gross * .2m, EmployeeDeductions = gross * .1m,
            NetPay = gross * .9m, TotalCost = gross * 1.2m };
    private static Invoice Invoice(int id, int run, int client, string currency) =>
        new() { Id = Id(id), InvoiceNumber = $"TEST-{id}", PayrollRunId = Id(run), ClientId = Id(client), CurrencyCode = currency,
            PayrollSubtotal = 120m, ManagementFee = 12m, Total = 132m, BillingCurrencyCode = currency,
            FxRateApplied = 1m, TotalInBillingCurrency = 132m, IssuedAtUtc = Now.AddDays(-10) };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
