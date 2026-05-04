namespace NickScanCentralImagingPortal.Core.Models
{
    public static class AnalysisStatuses
    {
        public const string Ready = "Ready";
        public const string AgentProcessing = "AgentProcessing"; // Decision Agent has claimed this group — do not assign to analysts
        public const string AnalystAssigned = "AnalystAssigned";
        public const string AnalystCompleted = "AnalystCompleted";
        public const string AuditAssigned = "AuditAssigned";
        public const string AuditCompleted = "AuditCompleted";
        public const string Submitted = "Submitted";
        public const string PartiallyCompleted = "PartiallyCompleted"; // ✅ NEW: For records with containers missing images
        public const string Completed = "Completed";

        // 2026-05-04 (2.16.1): terminal state for orphan AGs whose every container has
        // no boedocumentid + no active CBR. Used by the lease sweeper to break the
        // re-assignment cycle on FS6000 export-pending populations awaiting an ICUMS
        // export-feed extension. ZombieAnalysisGroupSweeperService writes "Archived"
        // for a different shape (AnalystCompleted + zero CCS); these are distinct.
        public const string Cancelled = "Cancelled";
    }
}


