using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Services.ImageSplitter;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Shared contract for deciding whether an analyst/audit decision is safe to
    /// bind to a split-aware analysis record.
    /// </summary>
    public static class SplitDecisionEligibility
    {
        public static bool RequiresSplitResolution(AnalysisRecord? record)
        {
            if (record?.IsMultiContainerScan != true)
                return false;

            return !IsResolvedForDecision(record);
        }

        public static bool IsResolvedForDecision(AnalysisRecord record)
        {
            if (!record.IsMultiContainerScan)
                return true;

            if (string.Equals(record.SplitStatus, SplitAnalysisStatus.Chosen, StringComparison.OrdinalIgnoreCase))
            {
                return record.SplitJobId.HasValue && record.SplitResultId.HasValue;
            }

            return SplitAnalysisStatus.IsTerminalNonChoice(record.SplitStatus);
        }

        public static bool IsDecisionCompatible(AnalysisRecord? record, ImageAnalysisDecision? decision)
        {
            if (record == null || decision == null)
                return false;

            if (!record.IsMultiContainerScan)
                return true;

            if (!IsResolvedForDecision(record))
                return false;

            if (string.Equals(record.SplitStatus, SplitAnalysisStatus.Chosen, StringComparison.OrdinalIgnoreCase))
            {
                return decision.SplitJobId == record.SplitJobId
                    && decision.SplitResultId == record.SplitResultId;
            }

            if (SplitAnalysisStatus.IsTerminalNonChoice(record.SplitStatus))
            {
                return !decision.SplitResultId.HasValue
                    && (!decision.SplitJobId.HasValue
                        || !record.SplitJobId.HasValue
                        || decision.SplitJobId == record.SplitJobId);
            }

            return false;
        }

        public static string DescribeUnresolvedSplit(AnalysisRecord record)
        {
            var status = string.IsNullOrWhiteSpace(record.SplitStatus)
                ? "missing"
                : record.SplitStatus;

            return $"split status is {status} with job {record.SplitJobId?.ToString() ?? "none"} and no audit-safe split result";
        }
    }
}
