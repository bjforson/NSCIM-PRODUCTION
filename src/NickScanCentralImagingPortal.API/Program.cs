using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NickERP.Platform.Tenancy;
using NickScanCentralImagingPortal.API.Logging;
using NickScanCentralImagingPortal.API.Middleware;
using NickScanCentralImagingPortal.API.Monitoring;
using NickScanCentralImagingPortal.API.Startup;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Services;
using Serilog;
// using AspNetCoreRateLimit; // REMOVED - replaced with .NET 8 built-in rate limiting

using var instanceMutex = new Mutex(true, @"Global\NSCIM_API_SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    Console.Error.WriteLine("Another instance of NSCIM API is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// ✅ FIX: Remove EventLog logger added by UseWindowsService() — it requires
// System.Threading.AccessControl.dll which is missing from the publish output,
// causing AggregateException on any log write and crashing Blazor circuits.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ✅ MEMORY OPTIMIZATION: Configure aggressive garbage collection for server workload
System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch; // Optimize for throughput
System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

// ✅ SECURITY FIX: Load environment variables (prefixed with NICKSCAN_)
// This loads NICKSCAN_* variables and makes them available in configuration
builder.Configuration.AddEnvironmentVariables("NICKSCAN_");

// Load logging configuration
builder.Configuration.AddJsonFile("appsettings.Logging.json", optional: true, reloadOnChange: true);

// ✅ SECURITY FIX: Replace credential placeholders with environment variables
// Note: IConfiguration is read-only, so we can't modify it directly
// Instead, the environment variables are loaded above and will be checked in the service code
ReplaceCredentialsWithEnvironmentVariables(builder.Configuration);

// ✅ CONNECTION STRING FIX: Normalize connection strings to use TCP/IP and handle environment-specific settings
NormalizeConnectionStrings(builder.Configuration, builder.Environment);

// Configure Serilog with EF logging completely disabled
// Note: Main configuration comes from appsettings.Logging.json
// This ensures consistent file size limits, rolling intervals, and retention policies
var dbConnString = builder.Configuration.GetConnectionString("NS_CIS_Connection");
var columnWriters = new Dictionary<string, Serilog.Sinks.PostgreSQL.ColumnWriters.ColumnWriterBase>
{
    { "timestamp", new Serilog.Sinks.PostgreSQL.ColumnWriters.TimestampColumnWriter() },
    { "level", new Serilog.Sinks.PostgreSQL.ColumnWriters.LevelColumnWriter(true, NpgsqlTypes.NpgsqlDbType.Varchar) },
    { "message", new Serilog.Sinks.PostgreSQL.ColumnWriters.RenderedMessageColumnWriter() },
    { "exception", new Serilog.Sinks.PostgreSQL.ColumnWriters.ExceptionColumnWriter() },
    { "properties", new Serilog.Sinks.PostgreSQL.ColumnWriters.PropertiesColumnWriter() },
    { "logger", new Serilog.Sinks.PostgreSQL.ColumnWriters.SinglePropertyColumnWriter("SourceContext", format: "l") },
};

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new NickScanCentralImagingPortal.API.Logging.ServiceColorFormatter())
    .WriteTo.PostgreSQL(
        connectionString: dbConnString ?? "",
        tableName: "applicationlogs",
        columnOptions: columnWriters,
        needAutoCreateTable: false,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
        batchSizeLimit: 50,
        period: TimeSpan.FromSeconds(10))
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Transaction", Serilog.Events.LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", Serilog.Events.LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Query", Serilog.Events.LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", Serilog.Events.LogEventLevel.Fatal)
    .CreateLogger();

builder.Host.UseSerilog();

// Add standardized services
builder.Services.AddStandardizedServices(builder.Configuration);

builder.Services.AddHttpClient();

// Raw Image Engine (Python service on port 5320)
var rawImageEngineUrl = builder.Configuration["RawImageEngine:BaseUrl"]
    ?? builder.Configuration["ImageSplitter:BaseUrl"]
    ?? "http://localhost:5320";
