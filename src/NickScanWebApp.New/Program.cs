using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Hosting; // For HostOptions
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanWebApp.New.Data;

using var instanceMutex = new Mutex(true, @"Global\NSCIM_WebApp_SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    Console.Error.WriteLine("Another instance of NSCIM WebApp is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// ✅ FIX: Remove EventLog logger — crashes with missing System.Threading.AccessControl.dll
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ✅ Configure logging to show only pertinent information - reduce noise
// Suppress verbose Blazor Server circuit logs (only show errors)
builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits.RemoteNavigationManager", LogLevel.Error);
builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost", LogLevel.Error);
builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits", LogLevel.Error);

// Suppress verbose HTTP client logs (only show warnings and errors)
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.NickScanAPI", LogLevel.Warning);

// Suppress verbose JS interop logs (only show errors)
builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost", LogLevel.Error);

// Suppress verbose SignalR logs (only show warnings and errors)
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);

// Suppress verbose routing logs
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);

// Suppress verbose hosting logs
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// Configure host to ignore background service exceptions (prevents shutdown on service errors)
builder.Services.Configure<HostOptions>(opts =>
{
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers(); // ✨ NEW: Add controllers for server-side auth endpoint
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    // Increase buffer sizes to prevent silent failures from message overflow
    options.MaxBufferedUnacknowledgedRenderBatches = 50;
});

// Add circuit handler for authentication management on server restart
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, NickScanWebApp.New.Services.AuthenticationCircuitHandler>();

// Add MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.TopRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
});

// ✅ NOTE: DualNotificationService registration moved below ApiService registration
// (See registration after ApiService is registered)

// ✅ AUTHENTICATION: Register AuthenticatedHttpMessageHandler for automatic token injection
builder.Services.AddScoped<NickScanWebApp.New.Services.AuthenticatedHttpMessageHandler>();

// ✅ HttpClient with authentication handler to automatically add JWT tokens
builder.Services.AddHttpClient("NickScanAPI", client =>
{
    // ✅ Use configured API URL (respects HTTP/HTTPS from appsettings.json)
    // Default to HTTP in development, HTTPS in production if not configured
    var apiUrl = builder.Configuration["ApiSettings:BaseUrl"];
    if (string.IsNullOrEmpty(apiUrl))
    {
        // Use 10.0.1.254 (not localhost) — the SSL cert's CN is 10.0.1.254,
        // and rendered image URLs get embedded into <img src=""> tags served
        // to the browser; the browser silently refuses to load cross-origin
        // images when the cert hostname doesn't match. localhost:5206 served
        // the right cert for the WebApp's own server-side HttpClient (which
        // ignores cert validity via the callback below) but broke every
        // <img> load client-side.
        apiUrl = builder.Environment.IsProduction()
            ? "https://10.0.1.254:5206"
            : "http://localhost:5205";
    }
    client.BaseAddress = new Uri(apiUrl);
    // ✅ FIX: Increased timeout to 90 seconds to handle slow cargo/ICUMS requests under load
    // Cargo group and ICUMS requests can exceed 60s for large groups or when DB is busy
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("ApiSettings:HttpClientTimeoutSeconds", 90));
})
.AddHttpMessageHandler<NickScanWebApp.New.Services.AuthenticatedHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();

    // ✅ SSL Certificate Validation
    // Always allow self-signed certificates to prevent certificate validation errors
    // This is safe for internal network use and prevents HTTPS connection failures
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        // Accept any certificate (including self-signed) to prevent HTTPS connection failures
        // This is appropriate for internal network use (10.0.1.254)
        return true;
    };
    Console.WriteLine("✅ HTTPS: Allowing self-signed SSL certificates for internal network use");

    return handler;
});

// Add HttpContextAccessor for authentication
builder.Services.AddHttpContextAccessor();

// ✅ SIMPLIFIED: JWT-only authentication (no cookies)
builder.Services.AddAuthorization(options =>
{
    // Register policies for both pages.* and controllers.* permissions
    // so Blazor pages can use [Authorize(Policy = ...)] with either prefix
    var allPermissions = Permissions
        .GetAllPermissions()
        .Where(p => p.Name.StartsWith("pages.", StringComparison.OrdinalIgnoreCase)
                 || p.Name.StartsWith("controllers.", StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var permission in allPermissions)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireAssertion(context =>
                context.User?.Claims.Any(c =>
                    string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase)) == true));
    }
});

// ✅ SINGLE AUTH PROVIDER: JWT-based authentication
builder.Services.AddScoped<NickScanWebApp.New.Services.SimpleAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<NickScanWebApp.New.Services.SimpleAuthStateProvider>());
builder.Services.AddCascadingAuthenticationState();

// ✅ Register CustomAuthStateProvider as a separate service for permission checks
builder.Services.AddScoped<NickScanWebApp.New.Services.CustomAuthStateProvider>();
builder.Services.AddScoped<NickScanWebApp.New.Services.Permissions.IPermissionProvider>(sp => sp.GetRequiredService<NickScanWebApp.New.Services.CustomAuthStateProvider>());

// Add theme service for dark mode (Scoped to allow IJSRuntime injection)
builder.Services.AddScoped<NickScanWebApp.New.Services.ThemeService>();

