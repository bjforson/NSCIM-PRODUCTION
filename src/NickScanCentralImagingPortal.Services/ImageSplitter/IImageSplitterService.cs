using System;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public record SplitJobReference(Guid JobId, string Status);
    public record SplitJobStatus(Guid JobId, string Status, string? BestStrategy, double? BestConfidence, int? SplitX, int ResultCount);

    public interface IImageSplitterService
    {
        Task<SplitJobReference?> SubmitSplitJobAsync(string containerNumbers, byte[] imageData, Guid? sourceImageId = null, string? scannerType = null, CancellationToken cancellationToken = default);
        Task<SplitJobStatus?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    }
}
