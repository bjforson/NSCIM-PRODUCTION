using System.Collections.Generic;
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
            { AnalysisStatuses.Ready, new HashSet<string> { AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AnalystCompleted } }, // AnalystCompleted: Decision Agent bypasses assignment
            { AnalysisStatuses.AnalystAssigned, new HashSet<string> { AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Ready } },
            { AnalysisStatuses.AnalystCompleted, new HashSet<string> { AnalysisStatuses.AuditAssigned, AnalysisStatuses.AuditCompleted } }, // AuditCompleted: Decision Agent bypasses audit assignment
            { AnalysisStatuses.AuditAssigned, new HashSet<string> { AnalysisStatuses.AuditCompleted, AnalysisStatuses.AnalystCompleted } },
            { AnalysisStatuses.AuditCompleted, new HashSet<string> { AnalysisStatuses.Submitted, AnalysisStatuses.Completed, AnalysisStatuses.PartiallyCompleted } }, // ✅ NEW: Can transition to PartiallyCompleted
            { AnalysisStatuses.Submitted, new HashSet<string> { AnalysisStatuses.Completed } },
            { AnalysisStatuses.PartiallyCompleted, new HashSet<string> { AnalysisStatuses.Ready, AnalysisStatuses.Completed } }, // ✅ NEW: Can reprocess (Ready) or auto-complete (Completed)
            { AnalysisStatuses.Completed, new HashSet<string>() } // Terminal state - no transitions allowed
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
                // Unknown from status - allow transition but log warning
                return true; // Allow unknown statuses for backward compatibility
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

