namespace NickScanCentralImagingPortal.Core.DTOs.AiWorkflow
{
    public sealed class OpsTriageResultDto
    {
        public bool Enabled { get; set; }
        public string? Message { get; set; }
        public string? SummaryText { get; set; }
        public string? Source { get; set; }
        public IReadOnlyList<OpsTriageHintDto> Hints { get; set; } = Array.Empty<OpsTriageHintDto>();
    }

    public sealed class OpsTriageHintDto
    {
        public long InvestigationId { get; set; }
        public string GroupId { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public IReadOnlyList<string> SuggestedChecks { get; set; } = Array.Empty<string>();
    }
}
