using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanWebApp.Mobile.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// Listen on all network interfaces so the app is reachable via LAN IP (e.g. http://10.0.1.254:5000)
// When running as a Windows Service, Kestrel endpoints from appsettings.json take precedence.
builder.WebHost.UseUrls("http://0.0.0.0:5000");

// Configure host to ignore background service exceptions
builder.Services.Configure<HostOptions>(opts =>
{
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// Add services to the container
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = true;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 50;
});

// Add circuit handler for authentication management
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, NickScanWebApp.Mobile.Services.AuthenticationCircuitHandler>();

// Add MudBlazor services - Mobile-optimized configuration
builder.Services.AddMudServices(config =>
{
    // Mobile: Use bottom position for snackbars (better for mobile)
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000; // Slightly shorter for mobile
    config.SnackbarConfiguration.HideTransitionDuration = 300;
    config.SnackbarConfiguration.ShowTransitionDuration = 300;
    // Mobile-specific: Larger touch targets
    config.SnackbarConfiguration.MaxDisplayedSnackbars = 3; // Less on mobile screen
});

// Add DualNotificationService
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.DualNotificationService>();

// ✅ AUTHENTICATION: Register AuthenticatedHttpMessageHandler for automatic token injection
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.AuthenticatedHttpMessageHandler>();

// ✅ HttpClient with authentication handler to automatically add JWT tokens
builder.Services.AddHttpClient("NickScanAPI", client =>
{
    // ✅ Use configured API URL (respects HTTP/HTTPS from appsettings.json)
    var apiUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
    client.BaseAddress = new Uri(apiUrl);
    // ✅ FIX: Increased timeout to 60 seconds to handle slow cargo group requests
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<NickScanWebApp.Mobile.Services.AuthenticatedHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    // ✅ SSL Certificate Validation
    // Always allow self-signed certificates to prevent certificate validation errors
    // This is safe for internal network use and prevents HTTPS connection failures
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        // Accept any certificate (including self-signed) to prevent HTTPS connection failures
        // This is appropriate for internal network use
        return true;
    };
    Console.WriteLine("✅ Mobile App: Allowing self-signed SSL certificates for internal network use");
    return handler;
});

// Add HttpContextAccessor for authentication
builder.Services.AddHttpContextAccessor();

// Configure authorization policies
builder.Services.AddAuthorization(options =>
{
    var pagePermissions = Permissions
        .GetAllPermissions()
        .Where(p => p.Name.StartsWith("pages.", StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var permission in pagePermissions)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireAssertion(context =>
                context.User?.Claims.Any(c =>
                    string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase)) == true));
    }
});

// Authentication services
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.SimpleAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<NickScanWebApp.Mobile.Services.SimpleAuthStateProvider>());
builder.Services.AddCascadingAuthenticationState();

// Register CustomAuthStateProvider for permission checks
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.CustomAuthStateProvider>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.Permissions.IPermissionProvider>(sp => sp.GetRequiredService<NickScanWebApp.Mobile.Services.CustomAuthStateProvider>());

// Theme service for dark mode
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.ThemeService>();

// Inactivity timer service
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.InactivityTimerService>();

// SignalR service for real-time updates
builder.Services.AddSingleton<NickScanWebApp.Mobile.Services.SignalRService>();

// Application services
builder.Services.AddMemoryCache();
builder.Services.AddDataCacheService();
builder.Services.AddScoped<Lazy<NickScanWebApp.Shared.Services.ApiService>>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ApiService>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.ApiService>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.RoleLookupService>();
// Shared services from NickScanWebApp.Shared
builder.Services.AddScoped<NickScanWebApp.Shared.Services.IContainerDetailsService, NickScanWebApp.Shared.Services.ContainerDetailsService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.BLReviewService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ContainerProcessingService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.SettingsService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.GatewayService>();
builder.Services.AddScoped<NickScanWebApp.Shared.Services.ExportService>();
// Mobile-specific services
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.ContainerProcessingService>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.CargoGroupService>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.CargoSummaryService>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.Permissions.PermissionCatalogClient>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.Permissions.PermissionGuard>();
builder.Services.AddScoped<NickScanWebApp.Mobile.Services.Permissions.AuthBootstrapper>();

// Health checks
builder.Services.AddHealthChecks();

// Configure server limits
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestLineSize = 32768;
    options.Limits.MaxRequestHeadersTotalSize = 65536;
    options.Limits.MaxRequestHeaderCount = 200;
    options.Limits.MaxRequestBufferSize = 1048576;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// HTTPS redirection (disabled in development for mobile testing)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy for mobile
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
app.UseRouting();
app.UseAuthorization();

// Map health check endpoint
app.MapHealthChecks("/health");

// Map controllers
app.MapControllers();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
