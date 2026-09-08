using Atlas.Api;
using Atlas.Api.Auth;
using Atlas.Api.Endpoints;
using Atlas.Api.Reporting;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Atlas") ?? "Data Source=atlas.db";
builder.Services.AddDbContext<AtlasDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<InvoiceLedgerService>();
builder.Services.AddScoped<ExpenseApprovalService>();
builder.Services.AddScoped<ContractProposalService>();
builder.Services.AddScoped<PayrollAssessmentService>();
builder.Services.AddScoped<DurableJobService>();
builder.Services.AddScoped<NotificationService>();
if (!builder.Environment.IsEnvironment("Testing") && builder.Configuration.GetValue("Notifications:Enabled", true))
    builder.Services.AddHostedService<NotificationWorker>();
if (!builder.Environment.IsEnvironment("Testing") && builder.Configuration.GetValue("Jobs:Enabled", true))
    builder.Services.AddHostedService<BackgroundJobWorker>();
builder.Services.AddSingleton(TimeProvider.System);

// API-key authentication (X-Api-Key header) with role/client-scope claims.
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthPolicies.PlatformAdmin, policy => policy.RequireRole(nameof(ApiRole.PlatformAdmin))));

// RFC 7807 problem responses for unhandled errors and bare status codes.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// Wrap error handling so logging observes the translated HTTP response.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
    db.Database.EnsureCreated();
    // WAL permits a job's consistent input reader and separate lease renewals.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    DataSeeder.Seed(db);
}

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapCountryEndpoints();
app.MapClientEndpoints();
app.MapWorkerEndpoints();
app.MapContractEndpoints();
app.MapOnboardingEndpoints();
app.MapComplianceEndpoints();
app.MapPayrollEndpoints();
app.MapInvoiceEndpoints();
app.MapApiUserEndpoints();
app.MapLeaveEndpoints();
app.MapExpenseEndpoints();
app.MapAmendmentEndpoints();
app.MapFxRateEndpoints();
app.MapBenefitEndpoints();
app.MapTerminationEndpoints();
app.MapReportEndpoints();
app.MapWorkforceViewEndpoints();
app.MapFinancialViewEndpoints();
app.MapOperationsViewEndpoints();
app.MapOnboardingWorkflowEndpoints();
app.MapInvoiceLedgerEndpoints();
app.MapBillingConfigurationEndpoints();
app.MapLeaveWorkflowEndpoints();
app.MapExpenseWorkflowEndpoints();
app.MapContractPreviewEndpoints();
app.MapReportPresetEndpoints();
app.MapPayrollLearningEndpoints();
app.MapPayrollJobEndpoints();
app.MapComplianceWorkflowEndpoints();
app.MapClientConfigurationEndpoints();
app.MapCostAllocationEndpoints();
app.MapWorkforceSnapshotEndpoints();
app.MapWorkerImportEndpoints();
app.MapContractDecisionEndpoints();
app.MapClientExportEndpoints();

app.Run();

/// <summary>Exposes the implicit Program class to WebApplicationFactory-based integration tests.</summary>
public partial class Program;
