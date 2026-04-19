using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services
{
    public interface IScannerServiceFactory
    {
        IScannerService GetScannerService(string scannerType);
        IEnumerable<string> GetAvailableScannerTypes();
        Task<Dictionary<string, bool>> GetHealthStatusAsync();
    }

    public class ScannerServiceFactory : IScannerServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ScannerServiceFactory> _logger;
        private readonly Dictionary<string, Type> _scannerServiceTypes;

        public ScannerServiceFactory(IServiceProvider serviceProvider, ILogger<ScannerServiceFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            _scannerServiceTypes = new Dictionary<string, Type>
            {
                { "Nuctech", typeof(NickScanCentralImagingPortal.ScannerServices.Nuctech.NuctechScannerService) },
                { "HeimannSmith", typeof(NickScanCentralImagingPortal.ScannerServices.HeimannSmith.HeimannSmithScannerService) }
            };
        }

        public IScannerService GetScannerService(string scannerType)
        {
            _logger.LogInformation("Getting scanner service for type: {ScannerType}", scannerType);

            if (!_scannerServiceTypes.TryGetValue(scannerType, out var serviceType))
            {
                _logger.LogError("Unknown scanner type: {ScannerType}", scannerType);
                throw new ArgumentException($"Unknown scanner type: {scannerType}", nameof(scannerType));
            }

            var service = _serviceProvider.GetService(serviceType) as IScannerService;
            if (service == null)
            {
                _logger.LogError("Failed to resolve scanner service for type: {ScannerType}", scannerType);
                throw new InvalidOperationException($"Failed to resolve scanner service for type: {scannerType}");
            }

            return service;
        }

        public IEnumerable<string> GetAvailableScannerTypes()
        {
            return _scannerServiceTypes.Keys;
        }

        public async Task<Dictionary<string, bool>> GetHealthStatusAsync()
        {
            _logger.LogInformation("Getting health status for all scanner services");

            var healthStatus = new Dictionary<string, bool>();

            foreach (var scannerType in _scannerServiceTypes.Keys)
            {
                try
                {
                    var service = GetScannerService(scannerType);
                    var isHealthy = await service.IsHealthyAsync();
                    healthStatus[scannerType] = isHealthy;

                    _logger.LogInformation("Scanner {ScannerType} health status: {IsHealthy}", scannerType, isHealthy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get health status for scanner type: {ScannerType}", scannerType);
                    healthStatus[scannerType] = false;
                }
            }

            return healthStatus;
        }
    }
}
