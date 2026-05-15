namespace NickScanCentralImagingPortal.Services.Caching;

public interface IPredictivePreloadService
{
    Task<PredictivePreloadRunResult> RunOnceAsync(CancellationToken cancellationToken = default);

    Task<PredictivePreloadAssignmentResult> PreloadAssignmentAsync(
        Guid groupId,
        string role,
        string eligibleStatus,
        CancellationToken cancellationToken = default);

    Task<PredictivePreloadContainerResult> PreloadContainerContextAsync(
        string containerNumber,
        CancellationToken cancellationToken = default);

    Task InvalidateAssignmentAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task InvalidateContainerContextAsync(string containerNumber, CancellationToken cancellationToken = default);

    Task InvalidateRoleAssignmentsAsync(string role, CancellationToken cancellationToken = default);

    Task<PredictiveAssignmentContext?> GetAssignmentContextAsync(
        Guid groupId,
        CancellationToken cancellationToken = default);

    Task<PredictiveContainerContext?> GetContainerContextAsync(
        string containerNumber,
        CancellationToken cancellationToken = default);
}
