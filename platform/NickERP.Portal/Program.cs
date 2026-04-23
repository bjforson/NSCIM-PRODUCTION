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