builder.Services.AddHttpClient("RawImageEngine", client =>
{
    client.BaseAddress = new Uri(rawImageEngineUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "NickScan-NSCIM/2.6");
});
builder.Services.AddHttpClient<NickScanCentralImagingPortal.Services.ImageSplitter.IImageSplitterService,
    NickScanCentralImagingPortal.Services.ImageSplitter.ImageSplitterService>(client =>
{
    client.BaseAddress = new Uri(rawImageEngineUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
// 2.15.3: NSCIM_API now supervises the Python image-splitter subprocess directly
// (see ImageSplitterSupervisorService). The old ImageSplitterHealthMonitorService
// only logged health — the supervisor does that AND manages the process lifecycle.
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.ImageSplitter.ImageSplitterSupervisorService>();

// NICKSCAN ERP — Phase 1 multi-tenancy: ITenantContext + TenantOwnedEntityInterceptor.
// The DbContexts (registered later in ServiceConfiguration) consume the
// interceptor via DI. This call is a no-op for entities that don't yet
// implement ITenantOwned, so it's safe to wire up everywhere.
builder.Services.AddNickERPTenancy();

// Add API services with global validation filter
builder.Services.AddControllers(options =>
{
    // Add global model validation filter
    options.Filters.Add<NickScanCentralImagingPortal.API.Filters.ModelValidationFilter>();
})
.AddJsonOptions(options =>
{
    // Serialize enums as strings instead of numbers for better API compatibility
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep original property names
})
.ConfigureApiBehaviorOptions(options =>
{
    // Disable automatic 400 responses to use our custom validation filter
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddEndpointsApiExplorer();

// ✅ Configure JWT Authentication
var jwtSecretKey = Environment.GetEnvironmentVariable("NICKSCAN_JWT_SECRET_KEY")
                   ?? builder.Configuration["Jwt:SecretKey"];

if (string.IsNullOrEmpty(jwtSecretKey) || jwtSecretKey.Contains("***USE_ENV_VAR***"))
{
    if (builder.Environment.IsProduction())
    {
        Log.Fatal("NICKSCAN_JWT_SECRET_KEY is not configured. Cannot start in production without a proper JWT key.");
        throw new InvalidOperationException("JWT Secret Key must be configured via NICKSCAN_JWT_SECRET_KEY environment variable in production.");
    }
    Log.Warning("JWT Secret Key not configured. Generating temporary key for development.");
    jwtSecretKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    Environment.SetEnvironmentVariable("NICKSCAN_JWT_SECRET_KEY", jwtSecretKey);
}

// ✨ DUAL AUTHENTICATION: Support both Cookies (for Blazor Server) and JWT (for API clients)
builder.Services.AddAuthentication(options =>
{
    // Use cookie auth by default (better for Blazor Server)
    options.DefaultAuthenticateScheme = "Dual";
    options.DefaultChallengeScheme = "Dual";
    options.DefaultScheme = "Dual";
})
.AddPolicyScheme("Dual", "Cookie or JWT", options =>
{
    // Try cookie first (Blazor Server), then JWT (API clients)
    options.ForwardDefaultSelector = context =>
    {
        // If request has a JWT token, use JWT scheme
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        // Otherwise use cookie scheme (Blazor Server)
        return Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
    };
})
.AddCookie(options =>
{
    options.Cookie.Name = "NickScan.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Strict; // ✅ Stricter than Lax (prevents CSRF)
    options.ExpireTimeSpan = TimeSpan.FromHours(builder.Configuration.GetValue<int>("Auth:CookieExpireHours", 8));
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";

    options.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
    {
        OnValidatePrincipal = context =>
        {
            var username = context.Principal?.Identity?.Name;
            Log.Debug("Cookie validated for user: {Username}", username);
            return Task.CompletedTask;
        },
        OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = builder.Environment.IsProduction(); // ✅ Enforce HTTPS in production
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        // ✅ FIX: Provide defaults for JWT configuration values
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "NickScanAPI",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "NickScanClients",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ClockSkew = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Jwt:ClockSkewMinutes", 1))
    };

    // JWT event handlers for logging
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Log.Warning("JWT Authentication failed: {Error}", context.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var username = context.Principal?.Identity?.Name;
            Log.Debug("JWT token validated for user: {Username}", username);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Log.Debug("JWT Challenge: {Error}", context.ErrorDescription ?? "No token provided");
            return Task.CompletedTask;
        }
    };
});

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    // ✅ SECURITY: Deny-by-default. Every endpoint requires an authenticated user unless it
    // explicitly opts in with [AllowAnonymous] (health, login, public stats).
    // This closes the class of bugs where a controller forgot its class-level [Authorize]
    // and anonymous requests fell through — see ImageAnalysisManagementController history.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    static bool HasAnyPermission(System.Security.Claims.ClaimsPrincipal? user, params string[] permissions)
    {
        if (user == null) return false;
        var permissionSet = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        return user.Claims.Any(c =>
            string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase) &&
            permissionSet.Contains(c.Value));
    }

    var adminPolicyPermissions = new[]
    {
        Permissions.ControllersUsersView,
        Permissions.ControllersUsersCreate,
        Permissions.ControllersUsersEdit,
        Permissions.ControllersUsersDelete,
        Permissions.ControllersUsersRoles,
        Permissions.ControllersPermissionsManage,
        Permissions.PagesAdminUsers,
        Permissions.PagesAdminRoles,
        Permissions.PagesAdminPermissions,
        Permissions.PagesAdminSettings,
        Permissions.PagesAdminServiceControl,
        Permissions.PagesAdminLogs,
        Permissions.PagesAdminAudit,
        Permissions.PagesIcumsPayloads,
        Permissions.PagesIcumsVerifyStatus
    };

    var customsOfficerPermissions = new[]
    {
        Permissions.PagesIcumsView,
        Permissions.PagesIcumsDownloadQueue,
        Permissions.PagesIcumsSubmissionQueue,
        Permissions.PagesIcumsBoeRequest,
        Permissions.PagesIcumsLooseCargo,
        Permissions.PagesIcumsAnalytics
    };

    var scannerOperatorPermissions = new[]
    {
        Permissions.PagesScannersView,
        Permissions.PagesScannersAse,
        Permissions.PagesScannersFs6000,
        Permissions.PagesScannersHeimann,
        Permissions.ScannersManage,
        Permissions.ScannersConfigure
    };

    var analystPermissions = new[]
    {
        Permissions.PagesImageAnalysisView,
        Permissions.ControllersImageAnalysisMyAssignments,
        Permissions.ControllersImageAnalysisAvailable,
        Permissions.ControllersImageAnalysisClaim,
        Permissions.PagesValidationBoeLookup,
        Permissions.PagesValidationRecordCompleteness
    };

    var auditPermissions = new[]
    {
        Permissions.PagesImageAnalysisAudit,
        Permissions.ControllersImageAnalysisDecisionAudit
    };

    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAssertion(context => HasAnyPermission(context.User, adminPolicyPermissions)));

    options.AddPolicy("CustomsOfficer", policy =>
        policy.RequireAssertion(context => HasAnyPermission(context.User, customsOfficerPermissions)));

    options.AddPolicy("ScannerOperator", policy =>
        policy.RequireAssertion(context => HasAnyPermission(context.User, scannerOperatorPermissions)));

    options.AddPolicy("ImageAnalyst", policy =>
        policy.RequireAssertion(context => HasAnyPermission(context.User, analystPermissions)));

    options.AddPolicy("AuditReviewer", policy =>
        policy.RequireAssertion(context => HasAnyPermission(context.User, auditPermissions)));
});

// ✅ Register dynamic authorization policy provider for permission-based authorization
builder.Services.AddSingleton<IAuthorizationPolicyProvider, NickScanCentralImagingPortal.API.Authorization.DynamicAuthorizationPolicyProvider>();

// ✅ Register permission authorization handler
builder.Services.AddScoped<IAuthorizationHandler, NickScanCentralImagingPortal.API.Authorization.PermissionAuthorizationHandler>();

// ✅ Configure Swagger with JWT support, XML docs, and examples
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "NickScan Central Imaging Portal API",
        Version = "v1.0.0",
        Description = @"
## Container Scanning and Validation API for Ghana Customs (GRA/GPHA)

This API provides comprehensive container scanning integration, ICUMS (Ghana Customs) data management, 
and real-time monitoring capabilities.

### Features
- 🔐 JWT Bearer Authentication
- 👥 Role-Based Access Control (RBAC)
- 🚦 Rate Limiting (100 req/min per IP)
- 📦 Container Data Management
- 🖼️ Image Processing (ASE, FS6000, Heimann Smith scanners)
- 🔄 ICUMS Integration with Circuit Breaker
- 📊 Real-time Dashboard with SignalR
- ✅ Container Validation & Completeness Tracking

### Authentication
1. Call `POST /api/Authentication/login` with credentials
2. Copy the JWT token from response
3. Click 'Authorize' button above
4. Enter: `Bearer {your-token-here}`
5. All subsequent requests will be authenticated

