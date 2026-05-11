using System;
using System.Collections.Generic;
using System.Linq;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public static class SplitAnalysisStatus
    {
        public const string Pending = "Pending";
        public const string Ready = "Ready";
        public const string Chosen = "Chosen";
        public const string Skipped = "Skipped";
        public const string NotApplicable = "NotApplicable";
        public const string VisualSingle = "VisualSingle";
        public const string Uncertain = "Uncertain";

        public static string ResolveForAnalysisRecord(
            SplitJobStatus? jobStatus,
            int fetchedCandidateCount = 0,
            bool candidateFetchAttempted = false,
            IEnumerable<string?>? candidateOutcomes = null)
        {
            var explicitNonChoice = TryMapNonChoiceOutcome(
                (candidateOutcomes ?? Array.Empty<string?>())
                .Concat(new[]
                {
                    jobStatus?.SplitOutcome,
                    jobStatus?.Status,
                    jobStatus?.BestStrategy,
                    jobStatus?.ErrorMessage
                }));

            if (explicitNonChoice != null)
                return explicitNonChoice;

            if (jobStatus == null)
                return Pending;

            if (IsFailedJobStatus(jobStatus.Status))
                return Skipped;

            if (!IsCompletedJobStatus(jobStatus.Status))
                return Pending;

            if (jobStatus.ResultCount <= 0)
                return Uncertain;

            if (candidateFetchAttempted && fetchedCandidateCount <= 0)
                return Pending;

            if (!jobStatus.SplitX.HasValue && fetchedCandidateCount <= 0)
                return Uncertain;

            return Ready;
        }

        public static bool IsCompletedJobStatus(string? status) =>
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

        public static bool IsFailedJobStatus(string? status) =>
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

        public static bool IsTerminalNonChoice(string? splitStatus) =>
            string.Equals(splitStatus, Skipped, StringComparison.OrdinalIgnoreCase)
            || string.Equals(splitStatus, NotApplicable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(splitStatus, VisualSingle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(splitStatus, Uncertain, StringComparison.OrdinalIgnoreCase);

        public static bool ShouldPreserveExistingResolution(string? splitStatus, Guid? existingJobId, Guid incomingJobId)
        {
            if (string.Equals(splitStatus, Chosen, StringComparison.OrdinalIgnoreCase))
                return true;

            return existingJobId == incomingJobId && IsTerminalNonChoice(splitStatus);
        }

        public static string? TryMapNonChoiceOutcome(IEnumerable<string?> values)
        {
            foreach (var value in values)
            {
                var normalized = Normalize(value);
                if (normalized.Length == 0)
                    continue;

                if (normalized is "notapplicable" or "na" or "notsplittable" or "nosplitneeded")
                    return NotApplicable;

                if (normalized is "visualsingle" or "single" or "singlecontainer" or "visuallysingle")
                    return VisualSingle;

                if (normalized is "uncertain" or "ambiguous" or "lowconfidence" or "inconclusive" or "nosplitdetected")
                    return Uncertain;

                if (normalized.Contains("visualsingle", StringComparison.Ordinal)
                    || normalized.Contains("singlecontainer", StringComparison.Ordinal)
                    || normalized.Contains("visuallysingle", StringComparison.Ordinal))
                {
                    return VisualSingle;
                }

                if (normalized.Contains("notapplicable", StringComparison.Ordinal)
                    || normalized.Contains("notsplittable", StringComparison.Ordinal)
                    || normalized.Contains("nosplitneeded", StringComparison.Ordinal))
                {
                    return NotApplicable;
                }

                if (normalized.Contains("uncertain", StringComparison.Ordinal)
                    || normalized.Contains("ambiguous", StringComparison.Ordinal)
                    || normalized.Contains("lowconfidence", StringComparison.Ordinal)
                    || normalized.Contains("inconclusive", StringComparison.Ordinal)
                    || normalized.Contains("nosplitdetected", StringComparison.Ordinal))
                {
                    return Uncertain;
                }
            }

            return null;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }
    }
}
