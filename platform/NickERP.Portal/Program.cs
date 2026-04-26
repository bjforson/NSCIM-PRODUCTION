using MudBlazor.Services;
using NickERP.Portal.Components;
using NickERP.Portal.Services;

// Single-instance guard so the service can't accidentally run twice.
using var mutex = new Mutex(true, @"Global\NickERP_Portal_SingleInstance", out var createdNew);
if (!createdNew)
{
    Console.WriteLine("NickERP Portal is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Windows Service support (ignored when run via `dotnet run`).
builder.Host.UseWindowsService();

// SECURITY: replace the ***USE_ENV_VAR_NICKSCAN_DB_PASSWORD*** placeholder in the
// NickHrDb connection string with the env-var value. Prior to this the password was
// hard-coded in appsettings.json and therefore in git — rotate it after deployment.
var dbPassword = Environment.GetEnvironmentVariable("NICKSCAN_DB_PASSWORD");
var nickHrConn = builder.Configuration.GetConnectionString("NickHrDb");
if (!string.IsNullOrEmpty(nickHrConn) && nickHrConn.Contains("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***"))
{
    if (string.IsNullOrEmpty(dbPassword))
    {
        throw new InvalidOperationException(
            "NICKSCAN_DB_PASSWORD environment variable is required — the appsettings.json " +
            "NickHrDb connection string still contains the placeholder.");
    }
    builder.Configuration["ConnectionStrings:NickHrDb"] =
        nickHrConn.Replace("***USE_ENV_VAR_NICKSCAN_DB_PASSWORD***", dbPassword);
}

// MudBlazor for visual consistency with NickHR.
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
});

// HTTP client factory (for StatsService to reach NSCIM API at localhost).
builder.Services.AddHttpClient();

// Stats aggregator.
builder.Services.AddScoped<StatsService>();

// Blazor Web with server interactivity.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