### Rate Limits
- General API: 100 requests/minute
- Login: 5 attempts/minute
- Health checks: No limit
",
        Contact = new Microsoft.OpenApi.OpenApiContact
        {
            Name = "NickScan Support",
            Email = "support@nickscan.com",
            Url = new Uri("https://nickscan.com")
        },
        License = new Microsoft.OpenApi.OpenApiLicense
        {
            Name = "Proprietary",
            Url = new Uri("https://nickscan.com/license")
        }
    });

    // ✅ Enable XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        Log.Information("✅ Swagger XML documentation enabled from {XmlPath}", xmlPath);
    }

    // ✅ Enable Annotations
    options.EnableAnnotations();

    // ✅ Resolve conflicting actions (e.g., backward compatibility endpoints with same route pattern)
    // This handles cases where multiple actions share the same route (e.g., Guid vs string parameters)
    // Swagger will automatically exclude actions marked with [ApiExplorerSettings(IgnoreApi = true)]
    // This resolver just picks the first non-ignored action if there are conflicts
    options.ResolveConflictingActions(apiDescriptions =>
    {
        // Return the first action - Swagger will automatically skip ones with IgnoreApi = true
        return apiDescriptions.First();
    });

    // ✅ Add operation tags by controller name
    options.TagActionsBy(api => new[] { api.GroupName ?? api.ActionDescriptor.RouteValues["controller"] ?? "Unknown" });

    // ✅ Include all APIs (obsolete endpoints will be marked as deprecated in Swagger)
    options.DocInclusionPredicate((name, api) => true);

    // ✅ Order actions by HTTP method
    options.OrderActionsBy((apiDesc) => $"{apiDesc.ActionDescriptor.RouteValues["controller"]}_{apiDesc.HttpMethod}");

    // Add JWT authentication to Swagger UI
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Description = @"
JWT Authorization header using the Bearer scheme.

**How to authenticate:**
1. Call POST /api/Authentication/login with your username and password
2. Copy the 'token' value from the response
3. Click the 'Authorize' button below
4. Enter: Bearer {your-token-here}
5. Click 'Authorize' and then 'Close'

