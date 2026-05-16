using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.RecordCompleteness
{
    /// <summary>
    /// Synchronizes the canonical record-completeness parent/child rows after
    /// image-analysis lifecycle events that happen outside the reconciliation worker.
    /// </summary>
    public static class RecordCompletenessRollupSync
    {
        public static Task<int> MarkContainerDecidedAsync(
            ApplicationDbContext db,
            AnalysisGroup? group,
            string containerNumber,
            string? scannerType,
            CancellationToken ct = default)
        {
            return MarkContainerAsync(db, group, containerNumber, scannerType, "Decided", ct);
        }

        public static Task<int> MarkContainerSubmittedAsync(
            ApplicationDbContext db,
            AnalysisGroup? group,
            string containerNumber,
            string? scannerType = null,
            CancellationToken ct = default)
        {
            return MarkContainerAsync(db, group, containerNumber, scannerType, "Submitted", ct);
        }

        private static async Task<int> MarkContainerAsync(
            ApplicationDbContext db,
            AnalysisGroup? group,
            string containerNumber,
            string? scannerType,
            string targetStatus,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return 0;

            var effectiveScannerType = scannerType ?? group?.ScannerType;
            var recordIds = await ResolveRecordIdsAsync(db, group, containerNumber, effectiveScannerType, ct);
            if (recordIds.Count == 0)
                return 0;

            var updatedChildren = 0;
            var recomputedRecords = 0;

            foreach (var recordId in recordIds)
            {
                var children = await db.RecordExpectedContainers
                    .AsTracking()
                    .Where(e => e.RecordId == recordId && e.ContainerNumber == containerNumber)
                    .ToListAsync(ct);

                if (!string.IsNullOrWhiteSpace(effectiveScannerType))
                {
                    children = children
                        .Where(e => ScannerMatches(e.ScannerType, effectiveScannerType))
                        .ToList();
                }

                if (children.Count == 0)
                    continue;

                var nowUtc = DateTime.UtcNow;
                foreach (var child in children)
                {
                    if (AdvanceChild(child, targetStatus, nowUtc))
                        updatedChildren++;
                }

                var record = await db.RecordCompletenessStatuses
                    .AsTracking()
                    .FirstOrDefaultAsync(r => r.Id == recordId, ct);

                if (record == null)
                    continue;

                var allChildren = await db.RecordExpectedContainers
                    .AsTracking()
                    .Where(e => e.RecordId == recordId)
                    .ToListAsync(ct);

                RecordCompletenessBuilder.Recompute(record, allChildren);
                recomputedRecords++;
            }

            if (recomputedRecords > 0)
                await db.SaveChangesAsync(ct);

            return updatedChildren;
        }

        private static async Task<IReadOnlyList<int>> ResolveRecordIdsAsync(
            ApplicationDbContext db,
            AnalysisGroup? group,
            string containerNumber,
            string? scannerType,
            CancellationToken ct)
        {
            if (group?.RecordCompletenessStatusId is int recordId)
                return new[] { recordId };

            var children = await db.RecordExpectedContainers
                .AsNoTracking()
                .Where(e => e.ContainerNumber == containerNumber)
                .Select(e => new { e.RecordId, e.ScannerType })
                .ToListAsync(ct);

            if (!string.IsNullOrWhiteSpace(scannerType))
            {
                children = children
                    .Where(e => ScannerMatches(e.ScannerType, scannerType))
                    .ToList();
            }

            return children
                .Select(e => e.RecordId)
                .Distinct()
                .ToList();
        }

        private static bool AdvanceChild(RecordExpectedContainer child, string targetStatus, DateTime nowUtc)
        {
            if (string.Equals(targetStatus, "Decided", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(child.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
                    return false;

                var changed = !string.Equals(child.Status, "Decided", StringComparison.OrdinalIgnoreCase);
                if (changed)
                    child.Status = "Decided";

                if (child.DecidedAtUtc == null)
                {
                    child.DecidedAtUtc = nowUtc;
                    changed = true;
                }

                return changed;
            }

            if (string.Equals(targetStatus, "Submitted", StringComparison.OrdinalIgnoreCase))
            {
                var changed = !string.Equals(child.Status, "Submitted", StringComparison.OrdinalIgnoreCase);
                if (changed)
                    child.Status = "Submitted";

                if (child.DecidedAtUtc == null)
                {
                    child.DecidedAtUtc = nowUtc;
                    changed = true;
                }

                return changed;
            }

            return false;
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
