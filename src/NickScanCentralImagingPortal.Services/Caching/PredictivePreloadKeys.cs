namespace NickScanCentralImagingPortal.Services.Caching;

public static class PredictivePreloadKeys
{
    public static string RoleAssignments(string role) => $"preload:role:{role}:assignments";
    public static string Assignment(Guid groupId) => $"preload:assignment:{groupId}";
    public static string AssignmentContainers(Guid groupId) => $"preload:assignment:{groupId}:containers";
    public static string ContainerContext(string containerNumber) => $"preload:container:{containerNumber}:context";
    public static string ContainerSummary(string containerNumber) => $"preload:container:{containerNumber}:summary";
    public static string ContainerScannerPage(string containerNumber, int page, int pageSize) =>
        $"preload:container:{containerNumber}:scanner:page:{page}:size:{pageSize}";
    public static string ContainerIcumsPage(string containerNumber, int page, int pageSize) =>
        $"preload:container:{containerNumber}:icums:page:{page}:size:{pageSize}";
    public static string ContainerBoe(string containerNumber) => $"preload:container:{containerNumber}:boe";
    public static string ContainerImageMetadata(string containerNumber) => $"preload:container:{containerNumber}:images:metadata";
    public static string CargoGroupSummary(string groupIdentifier) => $"preload:cargo-group:{groupIdentifier}:summary";
}
