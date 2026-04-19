using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.MasterOrchestrator.Models;
using NickScanCentralImagingPortal.MasterOrchestrator.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Configure services
builder.Services.AddHttpClient();
builder.Services.Configure<MasterOrchestratorConfig>(
    builder.Configuration.GetSection("MasterOrchestrator"));
builder.Services.AddHostedService<MasterOrchestratorService>();

var host = builder.Build();

Console.WriteLine(" NickScan Central Imaging Portal - Master Orchestrator");
Console.WriteLine("========================================================");
Console.WriteLine();

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, " Master Orchestrator failed to start");
    throw;
}
finally
{
    Console.WriteLine();
    Console.WriteLine(" Master Orchestrator stopped");
}
