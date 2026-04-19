using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.API.Services
{
    /// <summary>
    /// Health check for SSL certificate expiration monitoring
    /// Alerts when certificate expires in less than 30 days
    /// </summary>
    public class CertificateHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CertificateHealthCheck> _logger;

        public CertificateHealthCheck(IConfiguration configuration, ILogger<CertificateHealthCheck> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var certConfig = _configuration.GetSection("SslCertificates:ApiCertificate");
                var thumbprint = Environment.GetEnvironmentVariable("NICKSCAN_API_CERT_THUMBPRINT");

                if (string.IsNullOrEmpty(thumbprint))
                {
                    return Task.FromResult(HealthCheckResult.Healthy(
                        "No SSL certificate configured. Running HTTP-only (internal network)."));
                }

                var storeLocation = certConfig["StoreLocation"] == "LocalMachine"
                    ? StoreLocation.LocalMachine
                    : StoreLocation.CurrentUser;
                var storeName = Enum.Parse<StoreName>(certConfig["StoreName"] ?? "My");

                using var store = new X509Store(storeName, storeLocation);
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

                if (certs.Count == 0)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        "SSL certificate not found in certificate store. Install the certificate or the API will run HTTP-only."));
                }

                var cert = certs[0];
                var daysUntilExpiry = (cert.NotAfter - DateTime.UtcNow).Days;

                if (daysUntilExpiry < 0)
                {
                    _logger.LogError("🔴 Certificate expired {Days} days ago", Math.Abs(daysUntilExpiry));
                    return Task.FromResult(HealthCheckResult.Unhealthy(
                        $"Certificate expired {Math.Abs(daysUntilExpiry)} days ago. Renew immediately!"));
                }

                if (daysUntilExpiry < 30)
                {
                    _logger.LogWarning("⚠️ Certificate expires in {Days} days", daysUntilExpiry);
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"Certificate expires in {daysUntilExpiry} days. Renew soon."));
                }

                _logger.LogInformation("✅ Certificate valid. Expires in {Days} days", daysUntilExpiry);
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Certificate valid. Expires in {daysUntilExpiry} days."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking certificate health");
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Certificate health check failed", ex));
            }
        }
    }
}

