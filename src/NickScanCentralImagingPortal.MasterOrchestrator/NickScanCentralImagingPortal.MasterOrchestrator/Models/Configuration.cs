using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace NickScanCentralImagingPortal.MasterOrchestrator.Models;

public class MasterOrchestratorConfig
{
    public ServiceConfig ApiService { get; set; } = new();
    public ServiceConfig WebAppService { get; set; } = new();
    public HealthCheckConfig HealthCheck { get; set; } = new();
    public RetryPolicyConfig RetryPolicy { get; set; } = new();
    public bool AutoRestart { get; set; } = true;
}

public class ServiceConfig
{
    [Required]
    public int Port { get; set; }
    
    [Required]
    public string ProjectPath { get; set; } = string.Empty;
    
    public string HealthEndpoint { get; set; } = "/health";
    
    public int StartupTimeoutSeconds { get; set; } = 60;
}

public class HealthCheckConfig
{
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 10;
}

public class RetryPolicyConfig
{
    public int MaxAttempts { get; set; } = 5;
    public int BaseDelaySeconds { get; set; } = 5;
    public int MaxDelaySeconds { get; set; } = 300;
    public double BackoffMultiplier { get; set; } = 2.0;
}

public enum ServiceStatus
{
    Starting,
    Running,
    Unhealthy,
    Stopped,
    Failed
}

public class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public ServiceStatus Status { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public int ConsecutiveFailures { get; set; }
    public Process? Process { get; set; }
    public string Url { get; set; } = string.Empty;
}
