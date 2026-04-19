using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ServiceLifecycle
{
    /// <summary>
    /// Wrapper that makes any IHostedService manageable through IManagedService interface
    /// </summary>
    /// <typeparam name="T">The type of IHostedService to wrap</typeparam>
    public class ManagedHostedService<T> : IHostedService, IManagedService
        where T : class, IHostedService
    {
        private readonly T _wrappedService;
        private readonly ILogger<ManagedHostedService<T>> _logger;
        private readonly string _serviceName;
        private ServiceStatus _status = ServiceStatus.Stopped;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _statusLock = new object();
        private DateTime? _startTime;
        private DateTime? _lastRestartTime;

        public ManagedHostedService(
            T wrappedService,
            ILogger<ManagedHostedService<T>> logger)
        {
            _wrappedService = wrappedService ?? throw new ArgumentNullException(nameof(wrappedService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceName = typeof(T).Name;
        }

        public string Name => _serviceName;

        public ServiceStatus Status
        {
            get
            {
                lock (_statusLock)
                {
                    return _status;
                }
            }
            private set
            {
                lock (_statusLock)
                {
                    var oldStatus = _status;
                    _status = value;
                    if (oldStatus != value)
                    {
                        _logger.LogDebug("[ManagedHostedService] {ServiceName} status changed: {OldStatus} → {NewStatus}",
                            _serviceName, oldStatus, value);
                    }
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Status == ServiceStatus.Running || Status == ServiceStatus.Starting)
            {
                _logger.LogWarning("[ManagedHostedService] {ServiceName} is already running or starting", _serviceName);
                return;
            }

            try
            {
                Status = ServiceStatus.Starting;
                _cancellationTokenSource = new CancellationTokenSource();
                _startTime = DateTime.UtcNow;

                await _wrappedService.StartAsync(cancellationToken);

                Status = ServiceStatus.Running;
                _logger.LogInformation("[ManagedHostedService] {ServiceName} started successfully", _serviceName);
            }
            catch (Exception ex)
            {
                Status = ServiceStatus.Failed;
                _logger.LogError(ex, "[ManagedHostedService] Error starting {ServiceName}", _serviceName);
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Status == ServiceStatus.Stopped || Status == ServiceStatus.Stopping)
            {
                _logger.LogWarning("[ManagedHostedService] {ServiceName} is already stopped or stopping", _serviceName);
                return;
            }

            try
            {
                Status = ServiceStatus.Stopping;

                await _wrappedService.StopAsync(cancellationToken);

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _startTime = null;

                Status = ServiceStatus.Stopped;
                _logger.LogInformation("[ManagedHostedService] {ServiceName} stopped successfully", _serviceName);
            }
            catch (Exception ex)
            {
                Status = ServiceStatus.Failed;
                _logger.LogError(ex, "[ManagedHostedService] Error stopping {ServiceName}", _serviceName);
                throw;
            }
        }

        public async Task RestartAsync(CancellationToken cancellationToken)
        {
            if (Status == ServiceStatus.Restarting)
            {
                _logger.LogWarning("[ManagedHostedService] {ServiceName} is already restarting", _serviceName);
                return;
            }

            try
            {
                Status = ServiceStatus.Restarting;
                _lastRestartTime = DateTime.UtcNow;

                _logger.LogInformation("[ManagedHostedService] Restarting {ServiceName}...", _serviceName);

                // Stop the service
                await StopAsync(cancellationToken);

                // Brief pause to allow cleanup
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                // Start the service
                await StartAsync(cancellationToken);

                _logger.LogInformation("[ManagedHostedService] {ServiceName} restarted successfully", _serviceName);
            }
            catch (Exception ex)
            {
                Status = ServiceStatus.Failed;
                _logger.LogError(ex, "[ManagedHostedService] Error restarting {ServiceName}", _serviceName);
                throw;
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // Basic health check: service is running
                if (Status != ServiceStatus.Running)
                    return false;

                // If the wrapped service implements IManagedService, use its health check
                if (_wrappedService is IManagedService managedService)
                {
                    return await managedService.IsHealthyAsync();
                }

                // Default: if running, consider healthy
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ManagedHostedService] Error checking health for {ServiceName}", _serviceName);
                return false;
            }
        }

        public DateTime? GetStartTime() => _startTime;
        public DateTime? GetLastRestartTime() => _lastRestartTime;
    }
}

