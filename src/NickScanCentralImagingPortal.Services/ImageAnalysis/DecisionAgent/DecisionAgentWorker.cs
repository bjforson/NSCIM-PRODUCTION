using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent
{
    /// <summary>
    /// Core workflow logic for the Autonomous Decision Agent.
    /// Evaluates Ready groups against weighted risk conditions and auto-decides
    /// Normal/Abnormal for cargo outside the uncertain zone.
    /// </summary>
    public static class DecisionAgentWorker
    {
        private static readonly List<IConditionEvaluator> BuiltInEvaluators = new()
        {
            new CrmsLevelEvaluator(),
            new MultipleHouseBLEvaluator(),
            new HasVehicleEvaluator(),
            new HasUsedItemsEvaluator(),
            new MultipleLineItemsEvaluator(),
            new HighRiskCountryEvaluator(),
            new VagueDescriptionEvaluator(),
            new HighRiskHsCodeEvaluator(),
            new DutyValueAnomalyEvaluator()
        };

        private static readonly DynamicConditionEvaluator DynamicEvaluator = new();

        public static async Task RunDecisionAgentWorkflowAsync(
            ApplicationDbContext db,
            IcumDownloadsDbContext icumDb,
            DecisionAgentSettings settings,
            List<DecisionAgentCondition> conditions,
            ILogger logger,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            // Load Ready groups not yet processed by agent (LEFT JOIN audit log)
            var processedGroupIds = await db.DecisionAgentAuditLogs
                .Where(a => a.ReversedAtUtc == null) // exclude reversed (allow re-evaluation)
                .Select(a => a.GroupId)
                .Distinct()
                .ToListAsync(ct);

            var nowUtc = DateTime.UtcNow;
            var readyGroups = await db.AnalysisGroups
                .AsTracking()
                .Where(g => g.Status == AnalysisStatuses.Ready)
                .Where(g => !processedGroupIds.Contains(g.Id))
                .Where(g => !db.AnalysisAssignments.Any(a =>
                    a.GroupId == g.Id
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > nowUtc)))
                .OrderBy(g => g.Priority)
                .ThenBy(g => g.CreatedAtUtc)
                .Take(settings.MaxGroupsPerCycle)
                .ToListAsync(ct);

            if (!readyGroups.Any())
                return;

            // ✅ CLAIM: Mark all eligible groups as AgentProcessing BEFORE processing
            // This prevents the assignment worker from assigning them to analysts during scoring
            // Sprint 5G2 / B1: route through the state-machine facade. Ready → AgentProcessing
            // is in the legal table.
            foreach (var g in readyGroups)
            {
                var hasActiveHumanAssignment = await db.AnalysisAssignments
                    .AsNoTracking()
                    .AnyAsync(a =>
                        a.GroupId == g.Id
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow),
                        ct);

                if (hasActiveHumanAssignment)
                {
                    logger.LogInformation(
                        "[DECISION-AGENT] Skipping group {GroupIdentifier}; active human assignment already exists",
                        g.GroupIdentifier);
                    continue;
                }

                await AnalysisGroupStateMachine.TransitionAsync(
                    db, g, AnalysisStatuses.AgentProcessing,
                    triggerName: "DecisionAgentClaimedGroup",
                    actor: "DECISION-AGENT",
                    reason: "Claiming Ready group for autonomous evaluation cycle.",
                    correlationId: null,
                    ct: ct);
                g.UpdatedAtUtc = DateTime.UtcNow;
            }
            readyGroups = readyGroups
                .Where(g => g.Status == AnalysisStatuses.AgentProcessing)
                .ToList();

            if (!readyGroups.Any())
                return;

            logger.LogInformation("[DECISION-AGENT] Claimed {Count} group(s) as AgentProcessing", readyGroups.Count);

            // Build evaluators list (BuiltIn + Dynamic)
            var evaluators = new List<IConditionEvaluator>(BuiltInEvaluators);

            var processed = 0;
            var normalCount = 0;
            var abnormalCount = 0;
            var skippedCount = 0;

            foreach (var group in readyGroups)
            {
                if (ct.IsCancellationRequested) break;

                var groupSw = Stopwatch.StartNew();
                try
                {
                    await ProcessSingleGroupAsync(db, icumDb, settings, conditions, evaluators, group, logger, ct);
                    processed++;

                    var lastLog = await db.DecisionAgentAuditLogs
                        .Where(a => a.GroupId == group.Id)
                        .OrderByDescending(a => a.CreatedAtUtc)
                        .FirstOrDefaultAsync(ct);

                    if (lastLog?.Decision == "Normal") normalCount++;
                    else if (lastLog?.Decision == "Abnormal") abnormalCount++;
                    else skippedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[DECISION-AGENT] Error processing group {GroupId} ({GroupIdentifier})",
                        group.Id, group.GroupIdentifier);

                    // Log error in audit trail
                    db.DecisionAgentAuditLogs.Add(new DecisionAgentAuditLog
                    {
                        GroupId = group.Id,
                        GroupIdentifier = group.GroupIdentifier,
                        TotalScore = 0,
                        Decision = "Skipped",
                        IsShadowMode = settings.ShadowMode,
                        ProcessingDepthReached = "None",
                        ErrorMessage = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message,
                        ProcessingTimeMs = groupSw.ElapsedMilliseconds,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
            }

            logger.LogInformation(
                "[DECISION-AGENT] Cycle complete: {Processed} groups in {Elapsed}ms (Normal={Normal}, Abnormal={Abnormal}, Skipped={Skipped})",
                processed, sw.ElapsedMilliseconds, normalCount, abnormalCount, skippedCount);
        }

        private static async Task ProcessSingleGroupAsync(
            ApplicationDbContext db,
            IcumDownloadsDbContext icumDb,
            DecisionAgentSettings settings,
            List<DecisionAgentCondition> conditions,
            List<IConditionEvaluator> evaluators,
            AnalysisGroup group,
            ILogger logger,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            // Load records for this group
            var records = await db.AnalysisRecords
                .Where(r => r.GroupId == group.Id)
                .ToListAsync(ct);

            var containerNumbers = records.Select(r => r.ContainerNumber).Distinct().ToList();

            // Load cargo data from ICUMS downloads DB
            var boeDocuments = await icumDb.BOEDocuments
                .Where(b => containerNumbers.Contains(b.ContainerNumber))
                .ToListAsync(ct);

            var boeIds = boeDocuments.Select(b => b.Id).ToList();

            var manifestItems = await icumDb.ManifestItems
                .Where(m => boeIds.Contains(m.BOEDocumentId))
                .ToListAsync(ct);

            var vehicleImports = await icumDb.VehicleImports
                .Where(v => boeIds.Contains(v.BOEDocumentId))
                .ToListAsync(ct);

            // Build context
            var context = new DecisionAgentContext
            {
                Group = group,
                Records = records,
                BOEDocuments = boeDocuments,
                ManifestItems = manifestItems,
                VehicleImports = vehicleImports
            };

            // Build effective evaluator list including dynamic
            var effectiveEvaluators = new List<IConditionEvaluator>(evaluators);
            // For Dynamic conditions, use the DynamicEvaluator
            foreach (var cond in conditions.Where(c => c.Enabled && DynamicConditionEvaluator.IsDynamic(c.EvaluatorType)))
            {
                effectiveEvaluators.Add(new DynamicConditionProxy(cond.ConditionKey, DynamicEvaluator));
            }

            // Score
            var scoringResult = await DecisionAgentScoringEngine.ScoreAsync(
                context, conditions, effectiveEvaluators, logger, ct);

            // Determine decision zone
            string decision;
            bool canAct = !settings.ShadowMode;

            if (scoringResult.Score <= settings.NormalThreshold && settings.AllowNormalDecisions)
                decision = "Normal";
            else if (scoringResult.Score >= settings.AbnormalThreshold && settings.AllowAbnormalDecisions)
                decision = "Abnormal";
            else
                decision = "Skipped";

            if (decision != "Skipped")
            {
                var unresolvedSplitContainers = records
                    .Where(SplitDecisionEligibility.RequiresSplitResolution)
                    .Select(r => r.ContainerNumber)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (unresolvedSplitContainers.Any())
                {
                    logger.LogInformation(
                        "[DECISION-AGENT] Skipping auto-decision for group {GroupIdentifier}: split choice required for {Containers}",
                        group.GroupIdentifier,
                        string.Join(", ", unresolvedSplitContainers));
                    decision = "Skipped";
                }
            }

            // Apply decision if not shadow mode and not skipped
            var createdDecisionIds = new List<int>();
            var depthReached = "None";

            if (canAct && decision != "Skipped")
            {
                // Stage 1: Create ImageAnalysisDecision records
                depthReached = "Decision";
                foreach (var record in records)
                {
                    var analysisDecision = new ImageAnalysisDecision
                    {
                        ContainerNumber = record.ContainerNumber,
                        ScannerType = record.ScannerType ?? group.ScannerType ?? "FS6000",
                        Decision = decision,
                        Comments = $"Autonomous Decision Agent — {decision} (score: {scoringResult.Score:F3})",
                        Tags = decision == "Abnormal" ? "agent-flagged" : "agent-cleared",
                        ReviewedBy = "DECISION-AGENT",
                        ReviewedAt = DateTime.UtcNow,
                        GroupIdentifier = group.GroupIdentifier,
                        IsConsolidated = boeDocuments.Any(b => b.IsConsolidated),
                        SplitJobId = record.SplitJobId,
                        SplitResultId = record.SplitResultId
                    };
                    db.ImageAnalysisDecisions.Add(analysisDecision);
                }
                await db.SaveChangesAsync(ct);

                // Get created decision IDs
                createdDecisionIds = await db.ImageAnalysisDecisions
                    .Where(d => d.ReviewedBy == "DECISION-AGENT" && d.GroupIdentifier == group.GroupIdentifier)
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(records.Count)
                    .Select(d => d.Id)
                    .ToListAsync(ct);

                // Centralized side effects: update records, release assignments, advance workflow
                var sideEffects = new DecisionSideEffectsService(logger);
                await sideEffects.ApplyForGroupAsync(db, group.GroupIdentifier, group.ScannerType, ct);

                // Stage 2: Through Audit (if enabled)
                if (settings.ProcessingDepthAudit)
                {
                    depthReached = "Audit";
                    foreach (var decisionId in createdDecisionIds)
                    {
                        var originalDecision = await db.ImageAnalysisDecisions.FindAsync(new object[] { decisionId }, ct);
                        if (originalDecision == null) continue;

                        db.AuditDecisions.Add(new AuditDecision
                        {
                            ContainerNumber = originalDecision.ContainerNumber,
                            GroupIdentifier = group.GroupIdentifier,
                            ScannerType = originalDecision.ScannerType,
                            ImageAnalysisDecisionId = decisionId,
                            Decision = "Approved",
                            AuditNotes = $"Auto-approved by Decision Agent (score: {scoringResult.Score:F3})",
                            AuditedBy = "DECISION-AGENT",
                            AuditedAt = DateTime.UtcNow,
                            OverallGroupDecision = "Approved",
                            IsCompleted = true,
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync(ct);

                    // Advance: AgentProcessing → AuditCompleted (depth=Audit jumps past AnalystCompleted because
                    // DecisionSideEffectsService.ApplyForGroupAsync only advances Ready/AnalystAssigned, not AgentProcessing).
                    // Sprint 5G2 / B1: route through the state-machine facade.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        db, group, AnalysisStatuses.AuditCompleted,
                        triggerName: "DecisionAgentAutoAudited",
                        actor: "DECISION-AGENT",
                        reason: $"Auto-audit complete (depth=Audit); score={scoringResult.Score:F3}, decision={decision}.",
                        correlationId: null,
                        ct: ct);

                    // Stage 3: Through Submission (if enabled)
                    if (settings.ProcessingDepthSubmission)
                    {
                        depthReached = "Submission";
                        // Mark as Completed — the existing SubmissionWorker will pick it up
                        // or we can directly mark it for submission
                        group.TotalContainerCount = records.Count;
                        group.SubmittedContainerCount = records.Count;
                        group.PendingContainerCount = 0;
                        // Sprint 5G2 / B1: route through the state-machine facade. AuditCompleted → Completed
                        // is in the legal table.
                        await AnalysisGroupStateMachine.TransitionAsync(
                            db, group, AnalysisStatuses.Completed,
                            triggerName: "DecisionAgentAutoSubmitted",
                            actor: "DECISION-AGENT",
                            reason: $"Auto-submit complete (depth=Submission); score={scoringResult.Score:F3}, decision={decision}, records={records.Count}.",
                            correlationId: null,
                            ct: ct);
                    }
                }

                // Update ContainerCompletenessStatus workflow stages
                var workflowStage = depthReached switch
                {
                    "Submission" => "Submitted",
                    "Audit" => "PendingSubmission",
                    "Decision" => "Audit",
                    _ => "ImageAnalysis"
                };

                foreach (var containerNumber in containerNumbers)
                {
                    var completeness = await db.ContainerCompletenessStatuses
                        .Where(c => c.ContainerNumber == containerNumber)
                        .FirstOrDefaultAsync(ct);

                    if (completeness != null)
                    {
                        completeness.WorkflowStage = workflowStage;
                        completeness.UpdatedAt = DateTime.UtcNow;
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            // Write audit log
            var auditLog = new DecisionAgentAuditLog
            {
                GroupId = group.Id,
                GroupIdentifier = group.GroupIdentifier,
                TotalScore = scoringResult.Score,
                Decision = decision,
                IsShadowMode = settings.ShadowMode,
                ProcessingDepthReached = canAct && decision != "Skipped" ? depthReached : "None",
                ConditionResultsJson = JsonSerializer.Serialize(scoringResult.Entries),
                ContainerCount = containerNumbers.Count,
                ContainerNumbers = JsonSerializer.Serialize(containerNumbers),
                DecisionIds = createdDecisionIds.Any() ? JsonSerializer.Serialize(createdDecisionIds) : null,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.DecisionAgentAuditLogs.Add(auditLog);

            // ✅ REVERT: If agent skipped or is in shadow mode, release the group back to Ready
            // so it becomes available for human analyst assignment
            if (decision == "Skipped" || settings.ShadowMode)
            {
                if (group.Status == AnalysisStatuses.AgentProcessing)
                {
                    var hasActiveHumanAssignment = await db.AnalysisAssignments
                        .AsNoTracking()
                        .AnyAsync(a =>
                            a.GroupId == group.Id
                            && a.State == "Active"
                            && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow),
                            ct);

                    if (hasActiveHumanAssignment)
                    {
                        await db.SaveChangesAsync(ct);
                        logger.LogInformation(
                            "[DECISION-AGENT] Did not release group {GroupIdentifier} to Ready because an active human assignment exists",
                            group.GroupIdentifier);
                        return;
                    }

                    // Sprint 5G2 / B1: route through the state-machine facade. AgentProcessing → Ready
                    // is in the legal table.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        db, group, AnalysisStatuses.Ready,
                        triggerName: "DecisionAgentReleasedGroup",
                        actor: "DECISION-AGENT",
                        reason: $"Released group back to Ready (decision={decision}, shadow={settings.ShadowMode}); audit log persisted alongside.",
                        correlationId: null,
                        ct: ct);
                    group.UpdatedAtUtc = DateTime.UtcNow;
                    logger.LogInformation("[DECISION-AGENT] Reverted group {GroupIdentifier} to Ready (decision={Decision}, shadow={Shadow})",
                        group.GroupIdentifier, decision, settings.ShadowMode);
                }
                else
                {
                    await db.SaveChangesAsync(ct);
                }
            }
            else
            {
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation(
                "[DECISION-AGENT] Group {GroupIdentifier}: score={Score:F3}, decision={Decision}, depth={Depth}, shadow={Shadow}, time={TimeMs}ms",
                group.GroupIdentifier, scoringResult.Score, decision, auditLog.ProcessingDepthReached, settings.ShadowMode, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Wraps the DynamicConditionEvaluator to handle a specific condition key.
        /// </summary>
        private class DynamicConditionProxy : IConditionEvaluator
        {
            private readonly string _conditionKey;
            private readonly DynamicConditionEvaluator _inner;

            public DynamicConditionProxy(string conditionKey, DynamicConditionEvaluator inner)
            {
                _conditionKey = conditionKey;
                _inner = inner;
            }

            public bool CanHandle(string conditionKey) => conditionKey == _conditionKey;

            public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
                => _inner.EvaluateAsync(context, ct);
        }
    }
}