// Export service lives in NickScanWebApp.Shared.Services.ExportService and
// is registered below. No separate New-namespace version exists anymore.

// Add inactivity timer service for auto-logout
builder.Services.AddScoped<NickScanWebApp.New.Services.InactivityTimerService>();

// Add SignalR service for real-time updates
builder.Services.AddSingleton<NickScanWebApp.New.Services.SignalRService>();

// Add application services
builder.Services.AddMemoryCache();

// ✅ CRITICAL: Register ApiService FIRST before any services that depend on it
// Note: IHttpClientFactory is automatically registered by AddHttpClient("NickScanAPI", ...) above
// ✅ Register shared ApiService - DI will automatically resolve constructor dependencies
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ApiService>();
// ✅ Register New namespace ApiService (wrapper that inherits from shared service)
// DI will automatically resolve: IHttpClientFactory, ILogger<Shared.Services.ApiService>, AuthenticationStateProvider
builder.Services.AddScoped<NickScanWebApp.New.Services.ApiService>();
// ✅ MIGRATION COMPLETE: New.ApiService now wraps Shared.ApiService for backward compatibility

// ✅ Now register services that depend on ApiService
builder.Services.AddScoped<NickScanWebApp.New.Services.RoleLookupService>();
// builder.Services.AddSingleton<NickScanWebApp.New.Services.DataCacheService>();
// ✅ Use shared ContainerDetailsService from NickScanWebApp.Shared
builder.Services.AddScoped<NickScanWebApp.Shared.Services.IContainerDetailsService, NickScanWebApp.Shared.Services.ContainerDetailsService>();
// Shared services from NickScanWebApp.Shared
builder.Services.AddScoped<NickScanWebApp.Shared.Services.BLReviewService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ContainerProcessingService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.SettingsService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.GatewayService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ExportService>();
// App-specific services
builder.Services.AddScoped<NickScanWebApp.New.Services.CargoGroupService>();
builder.Services.AddScoped<NickScanWebApp.New.Services.CargoSummaryService>();
builder.Services.AddScoped<NickScanWebApp.New.Services.DualNotificationService>();
// View context orchestration
builder.Services.AddScoped<NickScanWebApp.New.Services.ViewContextCache>();
builder.Services.AddScoped<NickScanWebApp.New.Services.ContainerViewPreloader>();
builder.Services.AddScoped<NickScanWebApp.New.Services.CargoGroupViewPreloader>();
builder.Services.AddScoped<NickScanWebApp.New.Services.AuditReviewViewPreloader>();
builder.Services.AddScoped<NickScanWebApp.New.Services.ImageAnalysisViewPreloader>();

builder.Services.AddScoped<NickScanWebApp.New.Services.Permissions.PermissionCatalogClient>();
builder.Services.AddScoped<NickScanWebApp.New.Services.Permissions.PermissionGuard>();
builder.Services.AddScoped<NickScanWebApp.New.Services.Permissions.AuthBootstrapper>();

// v2.15.0 — in-app user manual. Singleton: corpus loaded once at startup
// (lazy, thread-safe); per-user filtering happens at render time through
// the scoped PermissionGuard.
builder.Services.AddSingleton<NickScanWebApp.New.Services.UserManual.UserManualService>();

// Demo services (temporarily disabled)
// builder.Services.AddSingleton<WeatherForecastService>();

// Add health checks
builder.Services.AddHealthChecks();

// Configure server limits to handle longer URLs (fix HTTP 414)
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestLineSize = 32768; // 32KB (default is 8KB) - Increased for long URLs
    options.Limits.MaxRequestHeadersTotalSize = 65536; // 64KB (default is 16KB) - Increased for headers
    options.Limits.MaxRequestHeaderCount = 200; // Increase header count limit
    options.Limits.MaxRequestBufferSize = 1048576; // 1MB buffer
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Detailed error pages in dev
}
else
{
    app.UseExceptionHandler("/Error");
    if (builder.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
    {
        app.UseHsts();
    }
}

// Forwarded headers — required for Cloudflare tunnel (scan.nickscan.net)
var forwardedHeadersOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// HTTPS redirection — only if an HTTPS endpoint is configured
if (!app.Environment.IsDevelopment() && builder.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
{
    app.UseHttpsRedirection();
}

// Add security headers middleware
app.Use(async (context, next) =>
{
    // Cache control - prevent caching of sensitive data
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";

    // Security headers
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Content Security Policy (adjust as needed for your app)
    // ✅ GATEWAY FIX: Allow HTTP images from API server (10.0.1.254:5205)
    var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://fonts.googleapis.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        $"img-src 'self' data: https: {apiBaseUrl}; " +
        $"connect-src 'self' {apiBaseUrl} ws: wss:;";

    await next();
});

app.UseStaticFiles();

// Mobile app retired 2026-04-22 — .New is responsive and serves all devices.
// (Formerly: app.UseMiddleware<MobileDetectionMiddleware>() redirected mobile user-agents.)

app.UseRouting();

// ✅ SIMPLIFIED: Only authorization (no cookie authentication)
app.UseAuthorization();

// Map health check endpoint
app.MapHealthChecks("/health");

// ✨ NEW: Map controllers for server-side auth endpoint
app.MapControllers();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
