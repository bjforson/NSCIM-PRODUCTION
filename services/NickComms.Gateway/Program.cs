using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Endpoints;
using NickComms.Gateway.Entities;
using NickComms.Gateway.Security;
using NickComms.Gateway.Services;
using NickERP.Platform.Tenancy;
using Serilog;

// Single instance guard
using var instanceMutex = new Mutex(true, @"Global\NickComms_Gateway_SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    Console.Error.WriteLine("Another instance of NickComms Gateway is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// Fix: Remove EventLog logger added by UseWindowsService() — requires missing DLL
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Load environment variables with NICKCOMMS_ prefix
builder.Configuration.AddEnvironmentVariables("NICKCOMMS_");

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Bind options
builder.Services.Configure<HubtelOptions>(builder.Configuration.GetSection(HubtelOptions.SectionName));
builder.Services.Configure<SmsGatewayOptions>(builder.Configuration.GetSection(SmsGatewayOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

// NICKSCAN ERP — multi-tenancy plumbing (Phase 1)
// Registers ITenantContext + TenantOwnedEntityInterceptor in DI.
// Currently a no-op for existing entities (none implement ITenantOwned yet);
// the interceptor activates as entities opt in during later phases.
builder.Services.AddNickERPTenancy();

// Database
builder.Services.AddDbContext<CommsDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("CommsDb"));
    options.AddInterceptors(sp.GetRequiredService<NickERP.Platform.Tenancy.TenantOwnedEntityInterceptor>());
});

// Memory cache (for API key caching)
builder.Services.AddMemoryCache();

// Authentication
builder.Services.AddAuthentication(ApiKeyAuthOptions.SchemeName)
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthOptions.SchemeName, _ => { });
builder.Services.AddAuthorization();

// HttpClient for Hubtel
builder.Services.AddHttpClient("HubtelSms");
builder.Services.AddHttpClient("HubtelOtp");

// Services
builder.Services.AddScoped<IHubtelClient, HubtelClient>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Queue services (singleton — background services that hold the Channel)
builder.Services.AddSingleton<SmsQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SmsQueueService>());
builder.Services.AddSingleton<EmailQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailQueueService>());

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NickComms Gateway",
        Version = "v1",
        Description = "Centralized communications gateway — SMS (Hubtel), Email (SMTP), OTP"
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        Description = "API key for client app authentication"
    });
    // Swashbuckle 10.x + Microsoft.OpenApi 2.x: use OpenApiSecuritySchemeReference
    c.AddSecurityRequirement((document) => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("ApiKey", document),
            new List<string>()
        }
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("CommsDb") ?? "", name: "database");

var app = builder.Build();

// Auto-migrate and seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();
    await db.Database.MigrateAsync();
    await SeedApiKeysAsync(db, builder.Configuration);
}

// Middleware
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "NickComms Gateway v1"));

app.UseAuthentication();
app.UseAuthorization();

// NICKSCAN ERP — resolves tenant from JWT/API-key context. Must run AFTER auth.
// For NickComms, the X-Api-Key auth scheme doesn't carry a tenant_id claim
// so the middleware falls back to the default tenant (1) for now. In Phase 2
// we'll teach the API key scheme to carry the per-key tenant id.
app.UseNickERPTenancy();

// Map endpoints
app.MapSmsEndpoints();
app.MapEmailEndpoints();
app.MapOtpEndpoints();
app.MapHistoryEndpoints();

app.MapHealthChecks("/api/health");

app.MapGet("/", () => Results.Ok(new
{
    service = "NickComms Gateway",
    version = "1.0.0",
    status = "running",
    endpoints = new[]
    {
        "POST /api/sms/send",
        "POST /api/sms/bulk",
        "GET  /api/sms/{id}/status",
        "POST /api/email/send",
        "POST /api/email/bulk",
        "GET  /api/email/{id}/status",
        "POST /api/otp/send",
        "POST /api/otp/verify",
        "POST /api/otp/resend",
        "GET  /api/messages/history",
        "GET  /api/health"
    }
}))
.ExcludeFromDescription();

Log.Information("NickComms Gateway starting on port 5220");
app.Run();

// --- Helper: Seed API keys from config ---
static async Task SeedApiKeysAsync(CommsDbContext db, IConfiguration config)
{
    var clients = config.GetSection("ApiKeys:Clients").GetChildren();
    foreach (var client in clients)
    {
        var appName = client["AppName"];
        var key = client["Key"];
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(key))
            continue;

        var keyHash = ApiKeyAuthHandler.HashKey(key);
        var exists = await db.ApiKeys.AnyAsync(k => k.AppName == appName);
        if (!exists)
        {
            db.ApiKeys.Add(new ApiKey
            {
                AppName = appName,
                KeyHash = keyHash,
                KeyPrefix = key.Length >= 8 ? key[..8] : key,
                IsActive = true
            });
            Log.Information("Seeded API key for {AppName}", appName);
        }
    }
    await db.SaveChangesAsync();
}
