using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.Infrastructure.Data;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Platform;

/// <summary>
/// End-to-end coverage for the platform-contract surface introduced during
/// the Week-2 security work: PLATFORM.md §6 module manifest, deny-by-default
/// auth, and the single-session sid pipeline. These are the exact surfaces
/// most prone to silent regression — a stale RouteTable entry, an
/// AllowAnonymous slipping into a manifest, a missing sid claim — so they
/// earn dedicated integration coverage that boots a real <see cref="WebApplicationFactory"/>
/// and exercises the HTTP pipeline.
/// </summary>
public class PlatformContractTests : IClassFixture<PlatformContractTests.Factory>
{
    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlatformContractTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Test factory that bypasses the production single-instance Mutex (see
    /// Program.cs) and overrides the EF DbContexts to run on the InMemory
    /// provider. Runs once per test class via <see cref="IClassFixture{T}"/>.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Set BEFORE the entry point fires so the Mutex check honours it.
            // Production never sets this; the env var is a test-only hatch.
            Environment.SetEnvironmentVariable("NSCIM_SKIP_SINGLE_INSTANCE", "1");
            // JWT key envvars must exist; the live machine has them, but the
            // test runner inherits the parent shell environment so this is
            // mostly a guard for CI runs.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NICKSCAN_JWT_SECRET_KEY")))
            {
                Environment.SetEnvironmentVariable(
                    "NICKSCAN_JWT_SECRET_KEY",
                    "test-only-jwt-key-do-not-use-in-prod-9876543210abcdef");
            }
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Replace the real Postgres DbContexts with InMemory so we don't
                // touch nickscan_production from the test suite. The InMemory
                // provider intentionally doesn't enforce relational constraints —
                // these tests only need the contexts to be resolvable.
                ReplaceDbContext<ApplicationDbContext>(services, "Tests_AppDb");
                ReplaceDbContext<IcumDownloadsDbContext>(services, "Tests_IcumDb");
            });
        }

        private static void ReplaceDbContext<TContext>(IServiceCollection services, string dbName)
            where TContext : DbContext
        {
            var optionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TContext>));
            if (optionsDescriptor != null) services.Remove(optionsDescriptor);

            services.AddDbContext<TContext>(o => o.UseInMemoryDatabase(dbName));
        }
    }

    [Fact]
    public async Task ModuleManifest_IsAnonymousAndReturnsExpectedShape()
    {
        // Endpoint must be reachable without auth — the unified portal launcher
        // hits it before any login token exists.
        var response = await _client.GetAsync("/api/_module/manifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("NSCIM", root.GetProperty("name").GetString());
        Assert.Equal("1.0", root.GetProperty("platformContractVersion").GetString());

        // Capabilities is an advertisement contract — the unified launcher
        // routes by it. Any rename here is a breaking change to the Portal.
        var capabilities = root.GetProperty("capabilities");
        var caps = capabilities.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("container.scan", caps);
        Assert.Contains("icums.submit", caps);
    }

    [Fact]
    public async Task UnauthenticatedRequestToProtectedEndpoint_Returns401()
    {
        // FallbackPolicy = RequireAuthenticatedUser means anything not explicitly
        // marked [AllowAnonymous] should reject anonymous traffic. This is the
        // regression test for the "controller forgot [Authorize] and returned 200"
        // class of bug that bit ImageAnalysisManagementController and
        // BiometricController in the past.
        var response = await _client.GetAsync("/api/Authentication/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_IsAnonymousAndExecutes()
    {
        // /api/health must stay open — used by Cloudflared, the synthetic
        // monitor, and Docker healthchecks. We tolerate any non-401 status
        // because the InMemory DbContext can't satisfy the live Postgres
        // health probe; what matters here is that the endpoint isn't gated
        // behind auth (a 401 would indicate the regression).
        var response = await _client.GetAsync("/api/health");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
