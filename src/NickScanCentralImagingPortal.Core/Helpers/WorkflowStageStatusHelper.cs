using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Helpers;

/// <summary>
/// Single source of truth for deriving AnalysisGroup.Status from ContainerCompletenessStatus.WorkflowStage.
/// WorkflowStage is the canonical state; AnalysisGroup.Status is a cached/derived view that gets synced.
/// Use this helper everywhere that computes Status from WorkflowStage counts to ensure consistency.
/// </summary>
public static class WorkflowStageStatusHelper
{
    /// <summary>
    /// Compute the correct AnalysisGroup.Status from WorkflowStage distribution.
    /// </summary>
    /// <param name="totalContainers">Total containers in the group</param>
    /// <param name="imageAnalysisCount">Containers with WorkflowStage = 'ImageAnalysis'</param>
    /// <param name="auditCount">Containers with WorkflowStage = 'Audit'</param>
    /// <param name="completedCount">Containers with WorkflowStage = 'Completed'</param>
    /// <param name="pendingCount">Containers with WorkflowStage = 'Pending' or null</param>
    /// <returns>The correct Status. For unexpected distributions (null/unknown stages), returns Ready to avoid groups getting stuck.</returns>
    public static string? ComputeStatusFromWorkflowStage(
        int totalContainers,
        int imageAnalysisCount,
        int auditCount,
        int completedCount,
        int pendingCount)
    {
        if (totalContainers <= 0) return null;

        // All completed → Completed
        if (completedCount == totalContainers)
            return AnalysisStatuses.Completed;

        // All in Audit (or Audit+Completed, no ImageAnalysis) → AnalystCompleted
        if (auditCount == totalContainers ||
            (auditCount + completedCount == totalContainers && imageAnalysisCount == 0))
            return AnalysisStatuses.AnalystCompleted;

        // Some still in ImageAnalysis or Pending → Ready
        if (imageAnalysisCount > 0 || pendingCount > 0)
            return AnalysisStatuses.Ready;

        // Unexpected distribution (null/unknown WorkflowStage) → treat as Ready so groups don't get stuck
        return AnalysisStatuses.Ready;
    }

    /// <summary>
    /// Compute correct status for "stuck" groups (Assigned but lease expired, no active assignment).
    /// Considers both WorkflowStage and current group status.
    /// </summary>
    public static string? ComputeCorrectStatusForStuckGroup(
        int totalContainers,
        int imageAnalysisCount,
        int auditCount,
        int completedCount,
        string currentStatus)
    {
        if (totalContainers <= 0) return null;

        // All completed → Completed
        if (completedCount == totalContainers)
            return AnalysisStatuses.Completed;

        // AuditAssigned with no active assignment → revert to AnalystCompleted
        if (currentStatus == AnalysisStatuses.AuditAssigned)
            return AnalysisStatuses.AnalystCompleted;

        // All in Audit → AnalystCompleted
        if (auditCount == totalContainers ||
            (auditCount + completedCount == totalContainers && imageAnalysisCount == 0))
            return AnalysisStatuses.AnalystCompleted;

        // Still has ImageAnalysis → Ready
        if (imageAnalysisCount > 0)
            return AnalysisStatuses.Ready;

        return AnalysisStatuses.Ready; // Default fallback
    }
}
