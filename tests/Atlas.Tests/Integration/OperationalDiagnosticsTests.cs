using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Atlas.Tests.Integration;

public class OperationalDiagnosticsTests
{
    [Fact]
    public async Task DependencyFailure_FailsReadinessButKeepsLivenessHealthy_WithoutAuthentication()
    {
        using var factory = new AtlasApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck("unavailable-dependency",
                () => HealthCheckResult.Unhealthy("Simulated outage"), tags: ["ready"])));
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Clear();
        foreach (var route in new[] { "/health", "/health/ready" })
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(route)).StatusCode);
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var body = await live.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("checks").GetArrayLength());
    }

    [Theory]
    [InlineData("domain", 409)]
    [InlineData("concurrency", 409)]
    [InlineData("unexpected", 500)]
    public async Task HandledExceptions_LogFinalStatus_AndShareRequestIdWithProblem(string failure, int status)
    {
        using var capture = new CaptureProvider();
        using var factory = new AtlasApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new ThrowingClock(failure)));
            builder.ConfigureLogging(logging => logging.AddProvider(capture));
        });
        using var client = configured.CreateClient();
        var response = await client.GetAsync("/api/reports/headcount");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var requestId = Assert.Single(response.Headers.GetValues("X-Request-Id"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(requestId, body.GetProperty("traceId").GetString());
        var message = Assert.Single(capture.Messages);
        Assert.Contains($"responded {status}", message);
        Assert.Contains(requestId, message);
        Assert.DoesNotContain(AtlasApiFactory.AdminApiKey, message);
        if (status == 500) Assert.DoesNotContain("private failure", body.ToString());
    }

    [Theory]
    [InlineData("/missing", 404)]
    [InlineData("/api/reports/headcount", 401)]
    public async Task BareErrors_AlsoHaveCorrelatedProblemDetails(string route, int status)
    {
        using var factory = new AtlasApiFactory();
        using var client = factory.CreateClientWithApiKey(null);
        var response = await client.GetAsync(route);
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), body.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("concurrency")]
    public async Task KnownConflicts_FallBackToJson_ForUnsupportedAcceptHeader(string failure)
    {
        using var factory = new AtlasApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new ThrowingClock(failure))));
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        var response = await client.GetAsync("/api/reports/headcount");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), body.GetProperty("traceId").GetString());
    }

    private sealed class ThrowingClock(string failure) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw (failure switch
        {
            "domain" => new DomainException("Simulated domain conflict"),
            "concurrency" => new DbUpdateConcurrencyException("Simulated stale write"),
            _ => new InvalidOperationException("private failure"),
        });
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Messages);
        public void Dispose() { }

        private sealed class CaptureLogger(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                Func<TState, Exception?, string> formatter)
            {
                if (category == "Atlas.Api.RequestLoggingMiddleware") messages.Enqueue(formatter(state, error));
            }
        }
    }
}
