namespace NickScanCentralImagingPortal.Core.Helpers;

public static class ContainerCompletenessPolicy
{
    public static ContainerCompletenessDecision Evaluate(
        bool hasScannerData,
        bool hasICUMSData,
        bool hasImageData,
        string? clearanceType,
        string? groupIdentifier)
    {
        var isCmrPending = string.Equals(clearanceType, "CMR", StringComparison.OrdinalIgnoreCase)
                           && string.IsNullOrWhiteSpace(groupIdentifier);
        var isComplete = hasScannerData && hasICUMSData && hasImageData && !isCmrPending;

        if (isComplete)
        {
            return new ContainerCompletenessDecision("Complete", "ImageAnalysis", true, false);
        }

        if (isCmrPending)
        {
            return new ContainerCompletenessDecision("AwaitingDeclaration", "Pending", false, true);
        }

        return new ContainerCompletenessDecision("Missing", "Pending", false, false);
    }
}

public sealed record ContainerCompletenessDecision(
    string Status,
    string WorkflowStage,
    bool IsComplete,
    bool IsAwaitingDeclaration);
