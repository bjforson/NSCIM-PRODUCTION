namespace NickScanCentralImagingPortal.Services.ASE
{
    public class AseConfiguration
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "networking";
        public string ServerHost { get; set; } = "10.0.0.3";
        public string Username { get; set; } = "cias";
        public string Password { get; set; } = string.Empty;
        public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(30);
        public int BatchSize { get; set; } = 100;
        public DateTime StartDate { get; set; } = new DateTime(2025, 9, 1);
        public bool EnableRealTimeSync { get; set; } = true;
    }
}
