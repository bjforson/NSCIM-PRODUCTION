using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// Single-row configuration table for the Autonomous Decision Agent.
    /// The agent scores Ready groups against weighted risk conditions and auto-decides
    /// Normal/Abnormal for cargo that falls outside the uncertain zone.
    /// </summary>
    [Table("DecisionAgentSettings")]
    public class DecisionAgentSettings
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Master on/off switch for the decision agent.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>When true, agent scores groups and writes audit logs but does NOT create decisions or advance statuses.</summary>
        public bool ShadowMode { get; set; } = true;

        /// <summary>When true, the agent can auto-decide "Normal" for low-scoring cargo.</summary>
        public bool AllowNormalDecisions { get; set; } = true;

        /// <summary>When true, the agent can auto-decide "Abnormal" for high-scoring cargo.</summary>
        public bool AllowAbnormalDecisions { get; set; } = false;

        /// <summary>Score at or below this threshold → auto "Normal" (0.0–1.0).</summary>
        public double NormalThreshold { get; set; } = 0.2;

        /// <summary>Score at or above this threshold → auto "Abnormal" (0.0–1.0).</summary>
        public double AbnormalThreshold { get; set; } = 0.7;

        // --- Processing Depth: how far the agent progresses decisions ---

        /// <summary>Stage 1: Create ImageAnalysisDecision (always true when agent is active).</summary>
        public bool ProcessingDepthDecision { get; set; } = true;

        /// <summary>Stage 2: Also advance through Audit (create AuditDecision).</summary>
        public bool ProcessingDepthAudit { get; set; } = false;

        /// <summary>Stage 3: Also advance through ICUMS Submission.</summary>
        public bool ProcessingDepthSubmission { get; set; } = false;

        /// <summary>Maximum number of Ready groups to evaluate per orchestrator cycle.</summary>
        public int MaxGroupsPerCycle { get; set; } = 50;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
