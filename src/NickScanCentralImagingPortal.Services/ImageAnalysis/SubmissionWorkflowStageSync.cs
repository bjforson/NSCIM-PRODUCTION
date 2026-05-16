using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.RecordCompleteness;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Keeps submission workflow state aligned when AnalysisGroup identifiers and
    /// ContainerCompletenessStatus identifiers drift apart.
    /// </summary>
    public static class SubmissionWorkflowStageSync
    {
        private static readonly HashSet<string> PendingSubmissionSourceStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "ImageAnalysis",
            "Audit",
            "Completed"
        };

        private static readonly HashSet<string> SubmittedSourceStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "ImageAnalysis",
            "Audit",
            "PendingSubmission",
            "Completed"
        };

        private static readonly HashSet<string> SubmittedFallbackSourceStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "PendingSubmission"
        };

        private static readonly HashSet<string> ProtectedSubmissionStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "Submitted",
            "SplitSuperseded"
        };

        public static async Task<int> MarkPendingSubmissionAsync(
            ApplicationDbContext db,
            AnalysisGroup group,
            IEnumerable<string>? additionalContainerNumbers,
            CancellationToken ct = default)
        {
            var rows = await LoadCandidateCompletenessRowsAsync(db, group, additionalContainerNumbers, ct);
            var nowUtc = DateTime.UtcNow;
            var updated = 0;

            foreach (var row in rows)
            {
                if (!CanMoveToStage(row.WorkflowStage, PendingSubmissionSourceStages))
                    continue;

                row.WorkflowStage = "PendingSubmission";
                row.UpdatedAt = nowUtc;
                updated++;
            }

            if (updated > 0)
                await db.SaveChangesAsync(ct);

            return updated;
        }

        public static async Task<int> MarkContainerSubmittedAsync(
            ApplicationDbContext db,
            string containerNumber,
            AnalysisGroup? group = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return 0;

            IReadOnlyList<ContainerCompletenessStatus> rows;
            if (group != null)
            {
                rows = await LoadCandidateCompletenessRowsAsync(db, group, new[] { containerNumber }, ct);
                rows = rows
                    .Where(r => string.Equals(r.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                rows = await db.ContainerCompletenessStatuses
                    .AsTracking()
                    .Where(c => c.ContainerNumber == containerNumber)
                    .ToListAsync(ct);
            }

            var eligibleStages = group != null ? SubmittedSourceStages : SubmittedFallbackSourceStages;
            var nowUtc = DateTime.UtcNow;
            var updated = 0;

            foreach (var row in rows)
            {
                if (!CanMoveToStage(row.WorkflowStage, eligibleStages))
                    continue;

                row.WorkflowStage = "Submitted";
                row.UpdatedAt = nowUtc;
                updated++;
            }

            if (updated > 0)
                await db.SaveChangesAsync(ct);

            var hasSubmittedCompletenessRow = rows.Any(r =>
                string.Equals(r.WorkflowStage, "Submitted", StringComparison.OrdinalIgnoreCase));

            if (updated > 0 || hasSubmittedCompletenessRow)
            {
                await RecordCompletenessRollupSync.MarkContainerSubmittedAsync(
                    db,
                    group,
                    containerNumber,
                    group?.ScannerType,
                    ct);
            }

            return updated;
        }

        public static IReadOnlyList<string> GetGroupIdentifierCandidates(AnalysisGroup group)
        {
            var normalized = !string.IsNullOrWhiteSpace(group.NormalizedGroupIdentifier)
                ? group.NormalizedGroupIdentifier
                : GroupIdentifierHelper.GetNormalizedGroupIdentifier(group.GroupIdentifier);

            return new[]
                {
                    group.GroupIdentifier,
                    group.NormalizedGroupIdentifier,
                    normalized
                }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<IReadOnlyList<ContainerCompletenessStatus>> LoadCandidateCompletenessRowsAsync(
            ApplicationDbContext db,
            AnalysisGroup group,
            IEnumerable<string>? additionalContainerNumbers,
            CancellationToken ct)
        {
            var groupIds = GetGroupIdentifierCandidates(group);
            var containers = await db.AnalysisRecords
                .AsNoTracking()
                .Where(r => r.GroupId == group.Id)
                .Select(r => r.ContainerNumber)
                .ToListAsync(ct);

            if (additionalContainerNumbers != null)
                containers.AddRange(additionalContainerNumbers);

            containers = containers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groupIds.Count == 0 && containers.Count == 0)
                return Array.Empty<ContainerCompletenessStatus>();

            var rows = await db.ContainerCompletenessStatuses
                .AsTracking()
                .Where(c =>
                    (c.GroupIdentifier != null && groupIds.Contains(c.GroupIdentifier)) ||
                    containers.Contains(c.ContainerNumber))
                .ToListAsync(ct);

            return rows
                .Where(r => ScannerMatches(r.ScannerType, group.ScannerType))
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .ToList();
        }

        private static bool CanMoveToStage(string? currentStage, HashSet<string> eligibleStages)
        {
            var stage = currentStage?.Trim() ?? "";
            return eligibleStages.Contains(stage) && !ProtectedSubmissionStages.Contains(stage);
        }

        private static bool ScannerMatches(string? candidate, string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
                return true;

            var candidateTrimmed = candidate.Trim();
            var requestedTrimmed = requested.Trim();
            var requestedBase = BaseScannerType(requestedTrimmed);

            return string.Equals(candidateTrimmed, requestedTrimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(requestedBase) &&
                    (string.Equals(candidateTrimmed, requestedBase, StringComparison.OrdinalIgnoreCase)
                     || candidateTrimmed.StartsWith(requestedBase + "-", StringComparison.OrdinalIgnoreCase)))
                || requestedTrimmed.StartsWith(candidateTrimmed + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static string? BaseScannerType(string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
                return null;

            var trimmed = scannerType.Trim();
            var dashIndex = trimmed.IndexOf('-');
            return dashIndex > 0 ? trimmed[..dashIndex] : trimmed;
        }
    }
}
