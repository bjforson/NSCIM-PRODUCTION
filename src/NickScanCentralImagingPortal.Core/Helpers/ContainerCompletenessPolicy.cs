namespace NickScanCentralImagingPortal.Core.Helpers;

public static class ContainerCompletenessPolicy
{
    public static ContainerCompletenessDecision Evaluate(
        bool hasScannerData,
        bool hasICUMSData,
        bool hasImageData,
        string? clearanceType,
        string? groupIdentifier,
        bool cmrCompositeProgressionEnabled = false,
        string? cmrRotationNumber = null,
        string? cmrContainerNumber = null,
        string? cmrBlNumber = null)
    {
        var isCmr = string.Equals(clearanceType, "CMR", StringComparison.OrdinalIgnoreCase);
        var hasCmrOperationalKey = !string.IsNullOrWhiteSpace(groupIdentifier)
            || (cmrCompositeProgressionEnabled
                && CmrCompositeKeyHelper.HasRequiredParts(cmrRotationNumber, cmrContainerNumber, cmrBlNumber));
        var isCmrPending = isCmr && !hasCmrOperationalKey;
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
