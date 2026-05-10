using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Centralized service for all side effects that must happen after a decision is saved.
    /// Called from every code path that creates/updates ImageAnalysisDecisions:
    /// - ImageAnalysisDecisionController (UI save)
    /// - DecisionAgentWorker (autonomous agent)
    /// - AuditReviewController (audit override)
    /// - ImageAnalysisController (alternative path)
    ///
    /// This ensures AnalysisRecord.Status, group status, assignments, and workflow stage
    /// are always kept in sync regardless of which code path saved the decision.
    /// </summary>
    public class DecisionSideEffectsService
    {
        private readonly ILogger _logger;
        private readonly ReadyGroupsCacheService? _queueService;

        [ActivatorUtilitiesConstructor]
        public DecisionSideEffectsService(ILogger<DecisionSideEffectsService> logger, ReadyGroupsCacheService queueService)
            : this((ILogger)logger, queueService) { }

        public DecisionSideEffectsService(ILogger<DecisionSideEffectsService> logger) : this((ILogger)logger, null) { }

        public DecisionSideEffectsService(ILogger logger) : this(logger, null) { }

        public DecisionSideEffectsService(ILogger logger, ReadyGroupsCacheService? queueService)
        {
            _logger = logger;
            _queueService = queueService;
        }

        private static string? BaseScannerType(string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
                return null;

            var trimmed = scannerType.Trim();
            var dashIndex = trimmed.IndexOf('-');
            return dashIndex > 0 ? trimmed.Substring(0, dashIndex) : trimmed;
        }

        private static bool ScannerMatches(string? candidate, string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return true;

            if (string.IsNullOrWhiteSpace(candidate))
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

        private static IQueryable<ContainerCompletenessStatus> ApplyScannerFilter(
            IQueryable<ContainerCompletenessStatus> query,
            string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
                return query;

            var scannerBase = BaseScannerType(scannerType) ?? scannerType;
            return query.Where(c =>
                c.ScannerType == scannerType
                || c.ScannerType == scannerBase
                || c.ScannerType.StartsWith(scannerBase + "-"));
        }

        private static IQueryable<ImageAnalysisDecision> ApplyScannerFilter(
            IQueryable<ImageAnalysisDecision> query,
            string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
                return query;

            var scannerBase = BaseScannerType(scannerType) ?? scannerType;
            return query.Where(d =>
                d.ScannerType == scannerType
                || d.ScannerType == scannerBase
                || d.ScannerType.StartsWith(scannerBase + "-"));
        }

        private static AnalysisGroup? ChooseGroupForScanner(IEnumerable<AnalysisGroup> candidates, string? scannerType)
        {
            return candidates
                .OrderByDescending(g => ScannerMatches(g.ScannerType, scannerType))
                .ThenByDescending(g => !string.IsNullOrWhiteSpace(g.ScannerType))
                .FirstOrDefault();
        }

        /// <summary>
        /// Apply all side effects after a decision is saved for a container.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public async Task ApplyAsync(
            ApplicationDbContext db,
            string containerNumber,
            string groupIdentifier,
            string? scannerType,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(containerNumber) || string.IsNullOrWhiteSpace(groupIdentifier))
                return;

            try
            {
                var normalizedGroupId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;

                // 0. Load group early — needed for decision guard and later steps
                var groupCandidates = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.GroupIdentifier == groupIdentifier
                        || g.NormalizedGroupIdentifier == groupIdentifier
                        || g.GroupIdentifier == normalizedGroupId
                        || g.NormalizedGroupIdentifier == normalizedGroupId)
                    .ToListAsync(ct);

                var group = ChooseGroupForScanner(groupCandidates, scannerType);

                if (group == null)
                    return;

                var effectiveScannerType = scannerType ?? group.ScannerType;
                if (!ScannerMatches(group.ScannerType, effectiveScannerType))
                {
                    _logger.LogWarning("[DECISION-FX] Group {Group} scanner {GroupScanner} does not match decision scanner {DecisionScanner}; skipping side effects",
                        groupIdentifier, group.ScannerType ?? "(none)", scannerType ?? "(none)");
                    return;
                }

                var decisionGroupIdentifiers = new[] { groupIdentifier, normalizedGroupId, group.GroupIdentifier, group.NormalizedGroupIdentifier }
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // GUARD: Verify a real ImageAnalysisDecision exists before flipping to "Decided".
                // Prevents the self-healing sweep from auto-deciding containers that were never reviewed.
                var hasDecision = await ApplyScannerFilter(
                    db.ImageAnalysisDecisions
                        .Where(d => d.ContainerNumber == containerNumber
                            && (d.Decision == "Normal" || d.Decision == "Abnormal")
                            && d.GroupIdentifier != null
                            && decisionGroupIdentifiers.Contains(d.GroupIdentifier)),
                    effectiveScannerType)
                    .AnyAsync(ct);

                if (!hasDecision)
                {
                    _logger.LogWarning("[DECISION-FX] No ImageAnalysisDecision for {Container} in {Group} — skipping status flip",
                        containerNumber, groupIdentifier);
                    return;
                }

                // 1. Update AnalysisRecord.Status to Decided
                var records = await db.AnalysisRecords
                    .AsTracking()
                    .Where(r => r.GroupId == group.Id
                        && r.ContainerNumber == containerNumber
                        && r.Status == "Ready")
                    .ToListAsync(ct);

                if (!string.IsNullOrWhiteSpace(effectiveScannerType))
                {
                    records = records
                        .Where(r => ScannerMatches(r.ScannerType, effectiveScannerType))
                        .ToList();
                }

                if (records.Any())
                {
                    foreach (var rec in records)
                        rec.Status = "Decided";
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("[DECISION-FX] Updated {Count} AnalysisRecord(s) to Decided for {Container}",
                        records.Count, containerNumber);
                }

                var allRecords = await db.AnalysisRecords
                    .Where(r => r.GroupId == group.Id)
                    .ToListAsync(ct);

                if (!allRecords.Any())
                    return;

                var allDecided = allRecords.All(r => r.Status == "Decided");
                if (!allDecided)
                    return; // Still waiting on other containers

                // 3. All records decided — advance group if still in analyst phase
                var groupJustCompleted = false;
                if (group.Status == AnalysisStatuses.AnalystAssigned ||
                    group.Status == AnalysisStatuses.Ready)
                {
                    // Sprint 5G2 / B1: route through the state-machine facade. Both Ready and AnalystAssigned
                    // are legal predecessors to AnalystCompleted.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        db, group, AnalysisStatuses.AnalystCompleted,
                        triggerName: "DecisionSideEffectsAllRecordsDecided",
                        actor: "DECISION-SIDE-EFFECTS",
                        reason: $"All records for group {groupIdentifier} are Decided; advancing analyst phase.",
                        correlationId: null,
                        ct: ct);
                    group.UpdatedAtUtc = DateTime.UtcNow;
                    groupJustCompleted = true;

                    _logger.LogInformation("[DECISION-FX] Group {Group} all records decided — advancing to AnalystCompleted",
                        groupIdentifier);
                }

                // 3c. 1.15.0 — Record-level rollup: update the linked RecordCompletenessStatus
                // and its RecordExpectedContainer children to reflect the decision.
                // Dual-writes alongside the legacy AnalysisParentGroup update below.
                // Best-effort: failures do not block the analyst's decision save.
                if (group.RecordCompletenessStatusId.HasValue)
                {
                    try
                    {
                        var recordId = group.RecordCompletenessStatusId.Value;

                        // Flip the matching RecordExpectedContainer to "Decided"
                        var expectedRows = await db.RecordExpectedContainers
                            .AsTracking()
                            .Where(e => e.RecordId == recordId
                                     && e.ContainerNumber == containerNumber)
                            .ToListAsync(ct);

                        if (!string.IsNullOrWhiteSpace(effectiveScannerType))
                        {
                            expectedRows = expectedRows
                                .Where(e => ScannerMatches(e.ScannerType, effectiveScannerType))
                                .ToList();
                        }
                        var nowUtc = DateTime.UtcNow;
                        foreach (var row in expectedRows)
                        {
                            if (row.Status != "Submitted" && row.Status != "Decided")
                            {
                                row.Status = "Decided";
                                row.DecidedAtUtc = nowUtc;
                            }
                        }

                        // Recompute parent rollup counts
                        var record = await db.RecordCompletenessStatuses
                            .AsTracking()
                            .FirstOrDefaultAsync(r => r.Id == recordId, ct);
                        if (record != null)
                        {
                            var allChildren = await db.RecordExpectedContainers
                                .Where(e => e.RecordId == recordId)
                                .ToListAsync(ct);

                            record.ContainersAwaitingScan = allChildren.Count(c => c.Status == "AwaitingScan");
                            record.ContainersScanned     = allChildren.Count(c => c.Status == "Pending");
                            record.ContainersReady       = allChildren.Count(c => c.Status == "Ready");
                            record.ContainersDecided     = allChildren.Count(c => c.Status == "Decided");
                            record.ContainersSubmitted   = allChildren.Count(c => c.Status == "Submitted");
                            record.ContainersNoImage     = allChildren.Count(c => c.Status == "NoImageAvailable");
                            record.ContainersNoScan      = allChildren.Count(c => c.Status == "NoScanReceived");

                            // Derive parent status from child state
                            var total = allChildren.Count;
                            if (record.ContainersSubmitted == total && total > 0)
                            {
                                record.Status = "Completed";
                                record.WorkflowStage = "Completed";
                            }
                            else if (record.ContainersDecided > 0 || record.ContainersSubmitted > 0)
                            {
                                record.Status = "InAudit";
                                record.WorkflowStage = "Audit";
                            }
                            record.UpdatedAtUtc = nowUtc;
                            record.LastCheckedAtUtc = nowUtc;

                            _logger.LogInformation(
                                "[DECISION-FX] Record {RecordId} ({Decl}) rollup updated: decided={Decided}/{Total} status={Status}",
                                recordId, record.DeclarationNumber, record.ContainersDecided, total, record.Status);
                        }

                        await db.SaveChangesAsync(ct);
                    }
                    catch (Exception recEx)
                    {
                        _logger.LogWarning(recEx,
                            "[DECISION-FX] Record-level rollup failed for group {Group} (non-fatal)",
                            groupIdentifier);
                    }
                }

                // 3b. Wave #6 (1.11.0): parent-group rollup.
                // Only runs on the transition (groupJustCompleted) so CompletedWaveCount is not double-incremented.
                // When a wave (child group) finishes, increment the parent's CompletedWaveCount.
                // When no pending/ready containers remain on the parent, mark the parent Complete.
                if (groupJustCompleted && group.ParentGroupId.HasValue)
                {
                    var parentId = group.ParentGroupId.Value;
                    var parent = await db.AnalysisParentGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(p => p.Id == parentId, ct);

                    if (parent != null)
                    {
                        parent.CompletedWaveCount += 1;
                        parent.UpdatedAtUtc = DateTime.UtcNow;

                        var hasOutstanding = await db.WavePendingContainers
                            .AnyAsync(w => w.ParentGroupId == parentId
                                && (w.Status == "Pending" || w.Status == "Ready"), ct);

                        if (!hasOutstanding && parent.Status == "Active")
                        {
                            parent.Status = "Complete";
                            _logger.LogInformation(
                                "[DECISION-FX] Parent group {ParentId} ({GroupIdentifier}) advanced to Complete — all waves decided, no outstanding containers",
                                parentId, parent.GroupIdentifier);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "[DECISION-FX] Parent group {ParentId} ({GroupIdentifier}) wave count incremented to {Count}; outstanding containers remain: {Outstanding}",
                                parentId, parent.GroupIdentifier, parent.CompletedWaveCount, hasOutstanding);
                        }
                    }
                }

                // 4. Release active analyst assignments
                var activeAssignments = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.GroupId == group.Id && a.State == "Active" && a.Role == "Analyst")
                    .ToListAsync(ct);

                foreach (var assignment in activeAssignments)
                {
                    assignment.State = "Released";
                    assignment.UpdatedAtUtc = DateTime.UtcNow;
                }

                // 4b. Remove released assignments from materialized queue
                if (_queueService != null)
                {
                    foreach (var assignment in activeAssignments)
                    {
                        try { await _queueService.RemoveQueueEntryAsync(db, assignment.Id, ct); }
                        catch { /* reconciliation catches misses */ }
                    }
                }

                // 5. Update WorkflowStage on completeness records
                // When the group just completed, advance ALL containers (not just the triggering one)
                // to prevent WorkflowStage desync where some containers stay at "ImageAnalysis".
                IQueryable<ContainerCompletenessStatus> completenessQuery = db.ContainerCompletenessStatuses
                    .AsTracking()
                    .Where(c => c.GroupIdentifier == normalizedGroupId
                        && c.WorkflowStage == "ImageAnalysis");

                completenessQuery = ApplyScannerFilter(completenessQuery, effectiveScannerType);

                if (!groupJustCompleted)
                    completenessQuery = completenessQuery.Where(c => c.ContainerNumber == containerNumber);

                var completenessRows = await completenessQuery.ToListAsync(ct);

                foreach (var row in completenessRows)
                {
                    row.WorkflowStage = "Audit";
                    row.UpdatedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(ct);

                if (activeAssignments.Any())
                {
                    _logger.LogInformation("[DECISION-FX] Released {Count} analyst assignment(s) for group {Group}",
                        activeAssignments.Count, groupIdentifier);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DECISION-FX] Error applying side effects for {Container} in group {Group}",
                    containerNumber, groupIdentifier);
            }
        }

        /// <summary>
        /// Apply side effects for all containers in a group at once (used by Decision Agent).
        /// </summary>
        public async Task ApplyForGroupAsync(
            ApplicationDbContext db,
            string groupIdentifier,
            string? scannerType,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
                return;

            var normalizedGroupId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
            var groupCandidates = await db.AnalysisGroups
                .AsTracking()
                .Where(g => g.GroupIdentifier == groupIdentifier
                    || g.NormalizedGroupIdentifier == groupIdentifier
                    || g.GroupIdentifier == normalizedGroupId
                    || g.NormalizedGroupIdentifier == normalizedGroupId)
                .ToListAsync(ct);

            var group = ChooseGroupForScanner(groupCandidates, scannerType);

            if (group == null)
                return;

            var effectiveScannerType = scannerType ?? group.ScannerType;
            var records = await db.AnalysisRecords
                .AsTracking()
                .Where(r => r.GroupId == group.Id)
                .ToListAsync(ct);

            if (!string.IsNullOrWhiteSpace(effectiveScannerType))
            {
                records = records
                    .Where(r => ScannerMatches(r.ScannerType, effectiveScannerType))
                    .ToList();
            }

            foreach (var container in records.Select(r => r.ContainerNumber).Distinct())
            {
                await ApplyAsync(db, container, groupIdentifier, effectiveScannerType, ct);
            }
        }
    }
}
