using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class ScannerStatus
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public ScannerState State { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public ScannerConfiguration? Configuration { get; set; }
        public ScannerStatistics? Statistics { get; set; }
    }

    public enum ScannerState
    {
        Offline = 0,
        Online = 1,
        Scanning = 2,
        Error = 3,
        Maintenance = 4,
        Idle = 5
    }

    public class ScannerConfiguration
    {
        public int ScannerId { get; set; }
        public int Resolution { get; set; } = 300; // DPI
        public string ColorMode { get; set; } = "Color"; // Color, Grayscale, BlackWhite
        public string PaperSize { get; set; } = "A4";
        public int Brightness { get; set; } = 0;
        public int Contrast { get; set; } = 0;
        public bool AutoFeed { get; set; } = true;
        public bool DuplexScanning { get; set; } = false;
        public string OutputFormat { get; set; } = "PDF";
        public int CompressionQuality { get; set; } = 85;
        public bool AutoRotate { get; set; } = true;
        public bool AutoDeskew { get; set; } = true;
        public bool AutoCrop { get; set; } = false;
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public class ScannerStatistics
    {
        public int ScannerId { get; set; }
        public int TotalScans { get; set; }
        public int ScansToday { get; set; }
        public int ScansThisWeek { get; set; }
        public int ScansThisMonth { get; set; }
        public double AverageScanTime { get; set; } // in seconds
        public double TotalScanTime { get; set; } // in hours
        public DateTime LastScanTime { get; set; }
        public int ErrorCount { get; set; }
        public double UptimePercentage { get; set; }
        public DateTime StatisticsDate { get; set; }
    }

    public class ScannerLog
    {
        public int Id { get; set; }
        public int ScannerId { get; set; }
        public string Level { get; set; } = string.Empty; // Info, Warning, Error, Debug
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    public class ConnectionTestResult
    {
        public bool IsConnected { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public double ResponseTime { get; set; } // in milliseconds
        public DateTime TestTime { get; set; }
        public Dictionary<string, string> AdditionalInfo { get; set; } = new();
    }

    public class ScannerOperationRequest
    {
        [Required]
        public int ScannerId { get; set; }

        [Required]
        public string Operation { get; set; } = string.Empty; // Start, Stop, Restart, Test

        public Dictionary<string, object> Parameters { get; set; } = new();

        public string UserId { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    public class ScannerOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}
