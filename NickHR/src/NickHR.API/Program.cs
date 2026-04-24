using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NickHR.Infrastructure;
using NickHR.Infrastructure.Data;
using NickHR.Services;
using Serilog;

// Single instance enforcement
using var mutex = new Mutex(true, @"Global\NickHR_API_SingleInstance", out var createdNew);
if (!createdNew)
{
    Console.WriteLine("NickHR API is already running. Exiting.");
    return;
}

// Enable legacy timestamp behavior for Npgsql
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Windows Service support
builder.Host.UseWindowsService();

// Resolve DB password from environment variable
var dbPassword = Environment.GetEnvironmentVariable("NICKSCAN_DB_PASSWORD") ?? "postgres";
var connString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connString) && connString.Contains("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***"))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] =
        connString.Replace("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***", dbPassword);
}

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/nickhr-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Infrastructure (DbContext, Identity, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

// Application Services
builder.Services.AddApplicationServices(builder.Configuration);

// JWT Authentication
// SECURITY: JWT signing key MUST come from the NICKHR_JWT_KEY environment variable
// in production. Leaving it in appsettings.json is the "secrets-in-git" anti-pattern.
// If the env var is missing OR the appsettings placeholder is still in place, fail
// fast at startup rather than silently falling back to a committed-in-history key.
var jwtKey = Environment.GetEnvironmentVariable("NICKHR_JWT_KEY")
             ?? builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Contains("***USE_ENV_VAR"))
{
    throw new InvalidOperationException(
        "JWT signing key not configured. Set the NICKHR_JWT_KEY environment variable " +
        "to a strong random value (>= 32 chars). Example (PowerShell, machine-scoped): " +
        "[Environment]::SetEnvironmentVariable('NICKHR_JWT_KEY', <value>, 'Machine')");
}
if (jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        $"JWT signing key is {jwtKey.Length} chars; HS256 requires >= 32. Regenerate NICKHR_JWT_KEY.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// ✅ SECURITY: Deny-by-default. Every endpoint requires an authenticated user unless it
// explicitly opts in with [AllowAnonymous] (login, register, health). Closes the class
// of bugs where a controller forgot [Authorize] — see BiometricController history.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NickHR API",
        Version = "v1",
        Description = "NickHR Human Resource Management System API"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    // Swashbuckle 10.x + Microsoft.OpenApi 2.x: use OpenApiSecuritySchemeReference
    c.AddSecurityRequirement((document) => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("NickHRCors", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "https://localhost:5300" })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Seed data
await SeedData.InitializeAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("NickHRCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
