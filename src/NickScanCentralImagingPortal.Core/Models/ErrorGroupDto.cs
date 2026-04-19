namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// DTO for error groups used by error monitoring and investigation services
    /// </summary>
    public class ErrorGroupDto
    {
        public string ErrorPattern { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? ServiceId { get; set; }
        public string? Operation { get; set; }
        public string? ExceptionType { get; set; }
        public List<ErrorLogEntryDto> Errors { get; set; } = new();
    }

    public class ErrorLogEntryDto
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string? ServiceId { get; set; }
        public string? Operation { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? Properties { get; set; }
    }
}

