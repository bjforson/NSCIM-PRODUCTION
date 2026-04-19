using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for submitting data back to ICUMS
    /// </summary>
    public interface IICUMSSubmissionService
    {
        /// <summary>
        /// Submits container data to ICUMS
        /// </summary>
        /// <param name="submissionData">Container submission data</param>
        /// <returns>Submission result</returns>
        Task<SubmissionResult> SubmitToICUMSAsync(ContainerSubmissionData submissionData);

        /// <summary>
        /// Processes all pending submissions in the queue
        /// </summary>
        /// <param name="stoppingToken">Cancellation token</param>
        Task ProcessSubmissionQueueAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Queues a container for submission
        /// </summary>
        /// <param name="submissionData">Container submission data</param>
        /// <param name="priority">Submission priority (1=Normal, 2=High, 3=Critical)</param>
        /// <param name="submittedBy">User or system initiating submission</param>
        /// <returns>Created submission queue item</returns>
        Task<ICUMSSubmissionQueue> QueueForSubmissionAsync(ContainerSubmissionData submissionData, int priority = 1, string? submittedBy = null);

        /// <summary>
        /// Gets submission statistics
        /// </summary>
        /// <returns>Submission statistics</returns>
        Task<SubmissionStatistics> GetSubmissionStatisticsAsync();

        /// <summary>
        /// Retries failed submissions
        /// </summary>
        /// <param name="maxRetries">Maximum number of retries</param>
        Task RetryFailedSubmissionsAsync(int maxRetries = 3);
    }
}
