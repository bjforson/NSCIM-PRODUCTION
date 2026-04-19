using System.Reflection;

namespace NickScanWebApp.New;

/// <summary>
/// Deployment info derived from assembly version. Check GET /api/server/version to verify.
/// Version is centralized in src/Directory.Build.props — bump there before each deploy.
/// </summary>
public static class DeploymentInfo
{
    public static string Version { get; } = CleanVersion(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown");

    private static string CleanVersion(string v) => v.Contains('+') ? v[..v.IndexOf('+')] : v;

    public static string Marker { get; } = $"webapp-v{Version}";
}
