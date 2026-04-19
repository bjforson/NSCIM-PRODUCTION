using Microsoft.EntityFrameworkCore;
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

        public DecisionSideEffectsService(ILogger<DecisionSideEffectsService> logger) : this((ILogger)logger, null) { }

        public DecisionSideEffectsService(ILogger logger) : this(logger, null) { }

        public DecisionSideEffectsService(ILogger logger, ReadyGroupsCacheService? queueService)
        {
            _logger = logger;
            _queueService = queueService;
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
                // 0. Load group early — needed for decision guard and later steps
                var group = await db.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier
                        || g.NormalizedGroupIdentifier == groupIdentifier, ct);

                if (group == null)
                    return;

                // GUARD: Verify a real ImageAnalysisDecision exists before flipping to "Decided".
                // Prevents the self-healing sweep from auto-deciding containers that were never reviewed.
                var hasDecision = await db.ImageAnalysisDecisions
                    .AnyAsync(d => d.ContainerNumber == containerNumber
                        && (d.GroupIdentifier == groupIdentifier
                            || d.GroupIdentifier == group.NormalizedGroupIdentifier), ct);

                if (!hasDecision)
                {
                    _logger.LogWarning("[DECISION-FX] No ImageAnalysisDecision for {Container} in {Group} — skipping status flip",
                        containerNumber, groupIdentifier);
                    return;
                }

                // 1. Update AnalysisRecord.Status to Decided
                var records = await db.AnalysisRecords
                    .AsTracking()
                    .Where(r => r.ContainerNumber == containerNumber && r.Status == "Ready")
                    .ToListAsync(ct);

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
                    group.Status = AnalysisStatuses.AnalystCompleted;
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
                var normalizedGroupId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
                IQueryable<ContainerCompletenessStatus> completenessQuery = db.ContainerCompletenessStatuses
                    .AsTracking()
                    .Where(c => c.GroupIdentifier == normalizedGroupId
                        && c.WorkflowStage == "ImageAnalysis");

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

            var group = await db.AnalysisGroups
                .AsTracking()
                .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier
                    || g.NormalizedGroupIdentifier == groupIdentifier, ct);

            if (group == null)
                return;

            var records = await db.AnalysisRecords
                .AsTracking()
                .Where(r => r.GroupId == group.Id)
                .ToListAsync(ct);

            foreach (var container in records.Select(r => r.ContainerNumber).Distinct())
            {
                await ApplyAsync(db, container, groupIdentifier, scannerType, ct);
            }
        }
    }
}
