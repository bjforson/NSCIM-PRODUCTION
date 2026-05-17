using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.Monitoring;

public static class EndpointRouteUsageCatalog
{
    private const string DeprecatedCategory = "Deprecated";
    private const string Phase3Category = "Phase3";

    private static readonly IReadOnlyList<EndpointRouteUsageDefinition> DeprecatedEndpointDefinitions =
        new List<EndpointRouteUsageDefinition>
    {
        new()
        {
            Pattern = "/api/ImageProcessing/image/",
            Category = DeprecatedCategory,
            CanonicalReplacement = "/api/scan-assets/{sourceScanId}/image",
            Owner = "Scan asset identity",
            Reason = "Legacy image-address route; source-scan identity is the canonical image contract.",
            DeprecatedOnUtc = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
            SafeRemovalAfterDays = 30
        },
        new()
        {
            Pattern = "/api/ImageProcessing/container/",
            Category = DeprecatedCategory,
            CanonicalReplacement = "/api/scan-assets/{sourceScanId}/image",
            Owner = "Scan asset identity",
            Reason = "Container-number image addressing is retained as a compatibility bridge while UI code moves to source-scan identities.",
            DeprecatedOnUtc = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
            SafeRemovalAfterDays = 30
        },
        new()
        {
            Pattern = "/api/image/",
            Category = DeprecatedCategory,
            CanonicalReplacement = "/api/scan-assets/{sourceScanId}/image",
            Owner = "Scan asset identity",
            Reason = "Old image controller alias; compatibility usage must drain before removal.",
            DeprecatedOnUtc = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
            SafeRemovalAfterDays = 30
        }
    };

    private static readonly IReadOnlyList<EndpointRouteUsageDefinition> Phase3RouteDefinitions =
        new List<EndpointRouteUsageDefinition>
    {
        new()
        {
            Pattern = "/api/image-analysis-management/",
            Category = Phase3Category,
            CanonicalReplacement = "/api/image-analysis/* and /api/image-analysis-management/* by workflow",
            Owner = "Image analysis management",
            Reason = "Tracked as the Phase 3 image-analysis management surface during frontend route migration.",
            DeprecatedOnUtc = null,
            SafeRemovalAfterDays = 0
        }
    };

    public static IReadOnlyList<EndpointRouteUsageDefinition> DeprecatedEndpoints => DeprecatedEndpointDefinitions;

    public static IReadOnlyList<EndpointRouteUsageDefinition> Phase3Routes => Phase3RouteDefinitions;

    public static bool IsDeprecatedEndpoint(string? path)
    {
        return TryGetDeprecatedDefinition(path, out _);
    }

    public static bool IsPhase3Route(string? path)
    {
        return TryGetPhase3Definition(path, out _);
    }

    public static bool TryGetDeprecatedDefinition(string? path, out EndpointRouteUsageDefinition? definition)
    {
        return TryGetDefinition(path, DeprecatedEndpointDefinitions, out definition);
    }

    public static bool TryGetPhase3Definition(string? path, out EndpointRouteUsageDefinition? definition)
    {
        return TryGetDefinition(path, Phase3RouteDefinitions, out definition);
    }

    private static bool TryGetDefinition(
        string? path,
        IReadOnlyList<EndpointRouteUsageDefinition> definitions,
        out EndpointRouteUsageDefinition? definition)
    {
        var normalizedPath = NormalizeForMatch(path);
        definition = definitions.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.Pattern)
            && normalizedPath.StartsWith(NormalizeForMatch(d.Pattern), StringComparison.Ordinal));

        return definition is not null;
    }

    private static string NormalizeForMatch(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }
}