**Example:**
```
Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**Roles:**
- SuperAdmin: Full access
- Administrator: Admin operations
- Customs Officer: ICUMS operations, validations
- Scanner Operator: Scanner management, ingestion
- Viewer: Read-only access
"
    });

    // Swashbuckle 10.x + Microsoft.OpenApi 2.x: AddSecurityRequirement now takes a
     // Func<OpenApiDocument, OpenApiSecurityRequirement> and uses OpenApiSecuritySchemeReference
     // instead of the old OpenApiSecurityScheme { Reference = ... } pattern.
    options.AddSecurityRequirement((document) => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// ✅ Add Comprehensive Health Checks
builder.Services.AddHealthChecks()
    // Database health checks
    .AddDbContextCheck<NickScanCentralImagingPortal.Infrastructure.Data.ApplicationDbContext>(
        "NS_CIS_Database",
        tags: new[] { "database", "ns_cis" })
    .AddDbContextCheck<NickScanCentralImagingPortal.Infrastructure.Data.IcumDbContext>(
        "ICUMS_Database",
        tags: new[] { "database", "icums" })
    .AddDbContextCheck<NickScanCentralImagingPortal.Infrastructure.Data.IcumDownloadsDbContext>(
        "ICUMS_Downloads_Database",
        tags: new[] { "database", "icums_downloads" })
    // ✅ SSL Certificate health check
    .AddCheck<NickScanCentralImagingPortal.API.Services.CertificateHealthCheck>(
        "ssl_certificate",
        tags: new[] { "ssl", "security" })
    // PostgreSQL connection health checks
    .AddNpgSql(
        builder.Configuration.GetConnectionString("NS_CIS_Connection") ?? "",
        name: "NS_CIS_PG_Connection",
        tags: new[] { "database", "postgresql" })
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ICUMS_Connection") ?? "",
        name: "ICUMS_PG_Connection",
        tags: new[] { "database", "postgresql" })
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ICUMS_Downloads_Connection") ?? "",
        name: "ICUMS_Downloads_PG_Connection",
        tags: new[] { "database", "postgresql" });

// ✅ Add Health Checks UI
builder.Services.AddHealthChecksUI(settings =>
{
    settings.SetEvaluationTimeInSeconds(60); // Check every 60 seconds
    settings.MaximumHistoryEntriesPerEndpoint(50);
    // ✅ FIX: Use absolute URL with localhost to avoid DNS resolution errors
    // HealthChecks UI makes HTTP calls internally and cannot use 0.0.0.0 as a connection address
    // Extract port from ASPNETCORE_URLS or use default from launchSettings.json (5205)
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? builder.Configuration["Urls"] ?? "";
    var apiPort = "5205"; // Default port (from launchSettings.json)
    if (!string.IsNullOrEmpty(urls))
    {
        // Try to extract port from URLs like "http://0.0.0.0:5205" or "http://localhost:5205"
        var portMatch = System.Text.RegularExpressions.Regex.Match(urls, @":(\d+)");
        if (portMatch.Success)
        {
            apiPort = portMatch.Groups[1].Value;
        }
    }
    // Use absolute URL with localhost (valid connection address) - HTTP only
    // Note: HTTPS redirection is disabled when using HTTP only to prevent SSL errors
    var healthEndpointUrl = $"http://localhost:{apiPort}/health";
    settings.AddHealthCheckEndpoint("NickScan API", healthEndpointUrl);
}).AddInMemoryStorage();

Log.Information("✅ Comprehensive health checks configured (3 databases, UI enabled)");

// Add comprehensive monitoring with SignalR
// This already registers ComprehensiveHealthCheckService as singleton and hosted service
builder.Services.AddMonitoringWithSignalR();

// Add structured logging services
builder.Services.AddSingleton<IStructuredLoggingService, StructuredLoggingService>();

// ✅ Add Security Event Logger
builder.Services.AddSingleton<NickScanCentralImagingPortal.API.Services.ISecurityEventLogger,
    NickScanCentralImagingPortal.API.Services.SecurityEventLogger>();
Log.Information("✅ Security event logger registered");

// ✅ Add Performance Metrics Service
builder.Services.AddSingleton<NickScanCentralImagingPortal.API.Services.IPerformanceMetricsService,
    NickScanCentralImagingPortal.API.Services.PerformanceMetricsService>();
Log.Information("✅ Performance metrics service registered");

// ✅ Add Notification Service
builder.Services.AddScoped<NickScanCentralImagingPortal.Infrastructure.Services.INotificationService,
    NickScanCentralImagingPortal.Infrastructure.Services.NotificationService>();
Log.Information("✅ Notification service registered");

// ✅ REMOVED: ContainerCompletenessService duplicate registration
// ContainerCompletenessOrchestratorService (registered in ServiceConfiguration.cs) already orchestrates
// the completeness check workflow using the scoped IContainerCompletenessService interface.
// builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.ContainerCompleteness.ContainerCompletenessService>();
// Log.Information("✅ Container Completeness Service registered");

builder.Services.AddScoped<NickScanCentralImagingPortal.Services.ContainerCompleteness.MultiContainerValidationService>();
builder.Services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.ICargoGroupService, NickScanCentralImagingPortal.Services.CargoGrouping.CargoGroupService>();

// Endpoint Usage Monitoring Service
builder.Services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IEndpointUsageService,
    NickScanCentralImagingPortal.Services.Monitoring.EndpointUsageService>();

// Endpoint Usage Buffer Service - batches inserts to reduce DB load (optional, middleware uses when available)
builder.Services.AddSingleton<NickScanCentralImagingPortal.Services.Monitoring.EndpointUsageBufferService>();
builder.Services.AddSingleton<NickScanCentralImagingPortal.Core.Interfaces.IEndpointUsageBufferService>(sp =>
    sp.GetRequiredService<NickScanCentralImagingPortal.Services.Monitoring.EndpointUsageBufferService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NickScanCentralImagingPortal.Services.Monitoring.EndpointUsageBufferService>());
Log.Information("✅ Multi-Container Validation Service registered");

// ✅ FIX: Register interface, not concrete class (ServiceConfiguration already registers it, but this ensures it's available)
builder.Services.AddScoped<NickScanCentralImagingPortal.Services.IcumApi.IICUMSDownloadQueueService, NickScanCentralImagingPortal.Services.IcumApi.ICUMSDownloadQueueService>();
Log.Information("✅ ICUMS Download Queue Service registered");

// PostICUMSValidationService runs inside ContainerCompletenessOrchestratorService — standalone registration removed to prevent duplicate execution

// ✅ Image Analysis Dashboard Broadcast Service - Real-time dashboard updates
builder.Services.AddHostedService<NickScanCentralImagingPortal.API.Hubs.ImageAnalysisDashboardBroadcastService>();
Log.Information("✅ Image Analysis Dashboard Broadcast Service registered");

// ✅ Dashboard Broadcast Service - Comprehensive dashboard SignalR broadcasts (30s interval from settings)
builder.Services.AddHostedService<NickScanCentralImagingPortal.API.Hubs.DashboardBroadcastService>();
Log.Information("✅ Dashboard Broadcast Service registered");

// ✅ User Readiness Sync Service - Syncs SignalR state to database
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.ImageAnalysis.UserReadinessSyncService>();
Log.Information("✅ User Readiness Sync Service registered");

// IcumJsonIngestionService: Standalone service handles full JSON parsing and ingestion.
// The orchestrator does NOT duplicate this — ingestion is handled entirely here.
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.IcumApi.IcumJsonIngestionService>();
Log.Information("✅ ICUMS JSON Ingestion Service registered");

// ✅ PHASE 2.2: Failed File Retry Service - Dead-Letter Queue with automatic retry
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.IcumApi.FailedFileRetryService>();
Log.Information("✅ Failed File Retry Service registered (Phase 2.2)");

// ✅ PHASE 3.1: ICUMS Metrics Collector Service - Updates gauges periodically
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.IcumApi.ICUMSMetricsCollectorService>();
Log.Information("✅ ICUMS Metrics Collector Service registered (Phase 3.1)");

// ✅ ARCHIVE SOLUTION: ICUMS File Archive Service - Archives processed files with compression
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.IcumApi.IcumFileArchiveService>();
Log.Information("✅ ICUMS File Archive Service registered (Archive Solution)");

// ✅ 1.14.0 — Record Completeness Reconciliation Worker
// Event-driven record building service — called by ingestion + completeness services
// immediately after BOE data arrives or container images become available.
builder.Services.AddSingleton<NickScanCentralImagingPortal.Services.RecordCompleteness.IRecordBuildingService,
    NickScanCentralImagingPortal.Services.RecordCompleteness.RecordBuildingService>();
Log.Information("✅ RecordBuildingService registered (event-driven record building)");

// Safety-net reconciliation pass — catches anything the event-driven path missed.
// See RecordReconciliationWorker.cs for the full state machine.
builder.Services.AddHostedService<NickScanCentralImagingPortal.Services.RecordCompleteness.RecordReconciliationWorker>();
Log.Information("✅ Record Reconciliation Worker registered (1.14.0 — safety-net)");

// ✅ Image Analysis Background Services are already registered in ServiceConfiguration.AddStandardizedServices()
// All workers (IntakeWorker, AssignmentWorker, SubmissionWorker, HousekeepingWorker, ImageAnalysisBootstrapper) 
// are registered via AddBackgroundServices() method
// No need to register them again here to avoid duplicate registrations

// ✅ PHASE 3: Configure Redis and Caching from database settings
// Read caching configuration from database (using temporary service provider)
var redisEnabled = false;
var redisConnection = "localhost:6379";
var redisInstanceName = "NickScanPortal:";
var responseCachingEnabled = false;
var maxResponseBodySizeMB = 1;
var useCaseSensitivePaths = false;

try
{
    // Create a temporary service provider to read settings
    var tempServices = new ServiceCollection();
    // ✅ FIX: Explicitly add IConfiguration and IMemoryCache to temporary service collection
    tempServices.AddSingleton<IConfiguration>(builder.Configuration);
    tempServices.AddMemoryCache(); // Required by SettingsProvider
    tempServices.AddStandardizedServices(builder.Configuration);
#pragma warning disable ASP0000 // Calling 'BuildServiceProvider' from application code results in an additional copy of singleton services being created.
    using var tempProvider = tempServices.BuildServiceProvider();
#pragma warning restore ASP0000
    var cachingConfig = tempProvider.GetRequiredService<NickScanCentralImagingPortal.Services.Settings.CachingConfigurationProvider>();
    var config = await cachingConfig.GetConfigurationAsync();

    redisEnabled = config.RedisEnabled;
    redisConnection = config.RedisConnectionString;
    redisInstanceName = config.RedisInstanceName;
    responseCachingEnabled = config.ResponseCachingEnabled;
    maxResponseBodySizeMB = config.MaxResponseBodySizeMB;
    useCaseSensitivePaths = config.UseCaseSensitivePaths;

    Log.Information("✅ Caching configuration loaded from database:");
    Log.Information("   - Redis: {RedisEnabled} ({Connection})", redisEnabled ? "Enabled" : "Disabled", redisConnection);
    Log.Information("   - Response Caching: {ResponseCachingEnabled}", responseCachingEnabled ? "Enabled" : "Disabled");
    Log.Information("   - Cache Durations: Containers({Containers}s), Scans({Scans}s), ICUMS({ICUMS}s), Users({Users}s), Roles({Roles}s)",
        config.ContainersDurationSeconds, config.ScansDurationSeconds, config.ICUMSDurationSeconds,
        config.UsersDurationSeconds, config.RolesDurationSeconds);
}
catch (Exception ex)
{
    Log.Warning(ex, "⚠️ Could not read caching settings from database. Using default values. Error: {Error}", ex.Message);
    Log.Information("✅ Caching using defaults: Redis disabled, Response caching disabled");
}

// ✅ Configure Redis Distributed Cache
if (redisEnabled)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = redisInstanceName;
    });

    // Register cache service
    builder.Services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.ICacheService,
        NickScanCentralImagingPortal.Services.Caching.RedisCacheService>();

    Log.Information("✅ Redis distributed cache configured: {Connection}", redisConnection);
}
else
{
    // Fallback to in-memory cache
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.ICacheService,
        NickScanCentralImagingPortal.Services.Caching.RedisCacheService>();
    Log.Information("⚠️ Redis disabled - using in-memory cache");
}

// ✅ Add Response Caching (always registered so [ResponseCache] VaryByQueryKeys works)
builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = maxResponseBodySizeMB * 1024 * 1024;
    options.UseCaseSensitivePaths = useCaseSensitivePaths;
});
if (responseCachingEnabled)
{
    Log.Information("✅ Response caching enabled (MaxBodySize: {MaxSize}MB, CaseSensitive: {CaseSensitive})",
        maxResponseBodySizeMB, useCaseSensitivePaths);
}

// ✅ Add Response Compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "application/xml", "text/json", "text/xml" });
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

Log.Information("✅ Response compression configured (Brotli + Gzip)");

// ✅ Configure .NET 8 Built-in Rate Limiting (Modern & Native)
// ✅ PHASE 3: Now reads from database settings via RateLimitingConfigurationProvider
// NOTE: Rate limiting values are read from System Settings (Category: RateLimiting)
// Changes to rate limiting settings require API restart to take effect

// Read rate limiting settings from database (using temporary service provider)
var loginLimit = builder.Configuration.GetValue<int>("RateLimiting:Login:PermitLimit", 5);
var apiLimit = builder.Configuration.GetValue<int>("RateLimiting:API:PermitLimit", 500);
var dashboardLimit = builder.Configuration.GetValue<int>("RateLimiting:Dashboard:PermitLimit", 200);
var exportLimit = builder.Configuration.GetValue<int>("RateLimiting:Export:PermitLimit", 50);
var adminLimit = builder.Configuration.GetValue<int>("RateLimiting:Admin:PermitLimit", 1000);

try
{
    // Create a temporary service provider to read settings
    var tempServices = new ServiceCollection();
    // ✅ FIX: Explicitly add IConfiguration and IMemoryCache to temporary service collection
    tempServices.AddSingleton<IConfiguration>(builder.Configuration);
    tempServices.AddMemoryCache(); // Required by SettingsProvider
    tempServices.AddStandardizedServices(builder.Configuration);
#pragma warning disable ASP0000 // Calling 'BuildServiceProvider' from application code results in an additional copy of singleton services being created.
    using var tempProvider = tempServices.BuildServiceProvider();
#pragma warning restore ASP0000
    var tempRateLimitingConfig = tempProvider.GetRequiredService<NickScanCentralImagingPortal.Services.Settings.RateLimitingConfigurationProvider>();
    var config = await tempRateLimitingConfig.GetConfigurationAsync();

    loginLimit = config.LoginLimitPerMinute;
    apiLimit = config.ApiLimitPerMinute;
    dashboardLimit = config.DashboardLimitPerMinute;
    exportLimit = config.ExportLimitPerMinute;
    adminLimit = config.AdminLimitPerMinute;

    Log.Information("✅ Rate limiting values loaded from database: login({LoginLimit}/min), api({ApiLimit}/min), dashboard({DashboardLimit}/min), export({ExportLimit}/min), admin({AdminLimit}/min)",
        loginLimit, apiLimit, dashboardLimit, exportLimit, adminLimit);
}
catch (Exception ex)
{
    Log.Warning(ex, "⚠️ Could not read rate limiting settings from database. Using default values. Error: {Error}", ex.Message);
    Log.Information("✅ Rate limiting using defaults: login(5/min), api(500/min), dashboard(200/min), export(50/min), admin(1000/min)");
}

builder.Services.AddRateLimiter(options =>
{

    // Policy for login endpoint (strictest - prevents brute force)
    // Database setting: RateLimiting.Login.PerMinute (default: 5)
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = loginLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0; // No queuing
    });

    // Policy for general API endpoints
    // Database setting: RateLimiting.API.PerMinute (default: 500)
    options.AddSlidingWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = apiLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.SegmentsPerWindow = 6; // 10-second segments
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Policy for dashboard endpoints
    // Database setting: RateLimiting.Dashboard.PerMinute (default: 200)
    options.AddSlidingWindowLimiter("dashboard", limiterOptions =>
    {
        limiterOptions.PermitLimit = dashboardLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.SegmentsPerWindow = 6;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Policy for export/bulk operations
    // Database setting: RateLimiting.Export.PerMinute (default: 50)
    options.AddSlidingWindowLimiter("export", limiterOptions =>
    {
        limiterOptions.PermitLimit = exportLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.SegmentsPerWindow = 6;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Policy for admin operations
    // Database setting: RateLimiting.Admin.PerMinute (default: 1000)
    options.AddSlidingWindowLimiter("admin", limiterOptions =>
    {
        limiterOptions.PermitLimit = adminLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.SegmentsPerWindow = 6;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Policy for health checks (no limit)
    options.AddPolicy("health", context => RateLimitPartition.GetNoLimiter("health"));

    // Handle rejected requests
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = "60";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata))
        {
            retryAfter = ((int)retryAfterMetadata.TotalSeconds).ToString();
        }

        context.HttpContext.Response.Headers.RetryAfter = retryAfter;

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            message = $"Too many requests. Please retry after {retryAfter} seconds.",
            retryAfter = retryAfter,
            statusCode = 429
        }, cancellationToken: cancellationToken);

        Log.Warning("⚠️ Rate limit exceeded for IP: {IP}, Path: {Path}",
            context.HttpContext.Connection.RemoteIpAddress,
            context.HttpContext.Request.Path);
    };
});

Log.Information("✅ Rate limiting configured (will read actual values from database after startup)");

// Add regular MemoryCache for application use with SIZE LIMITS to prevent memory bloat
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1000; // Limit to 1000 cache entries (prevents unbounded growth)
    options.CompactionPercentage = 0.25; // Remove 25% of entries when limit is reached
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5); // Scan for expired entries every 5 min
});

Log.Information("✅ .NET 8 built-in rate limiting configured (100 req/min sliding window, 5 req/min login)");

// ✅ Configure CORS from appsettings with environment-aware settings
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? new[] { "http://localhost:5000", "https://localhost:5001" };
var corsPolicyName = builder.Configuration["Cors:PolicyName"] ?? "NickScanCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: Allow configured origins
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  .SetPreflightMaxAge(TimeSpan.FromSeconds(3600));

            Log.Information("✅ CORS configured for DEVELOPMENT with {Count} origins", corsOrigins.Length);
        }
        else
        {
            // Production: Strict CORS policy - only explicitly configured origins
            policy.WithOrigins(corsOrigins)
                  .WithMethods(builder.Configuration.GetSection("Cors:AllowedMethods").Get<string[]>()
                              ?? new[] { "GET", "POST", "PUT", "DELETE" })
                  .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With")
                  .AllowCredentials()
                  .SetPreflightMaxAge(TimeSpan.FromSeconds(3600));

            Log.Information("✅ CORS configured for PRODUCTION with {Count} origins", corsOrigins.Length);
        }
    });
});

// ✅ Configure request size limits
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    var maxSize = builder.Configuration.GetValue<long>("Security:MaxRequestBodySize", 104857600); // 100MB default
    options.MultipartBodyLengthLimit = maxSize;
});

builder.WebHost.ConfigureKestrel(options =>
{
    var maxSize = builder.Configuration.GetValue<long>("Security:MaxRequestBodySize", 104857600);
    options.Limits.MaxRequestBodySize = maxSize;
    Log.Information("✅ Request body size limit: {Size} MB", maxSize / 1024 / 1024);

    // ✅ SSL/TLS Configuration - Only enable HTTPS if certificate is available
    // Prevents "address already in use" errors when HTTPS isn't needed
    var certConfig = builder.Configuration.GetSection("SslCertificates:ApiCertificate");
    var certSource = certConfig["Source"] ?? "Store";

    X509Certificate2? serverCertificate = null;

    if (certSource == "Store")
    {
        // Load certificate from Windows Certificate Store
        var storeLocation = certConfig["StoreLocation"] == "LocalMachine"
            ? StoreLocation.LocalMachine
            : StoreLocation.CurrentUser;
        var storeName = Enum.Parse<StoreName>(certConfig["StoreName"] ?? "My");
        var thumbprint = Environment.GetEnvironmentVariable("NICKSCAN_API_CERT_THUMBPRINT");

        if (!string.IsNullOrEmpty(thumbprint))
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (certs.Count > 0)
            {
                serverCertificate = certs[0];
                Log.Information("✅ SSL Certificate loaded from store: {Subject}", serverCertificate.Subject);
            }
            else
            {
                Log.Warning("⚠️ SSL Certificate not found in store with thumbprint: {Thumbprint}", thumbprint);
            }
        }
        else
        {
            Log.Warning("⚠️ NICKSCAN_API_CERT_THUMBPRINT environment variable not set");
        }
    }
    else if (certSource == "File")
    {
        // Load certificate from file
        var certPath = certConfig["File:Path"];
        var certPassword = Environment.GetEnvironmentVariable("NICKSCAN_API_CERT_PASSWORD");

        if (System.IO.File.Exists(certPath) && !string.IsNullOrEmpty(certPassword))
        {
            serverCertificate = new X509Certificate2(certPath, certPassword);
            Log.Information("✅ SSL Certificate loaded from file: {Path}", certPath);
        }
        else
        {
            Log.Warning("⚠️ SSL Certificate file not found or password not set: {Path}", certPath);
        }
    }

    // Configure HTTPS endpoint ONLY if certificate is available
    // This prevents "address already in use" errors when HTTPS isn't needed
    if (serverCertificate != null)
    {
        var httpsUrl = builder.Configuration["Kestrel:Endpoints:Https:Url"] ?? "https://0.0.0.0:5206";
        var portMatch = System.Text.RegularExpressions.Regex.Match(httpsUrl, @":(\d+)");
        if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var httpsPort))
        {
            // Use configured certificate
            options.Listen(IPAddress.Any, httpsPort, listenOptions =>
            {
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = serverCertificate;
                    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    Log.Information("✅ HTTPS endpoint configured on port {Port} with TLS 1.2/1.3 and certificate", httpsPort);
                });
            });
        }
    }
    else
    {
        Log.Information("⚠️ HTTPS endpoint not configured - no certificate available. Using HTTP only on port 5205.");
    }
});

var app = builder.Build();

// ✅ PHASE 3: Read caching configuration from database for middleware
var responseCachingEnabledForMiddleware = false;
try
{
    using var scope = app.Services.CreateScope();
    var cachingConfig = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Services.Settings.CachingConfigurationProvider>();
    var cachingConfigValues = await cachingConfig.GetConfigurationAsync();
    responseCachingEnabledForMiddleware = cachingConfigValues.ResponseCachingEnabled;
}
catch (Exception ex)
{
    Log.Warning(ex, "⚠️ Could not read caching settings for middleware. Using default (disabled). Error: {Error}", ex.Message);
}

// ✅ PHASE 3: Read and log rate limiting configuration from database
try
{
    using var scope = app.Services.CreateScope();
    var rateLimitingConfig = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Services.Settings.RateLimitingConfigurationProvider>();
    var config = await rateLimitingConfig.GetConfigurationAsync();

    Log.Information("✅ Rate limiting configured from database settings:");
    Log.Information("   - Login: {LoginLimit}/min", config.LoginLimitPerMinute);
    Log.Information("   - API: {ApiLimit}/min", config.ApiLimitPerMinute);
    Log.Information("   - Dashboard: {DashboardLimit}/min", config.DashboardLimitPerMinute);
    Log.Information("   - Export: {ExportLimit}/min", config.ExportLimitPerMinute);
    Log.Information("   - Admin: {AdminLimit}/min", config.AdminLimitPerMinute);
    Log.Information("   ⚠️ Note: Rate limiting values are configured at startup. Changes require API restart.");
}
catch (Exception ex)
{
    Log.Warning(ex, "⚠️ Could not read rate limiting settings from database. Using default values. Error: {Error}", ex.Message);
    Log.Information("✅ Rate limiting using default values: login(5/min), api(500/min), dashboard(200/min), export(50/min), admin(1000/min)");
}

// ✅ CRITICAL: Correlation ID must be first for proper request tracking
app.UseCorrelationId();
Log.Information("✅ Correlation ID middleware enabled");

// ✅ Global Exception Handler (after correlation ID, before everything else)
app.UseGlobalExceptionHandler();
Log.Information("✅ Global exception handler enabled");

// ✅ Performance Logging (track all request performance)
if (builder.Configuration.GetValue<bool>("Performance:EnablePerformanceLogging", true))
{
    app.UsePerformanceLogging();
    Log.Information("✅ Performance logging middleware enabled");
}

// ✅ Performance Metrics Collection
app.UsePerformanceMetrics();
Log.Information("✅ Performance metrics collection enabled");

// ✅ Response Compression Middleware (MUST be before Response Caching)
// Exclude container image endpoint to avoid Content-Length mismatch: images are already compressed (JPEG/PNG),
// and compressing them can change byte count vs declared Content-Length and cause "too many bytes written" errors.
app.UseWhen(
    context =>
    {
        var path = context.Request.Path.Value ?? "";
        bool isContainerImageEndpoint = path.Contains("/api/ImageProcessing/container/", StringComparison.OrdinalIgnoreCase)
            && path.Contains("/complete/image", StringComparison.OrdinalIgnoreCase);
        return !isContainerImageEndpoint;
    },
    branch => branch.UseResponseCompression());
Log.Information("✅ Response compression middleware enabled (excludes container image endpoint)");

// ✅ PHASE 3: Response Caching Middleware (always registered so [ResponseCache] VaryByQueryKeys works)
app.UseResponseCaching();
if (responseCachingEnabledForMiddleware)
{
    Log.Information("✅ Response caching middleware enabled");
}
else
{
    Log.Information("ℹ️ Response caching middleware registered (caching disabled in settings)");
}

// ✅ Add Security Headers Middleware
app.Use(async (context, next) =>
{
    // HSTS (HTTP Strict Transport Security)
    if (builder.Configuration.GetValue<bool>("Security:EnableHsts", true) && !app.Environment.IsDevelopment())
    {
        var hstsMaxAge = builder.Configuration.GetValue<int>("Security:HstsMaxAgeSeconds", 31536000);
        context.Response.Headers["Strict-Transport-Security"] = $"max-age={hstsMaxAge}; includeSubDomains";
    }

    // X-Content-Type-Options
    if (builder.Configuration.GetValue<bool>("Security:EnableXContentTypeOptions", true))
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    // X-Frame-Options
    if (builder.Configuration.GetValue<bool>("Security:EnableXFrameOptions", true))
    {
        var xFrameOptions = builder.Configuration["Security:XFrameOptions"] ?? "DENY";
        context.Response.Headers["X-Frame-Options"] = xFrameOptions;
    }

    // X-XSS-Protection
    if (builder.Configuration.GetValue<bool>("Security:EnableXXSSProtection", true))
    {
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    }

    // Referrer-Policy
    if (builder.Configuration.GetValue<bool>("Security:EnableReferrerPolicy", true))
    {
        var referrerPolicy = builder.Configuration["Security:ReferrerPolicy"] ?? "strict-origin-when-cross-origin";
        context.Response.Headers["Referrer-Policy"] = referrerPolicy;
    }

    // Content-Security-Policy (CSP) - Basic policy
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: https:; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline';";

    // Permissions-Policy (formerly Feature-Policy)
    context.Response.Headers["Permissions-Policy"] =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    await next();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    Log.Information("✅ Swagger UI enabled at /swagger");
}
else
{
    if (builder.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
    {
        app.UseHsts();
        Log.Information("✅ HSTS enabled for production");
    }
}

// ✅ HTTPS Redirection - Only enable if HTTPS is actually configured
// Disable in development or when using HTTP only to prevent redirect loops
// Check if HTTPS endpoint is configured in appsettings.json
var hasHttpsEndpoint = builder.Configuration.GetSection("Kestrel:Endpoints:Https").Exists();
if (hasHttpsEndpoint && !app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    Log.Information("✅ HTTPS redirection enabled");
}
else
{
    // Disable HTTPS redirection in development or when using HTTP only
    // This prevents HealthChecks.UI from trying to connect via HTTPS when only HTTP is available
    Log.Information("⚠️ HTTPS redirection disabled - using HTTP only or development mode");
}

// ✅ .NET 8 Built-in Rate Limiting (MUST be before endpoints/controllers)
app.UseRateLimiter();
Log.Information("✅ Rate limiting middleware enabled (.NET 8 built-in)");

// ✅ CRITICAL: CORS must be before Authentication/Authorization
app.UseCors(corsPolicyName);
Log.Information("✅ CORS policy '{PolicyName}' applied", corsPolicyName);

// ✅ CRITICAL: Routing must be before authentication/authorization
app.UseRouting();
Log.Information("✅ Routing middleware enabled");

// ✅ CRITICAL: Add authentication BEFORE authorization
app.UseAuthentication(); // Authenticates the user (validates JWT token)
Log.Information("✅ Authentication middleware enabled");

// ✅ SECURITY: HMAC-signed short-lived URL auth for image-serving endpoints that
// browser <img src>/cross-origin fetch cannot carry a Bearer or SameSite=Strict
// cookie on. MUST run AFTER UseAuthentication (so we only act on requests the
// normal schemes didn't claim) and BEFORE UseAuthorization (so the [Authorize]
// filter sees a populated User principal). See SignedImageUrlMiddleware.
app.UseMiddleware<NickScanCentralImagingPortal.API.Middleware.SignedImageUrlMiddleware>();
Log.Information("✅ Signed-image-URL middleware enabled");

app.UseAuthorization();  // Authorizes the user (checks permissions)
Log.Information("✅ Authorization middleware enabled");

// NICKSCAN ERP — Phase 1 multi-tenancy: resolve tenant from JWT claim.
// Must run AFTER authentication so the User principal is populated.
app.UseNickERPTenancy();
Log.Information("✅ NickERP tenancy middleware enabled");

app.MapControllers();

// ✅ Map Health Check Endpoints (no rate limiting)
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    // HealthChecks UI expects the default UIResponseWriter JSON format
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous().DisableRateLimiting();

// Liveness probe (simple check, no rate limiting)
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // No checks, just returns 200 if app is running
}).DisableRateLimiting();

// Readiness probe (all dependencies must be ready, no rate limiting)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("database") || check.Tags.Contains("ready")
}).DisableRateLimiting();

// Health Check UI
app.MapHealthChecksUI(options =>
{
    options.UIPath = "/health-ui";
    options.ApiPath = "/health-api";
});

Log.Information("✅ Health check endpoints configured: /health, /health/live, /health/ready, /health-ui");

app.MapHub<NickScanCentralImagingPortal.API.Hubs.DashboardHub>("/hubs/dashboard");
app.MapHub<NickScanCentralImagingPortal.API.Hubs.ComprehensiveDashboardHub>("/hubs/comprehensive-dashboard");
app.MapHub<NickScanCentralImagingPortal.API.Hubs.ImageAnalysisDashboardHub>("/hubs/imageAnalysisDashboard");
app.MapHub<NickScanCentralImagingPortal.API.Hubs.UserReadinessHub>("/hubs/userReadiness");
app.MapHub<NickScanCentralImagingPortal.API.Hubs.ContainerScanQueueHub>("/hubs/containerScanQueue");

// Configure comprehensive monitoring
app.UseComprehensiveMonitoring();

// ✅ Guarantee the SuperAdmin account is healthy before continuing startup
await SuperAdminGuard.EnsureAsync(app.Services, app.Configuration);

// ✅ Auto-run PermissionSeeder on startup to ensure permissions and roles are initialized
Log.Information("🔧 Running PermissionSeeder to initialize permissions and roles...");
try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Infrastructure.Data.ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NickScanCentralImagingPortal.Infrastructure.Data.PermissionSeeder>>();
        var seeder = new NickScanCentralImagingPortal.Infrastructure.Data.PermissionSeeder(context, logger);
        await seeder.SeedAsync();
        Log.Information("✅ PermissionSeeder completed successfully");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "⚠️ PermissionSeeder failed - permissions may not be initialized. Error: {Message}", ex.Message);
    // Don't throw - allow app to continue (permissions might already exist)
}

// Purge stale ASE image cache entries (thumbnails cached by older conversion logic)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var cacheService = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Services.ImageProcessing.IImageCacheService>();
        var purged = await cacheService.PurgeStaleEntriesAsync(minSizeBytes: 10000, minWidth: 100, minHeight: 100);
        if (purged > 0)
            Log.Information("🧹 Purged {Count} stale ASE image cache entries at startup", purged);
    }
}
catch (Exception ex)
{
    Log.Warning(ex, "⚠️ Failed to purge stale image cache entries — will be handled on next request");
}

Log.Information(" Application starting up...");

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, " Application failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

#pragma warning disable CS1587 // XML comment is not placed on a valid language element
/// <summary>
/// Normalize connection strings to use TCP/IP and apply environment-specific settings
/// Fixes "Named Pipes Provider, error: 40" by forcing TCP/IP connections
/// </summary>
#pragma warning restore CS1587
static void NormalizeConnectionStrings(IConfiguration configuration, IWebHostEnvironment environment)
{
    var connectionStringKeys = new[]
    {
        "ConnectionStrings:NS_CIS_Connection",
        "ConnectionStrings:ICUMS_Connection",
        "ConnectionStrings:ICUMS_Downloads_Connection"
    };

    Console.WriteLine("✅ Validating PostgreSQL connection strings...");

    foreach (var connStringKey in connectionStringKeys)
    {
        var connString = configuration[connStringKey];
        if (string.IsNullOrEmpty(connString))
        {
            Console.WriteLine($"   ⚠️ {connStringKey.Split(':').Last()} is empty — skipping");
            continue;
        }

        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connString);
            var keyName = connStringKey.Split(':').Last();
            Console.WriteLine($"   ✅ {keyName}: Host={builder.Host}, Port={builder.Port}, Database={builder.Database}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ {connStringKey.Split(':').Last()} is malformed: {ex.Message}");
        }
    }
}

#pragma warning disable CS1587 // XML comment is not placed on a valid language element
/// <summary>
/// Replace credential placeholders in configuration with environment variables
/// This prevents hardcoding sensitive credentials in appsettings.json
/// </summary>
#pragma warning restore CS1587
static void ReplaceCredentialsWithEnvironmentVariables(IConfiguration configuration)
{
    const string placeholder = "***USE_ENV_VAR_";

    // PostgreSQL Database Password
    var dbPassword = Environment.GetEnvironmentVariable("NICKSCAN_DB_PASSWORD");
    if (!string.IsNullOrEmpty(dbPassword))
    {
        foreach (var key in new[] { "NS_CIS_Connection", "ICUMS_Connection", "ICUMS_Downloads_Connection" })
        {
            var connString = configuration[$"ConnectionStrings:{key}"];
            if (!string.IsNullOrEmpty(connString) && connString.Contains("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***"))
            {
                configuration[$"ConnectionStrings:{key}"] = connString.Replace("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***", dbPassword);
            }
        }
        Log.Information("PostgreSQL database password loaded from environment variable (password length: {Length})", dbPassword.Length);
    }
    else
    {
        Log.Warning("NICKSCAN_DB_PASSWORD not set — PostgreSQL connections will fail");
    }

    // ASE Database Password
    var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
    if (!string.IsNullOrEmpty(asePassword))
    {
        var aseConnString = configuration["ASE:ConnectionString"];
        if (!string.IsNullOrEmpty(aseConnString))
        {
            aseConnString = aseConnString.Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", asePassword);
            configuration["ASE:ConnectionString"] = aseConnString;
        }
        Log.Information("ASE database password loaded from environment variable (password length: {Length})", asePassword.Length);
    }
    else
    {
        Log.Warning("NICKSCAN_ASE_PASSWORD not set — ASE connections will fail");
    }

    // FS6000 Network Share Password
    var fs6000Password = Environment.GetEnvironmentVariable("NICKSCAN_FS6000_NETWORK_PASSWORD");
    if (!string.IsNullOrEmpty(fs6000Password))
    {
        var current = configuration["FS6000:FileSync:NetworkSharePassword"];
        if (!string.IsNullOrEmpty(current) && current.Contains(placeholder))
        {
            configuration["FS6000:FileSync:NetworkSharePassword"] = fs6000Password;
        }
        Log.Information("FS6000 network share password loaded from environment variable");
    }
    else
    {
        Log.Warning("NICKSCAN_FS6000_NETWORK_PASSWORD not set — FS6000 file sync will fail");
    }

    // ICUMS Auth Keys
    var icumsAuthKey = Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY");
    if (!string.IsNullOrEmpty(icumsAuthKey))
    {
        configuration["ICUMS:AuthKey"] = icumsAuthKey;
    }
    else
    {
        Log.Warning("NICKSCAN_ICUMS_AUTH_KEY not set — ICUMS API calls will fail");
    }

    var icumsDocsAuthKey = Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_DOCS_AUTH_KEY");
    if (!string.IsNullOrEmpty(icumsDocsAuthKey))
    {
        configuration["ICUMS:DocumentsAuthKey"] = icumsDocsAuthKey;
    }
    else
    {
        Log.Warning("NICKSCAN_ICUMS_DOCS_AUTH_KEY not set — ICUMS document API calls will fail");
    }

    var icumsJsonAuthKey = Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_JSON_AUTH_KEY");
    if (!string.IsNullOrEmpty(icumsJsonAuthKey))
    {
        configuration["ICUMS:JsonDocumentsAuthKey"] = icumsJsonAuthKey;
    }
    else
    {
        Log.Warning("NICKSCAN_ICUMS_JSON_AUTH_KEY not set — ICUMS JSON document API calls will fail");
    }

    Log.Information("Credential environment variable substitution complete");
}

// Expose Program as a public partial class so WebApplicationFactory<Program>
// in the test project can reach it. .NET 6+ minimal hosting makes Program
// implicitly internal, which breaks integration test fixtures otherwise.
public partial class Program { }
