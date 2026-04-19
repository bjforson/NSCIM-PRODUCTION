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
    }
}


