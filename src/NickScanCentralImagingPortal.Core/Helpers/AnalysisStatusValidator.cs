using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Helpers
{
    /// <summary>
    /// ✅ FIX 3 (LOW PRIORITY): Status validation/state machine for AnalysisGroup status transitions
    /// Ensures only valid status transitions are allowed
    /// </summary>
    public static class AnalysisStatusValidator
    {
        /// <summary>
        /// Valid status transitions from each status
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new()
        {
            // Sprint 5G2 / B1: promote ImageAnalysisDecisionController auto-progression's Ready→PartiallyCompleted
            // edge into the legal table. Decision controller marks groups PartiallyCompleted when ALL containers
            // are imageless (analysisGroup created/loaded fresh in the same request body).
            // Sprint 5G2 / B1 (followup): Ready→Completed for intake's wave-progression reconciliation
            // (RunIntakeWorkflowAsync at ImageAnalysisOrchestratorService.cs:887): when an existing group is
            // re-observed during intake and stats show all containers terminal, the group jumps directly to
            // Completed. The local transition table at the call site previously gated this; now the validator does.
            // Sprint 5G2 / B1 (followup 2): Ready→AuditAssigned for ImageAnalysisController.AssignGroup —
            // admin/lead can assign an auditor directly to a Ready group, skipping the analyst step entirely
            // (rare but legitimate "Audit-only" review path).
            // Sprint 5G2 / B1 (followup 3): Ready→Cancelled for composite scan-pair quarantine.
            // A group identifier like "MSMU1683356, MRKU8254509" belongs to the split subsystem,
            // not the image-analysis assignment identity surface; housekeeping cancels those
            // invalid AG shells before they can be reassigned.
            { AnalysisStatuses.Ready, new HashSet<string> { AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AuditAssigned, AnalysisStatuses.AnalystCompleted, AnalysisStatuses.AgentProcessing, AnalysisStatuses.PartiallyCompleted, AnalysisStatuses.Completed, AnalysisStatuses.Cancelled } }, // AnalystCompleted: Decision Agent bypasses assignment; AgentProcessing: Decision Agent claims group; PartiallyCompleted: auto-progression on imageless group; Completed: intake wave-progression reconciliation; AuditAssigned: admin direct-to-audit assignment; Cancelled: composite scan-pair quarantine
            // Sprint 5G2 / B1: promote DecisionAgentWorker's AgentProcessing→AuditCompleted edge into the
            // legal table. When ProcessingDepthAudit is enabled, the agent self-audits its own decision and
            // jumps directly from AgentProcessing to AuditCompleted (the AnalystCompleted state is implicit
            // and skipped — DecisionSideEffectsService's guard at line 104 only fires for AnalystAssigned/Ready,
            // so the group stays AgentProcessing through the side effects).
            { AnalysisStatuses.AgentProcessing, new HashSet<string> { AnalysisStatuses.Ready, AnalysisStatuses.AnalystCompleted, AnalysisStatuses.AuditCompleted } }, // 2026-05-05: Decision Agent releases (no decision) or completes (auto-decided); B1: AuditCompleted when ProcessingDepthAudit advances directly
            // Sprint 5G2 / B1: promote ImageAnalysisDecisionController auto-progression's AnalystAssigned→PartiallyCompleted
            // edge (some containers imageless, all decided) into the legal table.
            { AnalysisStatuses.AnalystAssigned, new HashSet<string> { AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Ready, AnalysisStatuses.Cancelled, AnalysisStatuses.PartiallyCompleted, AnalysisStatuses.Completed } }, // 2026-05-05: orphan-AG path → Cancelled; B1: PartiallyCompleted from auto-progression; B1: Completed from RunStuckGroupsSweep when housekeeping observes all containers Completed
            // Sprint 5G2 / B1: promote ZombieAnalysisGroupSweeper's existing transition out of bypass-only
            // into the legal table so AnalysisGroupStateMachine can apply it. The sweeper archives
            // AnalystCompleted AGs that have aged past the grace window with zero ContainerCompletenessStatus
            // rows; this is documented live behaviour (zombie sweeper writes Archived) which the validator
            // previously did not know about.
            // Sprint 5G2 / B1: promote ImageAnalysisManagementController.ReverseAgentDecision's
            // AnalystCompleted→Ready edge into the legal table. Agent reversal rolls a group back to
            // Ready so analysts can re-decide (the agent's auto-completion is undone).
            // Sprint 5G2 / B1 (followup): AnalystCompleted→Completed for intake wave-progression
            // reconciliation + housekeeping WorkflowStage drift sync
            // (ImageAnalysisOrchestratorService.cs:887 + 3960): when intake or the WorkflowStage-driven
            // housekeeping observes an AnalystCompleted group whose underlying containers are all
            // terminal, the group jumps to Completed without going via Audit. Live behaviour predates
            // the validator; promoting the edge here so the facade can apply it.
            // Sprint 5G2 / B1 (followup 2): AnalystCompleted→AnalystAssigned for
            // ImageAnalysisController.AssignGroup — admin re-assigns an analyst to revisit a completed
            // analysis (the inverse of the normal AnalystAssigned→AnalystCompleted edge).
            { AnalysisStatuses.AnalystCompleted, new HashSet<string> { AnalysisStatuses.AuditAssigned, AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AuditCompleted, AnalysisStatuses.Archived, AnalysisStatuses.Ready, AnalysisStatuses.Completed } }, // AuditCompleted: Decision Agent bypasses audit assignment; Archived: ZombieAnalysisGroupSweeper; Ready: ReverseAgentDecision rollback; Completed: intake wave-progression + housekeeping drift sync; AnalystAssigned: admin re-assignment for re-review
            // Sprint 5G2 / B1: promote housekeeping's AuditAssigned→Ready/Completed edges into the legal table.
            // RunStuckGroupsSweep observes the WorkflowStage and computes the correct status — could be Ready
            // (containers reverted to ImageAnalysis) or Completed (all containers Completed).
            { AnalysisStatuses.AuditAssigned, new HashSet<string> { AnalysisStatuses.AuditCompleted, AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Cancelled, AnalysisStatuses.Ready, AnalysisStatuses.Completed } }, // 2026-05-05: orphan-AG path → Cancelled; B1: Ready/Completed from housekeeping stuck-group sweep
            { AnalysisStatuses.AuditCompleted, new HashSet<string> { AnalysisStatuses.Submitted, AnalysisStatuses.Completed, AnalysisStatuses.PartiallyCompleted } }, // ✅ NEW: Can transition to PartiallyCompleted
            { AnalysisStatuses.Submitted, new HashSet<string> { AnalysisStatuses.Completed } },
            { AnalysisStatuses.PartiallyCompleted, new HashSet<string> { AnalysisStatuses.Ready, AnalysisStatuses.Completed } }, // ✅ NEW: Can reprocess (Ready) or auto-complete (Completed)
            { AnalysisStatuses.Completed, new HashSet<string>() }, // Terminal state - no transitions allowed
            // 2026-05-05 (Sprint 2C, audit 4.04): mark Cancelled + Archived as terminal so a stray
            // transition out of either is rejected by the validator (was previously falling through
            // to the unknown-from branch and silently allowed).
            { AnalysisStatuses.Cancelled, new HashSet<string>() }, // Terminal - orphan-AG sweeper outcome
            { AnalysisStatuses.Archived, new HashSet<string>() }   // Terminal - record reconciler / zombie sweeper outcome
        };

        /// <summary>
        /// Check if a status transition is valid
        /// </summary>
        /// <param name="fromStatus">Current status</param>
        /// <param name="toStatus">Target status</param>
        /// <returns>True if transition is valid, false otherwise</returns>
        public static bool IsValidTransition(string fromStatus, string toStatus)
        {
            if (string.IsNullOrWhiteSpace(fromStatus) || string.IsNullOrWhiteSpace(toStatus))
            {
                return false;
            }

            // Same status is always valid (idempotent)
            if (fromStatus.Equals(toStatus, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if transition is in valid transitions dictionary
            if (!ValidTransitions.ContainsKey(fromStatus))
            {
                // 2026-05-05 (Sprint 2C, audit 4.04): unknown-from used to silently
                // return true ("backward compatibility"), which masked typos and let
                // bogus transitions through. Now reject and emit a Trace.WriteLine —
                // the validator has no logger of its own, but every caller logs
                // LogWarning when IsValidTransition returns false (e.g. orchestrator
                // SubmissionWorkflow), so the rejection is visible at the call site.
                Trace.WriteLine(
                    $"[AnalysisStatusValidator] Rejecting transition from unknown status '{fromStatus}' to '{toStatus}'. Add '{fromStatus}' to ValidTransitions if it is a real state.");
                return false;
            }

            return ValidTransitions[fromStatus].Contains(toStatus);
        }

        /// <summary>
        /// Get all valid target statuses from a given status
        /// </summary>
        /// <param name="fromStatus">Current status</param>
        /// <returns>List of valid target statuses</returns>
        public static List<string> GetValidTargetStatuses(string fromStatus)
        {
            if (string.IsNullOrWhiteSpace(fromStatus))
            {
                return new List<string>();
            }

            if (!ValidTransitions.ContainsKey(fromStatus))
            {
                return new List<string>();
            }

            return ValidTransitions[fromStatus].ToList();
        }

        /// <summary>
        /// Validate and throw exception if transition is invalid
        /// </summary>
        /// <param name="fromStatus">Current status</param>
        /// <param name="toStatus">Target status</param>
        /// <param name="groupId">Group ID for error message (optional)</param>
        /// <exception cref="InvalidOperationException">Thrown if transition is invalid</exception>
        public static void ValidateTransition(string fromStatus, string toStatus, string? groupId = null)
        {
            if (!IsValidTransition(fromStatus, toStatus))
            {
                var validTargets = GetValidTargetStatuses(fromStatus);
                var validTargetsStr = validTargets.Any()
                    ? string.Join(", ", validTargets)
                    : "none (terminal state)";

                var groupInfo = !string.IsNullOrWhiteSpace(groupId) ? $" for group {groupId}" : "";
                throw new System.InvalidOperationException(
                    $"Invalid status transition{groupInfo}: Cannot transition from '{fromStatus}' to '{toStatus}'. " +
                    $"Valid target statuses from '{fromStatus}': {validTargetsStr}");
            }
        }

        /// <summary>
        /// Check if a status is a terminal state (no further transitions allowed)
        /// </summary>
        /// <param name="status">Status to check</param>
        /// <returns>True if status is terminal, false otherwise</returns>
        public static bool IsTerminalState(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return ValidTransitions.ContainsKey(status) &&
                   ValidTransitions[status].Count == 0;
        }

        /// <summary>
        /// Get status transition path from one status to another
        /// Returns the shortest path if multiple paths exist
        /// </summary>
        /// <param name="fromStatus">Starting status</param>
        /// <param name="toStatus">Target status</param>
        /// <returns>List of statuses representing the path, or empty list if no path exists</returns>
        public static List<string> GetTransitionPath(string fromStatus, string toStatus)
        {
            if (string.IsNullOrWhiteSpace(fromStatus) || string.IsNullOrWhiteSpace(toStatus))
            {
                return new List<string>();
            }

            if (fromStatus.Equals(toStatus, System.StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { fromStatus };
            }

            // Simple BFS to find shortest path
            var queue = new System.Collections.Generic.Queue<(string Status, List<string> Path)>();
            var visited = new HashSet<string>();

            queue.Enqueue((fromStatus, new List<string> { fromStatus }));
            visited.Add(fromStatus);

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();

                if (current.Equals(toStatus, System.StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                var nextStatuses = GetValidTargetStatuses(current);
                foreach (var next in nextStatuses)
                {
                    if (!visited.Contains(next))
                    {
                        visited.Add(next);
                        var newPath = new List<string>(path) { next };
                        queue.Enqueue((next, newPath));
                    }
                }
            }

            return new List<string>(); // No path found
        }
    }
}

