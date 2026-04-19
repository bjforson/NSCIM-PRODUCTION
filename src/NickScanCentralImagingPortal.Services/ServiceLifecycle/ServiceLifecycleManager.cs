using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ServiceLifecycle
{
    /// <summary>
    /// Manages the lifecycle of background services, providing restart, stop, and start capabilities
    /// </summary>
    public class ServiceLifecycleManager : IServiceLifecycleManager
    {
        private readonly ILogger<ServiceLifecycleManager> _logger;
        private readonly ServiceLifecycleOptions _options;
        private readonly Dictionary<string, IManagedService> _services = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly Dictionary<string, DateTime> _lastRestartTimes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TimeSpan> _serviceUptimes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _serviceStartTimes = new(StringComparer.OrdinalIgnoreCase);

        public ServiceLifecycleManager(
            ILogger<ServiceLifecycleManager> logger,
            IOptions<ServiceLifecycleOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public void RegisterService(IManagedService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (string.IsNullOrWhiteSpace(service.Name))
                throw new ArgumentException("Service name cannot be null or empty", nameof(service));

            lock (_services)
            {
                if (_services.ContainsKey(service.Name))
                {
                    _logger.LogWarning("[ServiceLifecycle] Service {ServiceName} is already registered, replacing existing registration", service.Name);
                }

                _services[service.Name] = service;
                _logger.LogInformation("[ServiceLifecycle] Registered service: {ServiceName}", service.Name);
            }
        }

        public IManagedService? GetService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return null;

            lock (_services)
            {
                return _services.TryGetValue(serviceName, out var service) ? service : null;
            }
        }

        public ServiceStatus GetServiceStatus(string serviceName)
        {
            var service = GetService(serviceName);
            if (service == null)
            {
                _logger.LogWarning("[ServiceLifecycle] Service {ServiceName} not found", serviceName);
                return ServiceStatus.Stopped;
            }

            return service.Status;
        }

        public Dictionary<string, ServiceStatus> GetAllServiceStatuses()
        {
            lock (_services)
            {
                return _services.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Status);
            }
        }

        public async Task<bool> RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("[ServiceLifecycle] Restart requested for service: {ServiceName}", serviceName);

                var service = GetService(serviceName);
                if (service == null)
                {
                    _logger.LogError("[ServiceLifecycle] Service {ServiceName} not found", serviceName);
                    return false;
                }

                // Check if service is already restarting
                if (service.Status == ServiceStatus.Restarting)
                {
                    _logger.LogWarning("[ServiceLifecycle] Service {ServiceName} is already restarting", serviceName);
                    return false;
                }

                var oldStatus = service.Status;
                _logger.LogInformation("[ServiceLifecycle] {ServiceName} status: {OldStatus} → Restarting", serviceName, oldStatus);

                var restartStartTime = DateTime.UtcNow;

                try
                {
                    await service.RestartAsync(cancellationToken);

                    _lastRestartTimes[serviceName] = restartStartTime;
                    var restartDuration = DateTime.UtcNow - restartStartTime;

                    _logger.LogInformation("[ServiceLifecycle] {ServiceName} restarted successfully in {Duration}ms",
                        serviceName, restartDuration.TotalMilliseconds);

                    // Update uptime tracking
                    if (service.Status == ServiceStatus.Running)
                    {
                        _serviceStartTimes[serviceName] = DateTime.UtcNow;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ServiceLifecycle] Error restarting service {ServiceName}", serviceName);
                    return false;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<bool> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("[ServiceLifecycle] Stop requested for service: {ServiceName}", serviceName);

                var service = GetService(serviceName);
                if (service == null)
                {
                    _logger.LogError("[ServiceLifecycle] Service {ServiceName} not found", serviceName);
                    return false;
                }

                if (service.Status == ServiceStatus.Stopped || service.Status == ServiceStatus.Stopping)
                {
                    _logger.LogWarning("[ServiceLifecycle] Service {ServiceName} is already stopped or stopping", serviceName);
                    return service.Status == ServiceStatus.Stopped;
                }

                try
                {
                    await service.StopAsync(cancellationToken);
                    _logger.LogInformation("[ServiceLifecycle] {ServiceName} stopped successfully", serviceName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ServiceLifecycle] Error stopping service {ServiceName}", serviceName);
                    return false;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<bool> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("[ServiceLifecycle] Start requested for service: {ServiceName}", serviceName);

                var service = GetService(serviceName);
                if (service == null)
                {
                    _logger.LogError("[ServiceLifecycle] Service {ServiceName} not found", serviceName);
                    return false;
                }

                if (service.Status == ServiceStatus.Running || service.Status == ServiceStatus.Starting)
                {
                    _logger.LogWarning("[ServiceLifecycle] Service {ServiceName} is already running or starting", serviceName);
                    return service.Status == ServiceStatus.Running;
                }

                try
                {
                    await service.StartAsync(cancellationToken);
                    _serviceStartTimes[serviceName] = DateTime.UtcNow;
                    _logger.LogInformation("[ServiceLifecycle] {ServiceName} started successfully", serviceName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ServiceLifecycle] Error starting service {ServiceName}", serviceName);
                    return false;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<bool> IsServiceHealthyAsync(string serviceName)
        {
            var service = GetService(serviceName);
            if (service == null)
                return false;

            try
            {
                return await service.IsHealthyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ServiceLifecycle] Error checking health for service {ServiceName}", serviceName);
                return false;
            }
        }

        public DateTime? GetLastRestartTime(string serviceName)
        {
            return _lastRestartTimes.TryGetValue(serviceName, out var time) ? time : null;
        }

        public TimeSpan? GetServiceUptime(string serviceName)
        {
            if (!_serviceStartTimes.TryGetValue(serviceName, out var startTime))
                return null;

            var service = GetService(serviceName);
            if (service?.Status != ServiceStatus.Running)
                return null;

            return DateTime.UtcNow - startTime;
        }
    }

    /// <summary>
    /// Configuration options for service lifecycle management
    /// </summary>
    public class ServiceLifecycleOptions
    {
        public int RestartTimeoutSeconds { get; set; } = 30;
        public int StopTimeoutSeconds { get; set; } = 10;
        public int StartTimeoutSeconds { get; set; } = 15;
        public bool EnableAutoRecovery { get; set; } = false;
        public int MaxRestartAttempts { get; set; } = 3;
    }
}

