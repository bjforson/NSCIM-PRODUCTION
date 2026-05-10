using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public record TwoContainerSplitEnsureResult(
        int OriginalScanRecordId,
        bool IsApplicable,
        bool SplitJobCreated,
        bool SplitJobFound,
        int LinkedAnalysisRecords,
        string Status);

    public record TwoContainerSplitSweepResult(
        int OriginalsScanned,
        int ApplicableOriginals,
        int JobsCreated,
        int JobsFound,
        int AnalysisRecordsLinked);

    public interface ITwoContainerSplitIntakeService
    {
        Task<TwoContainerSplitEnsureResult> EnsureSplitJobForOriginalAsync(
            int originalScanRecordId,
            CancellationToken cancellationToken = default);

        Task<TwoContainerSplitSweepResult> SweepAsync(
            int submitLimit = 25,
            int linkLimit = 100,
            CancellationToken cancellationToken = default);
    }
}
