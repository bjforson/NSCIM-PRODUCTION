namespace NickScanCentralImagingPortal.Services.Caching;

public static class SystemCacheKeyRegistry
{
    public static class Prefixes
    {
        public const string PredictivePreload = "preload:";
        public const string ReadyGroups = "ReadyGroups";
        public const string ContainerDetails = "container-details:";
        public const string CargoGroup = "cargo-group:";
        public const string Icums = "icums:";
        public const string Scanner = "scanner:";
        public const string User = "user:";
        public const string Role = "role:";
    }

    public static string Container(string containerNumber, string suffix) =>
        Build(Prefixes.ContainerDetails, Normalize(containerNumber), suffix);

    public static string CargoGroupSummary(string groupIdentifier) =>
        Build(Prefixes.CargoGroup, Normalize(groupIdentifier), "summary");

    public static string Build(params string?[] parts)
    {
        return string.Join(":", parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim().Trim(':')));
    }

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}
