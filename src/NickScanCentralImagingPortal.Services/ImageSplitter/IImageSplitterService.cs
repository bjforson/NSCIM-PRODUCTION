using System;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public record SplitJobReference(Guid JobId, string Status);
    public record SplitJobStatus(
        Guid JobId,
        string Status,
        string? BestStrategy,
        double? BestConfidence,
        int? SplitX,
        int ResultCount,
        string? SplitOutcome = null,
        string? ErrorMessage = null);
    public record SplitResultReference(Guid ResultId, string? StrategyName, double? Confidence, string? SplitOutcome = null);

    public interface IImageSplitterService
    {
        Task<SplitJobReference?> SubmitSplitJobAsync(string containerNumbers, byte[] imageData, Guid? sourceImageId = null, string? scannerType = null, CancellationToken cancellationToken = default);
        Task<SplitJobReference?> FindLatestJobByContainersAsync(string containerNumbers, CancellationToken cancellationToken = default);
        Task<SplitJobStatus?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SplitResultReference>> GetTopSplitResultsAsync(Guid jobId, int take = 2, CancellationToken cancellationToken = default);
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    }
}
