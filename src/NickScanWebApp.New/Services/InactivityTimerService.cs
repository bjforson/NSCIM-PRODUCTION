using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Service to track user inactivity and trigger auto-logout after a configurable timeout
    /// </summary>
    public class InactivityTimerService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly SimpleAuthStateProvider _authStateProvider;
        private readonly ILogger<InactivityTimerService> _logger;
        private readonly TimeSpan _inactivityTimeout;
        private readonly bool _enabled = true;
        private readonly int _checkIntervalSeconds;
        private Timer? _inactivityTimer;
        private DateTime _lastActivity = DateTime.UtcNow;
        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<InactivityTimerService>? _dotNetReference;
        private bool _isInitialized = false;

        public event Func<Task>? OnInactivityTimeout;

        public InactivityTimerService(
            IJSRuntime jsRuntime,
            SimpleAuthStateProvider authStateProvider,
            ILogger<InactivityTimerService> logger,
            IConfiguration configuration)
        {
            _jsRuntime = jsRuntime;
            _authStateProvider = authStateProvider;
            _logger = logger;

            // Read timeout and enable flag from configuration so behavior is tunable without code changes
            // Authentication:InactivityTimeoutMinutes = 0 or negative → disable auto-logout
            var timeoutMinutes = configuration.GetValue<int?>("Authentication:InactivityTimeoutMinutes") ?? 60;
            var enableFlag = configuration.GetValue<bool?>("Authentication:EnableInactivityLogout") ?? true;

            _checkIntervalSeconds = configuration.GetValue<int>("Authentication:InactivityCheckIntervalSeconds", 10);

            if (!enableFlag || timeoutMinutes <= 0)
            {
                _enabled = false;
                _inactivityTimeout = TimeSpan.Zero;
                _logger.LogInformation("⏱️ Inactivity auto-logout is DISABLED via configuration");
            }
            else
            {
                _inactivityTimeout = TimeSpan.FromMinutes(timeoutMinutes);
                _logger.LogInformation("✅ Inactivity timer configured - timeout set to {Minutes} minutes, check interval {Seconds}s", _inactivityTimeout.TotalMinutes, _checkIntervalSeconds);
            }
        }

        public async Task InitializeAsync()
        {
            if (!_enabled)
            {
                _logger.LogDebug("Inactivity timer initialization skipped (disabled)");
                return;
            }

            if (_isInitialized) return;

            try
            {
                _dotNetReference = DotNetObjectReference.Create(this);

                // ✅ FIX: Handle TaskCanceledException gracefully (expected during prerendering or if JS isn't ready)
                // Load the JavaScript module for activity tracking
                _jsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./js/inactivityTracker.js");

                // Initialize activity tracking in JavaScript (pass timeout so JS can enforce client-side)
                await _jsModule.InvokeVoidAsync("initializeActivityTracking", _dotNetReference, (int)_inactivityTimeout.TotalMilliseconds);

                // Start the inactivity timer
                _inactivityTimer = new Timer(CheckInactivity, null, TimeSpan.FromSeconds(_checkIntervalSeconds), TimeSpan.FromSeconds(_checkIntervalSeconds));

                _isInitialized = true;
                _logger.LogInformation("✅ Inactivity timer initialized");
            }
            catch (TaskCanceledException)
            {
                // ✅ FIX: TaskCanceledException is expected during prerendering or if JS interop isn't ready yet
                // This is not a critical error - the timer will be retried when JS becomes available
                _logger.LogDebug("Inactivity timer initialization canceled (JS interop not ready yet) - will retry on next render");
                // Reset initialization flag to allow retry
                _isInitialized = false;
                // Clean up partial initialization
                _dotNetReference?.Dispose();
                _dotNetReference = null;
            }
            catch (JSDisconnectedException)
            {
                // ✅ FIX: JS disconnected - expected if circuit is disconnected
                _logger.LogDebug("Inactivity timer initialization skipped (JS disconnected)");
                _isInitialized = false;
                _dotNetReference?.Dispose();
                _dotNetReference = null;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop"))
            {
                // ✅ FIX: JS interop not available (prerendering) - expected and will retry
                _logger.LogDebug("Inactivity timer initialization skipped (JS interop not available) - will retry on next render");
                _isInitialized = false;
                _dotNetReference?.Dispose();
                _dotNetReference = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize inactivity timer");
                // Don't reset _isInitialized for other exceptions - prevent infinite retry loops
            }
        }

        /// <summary>
        /// Called from JavaScript when user activity is detected
        /// </summary>
        [JSInvokable]
        public void RecordActivity()
        {
            _lastActivity = DateTime.UtcNow;
            _logger.LogDebug("User activity recorded at {Time}", _lastActivity);
        }

        // ✅ FIX: Timer callback must be void, but we wrap async work in Task.Run for proper exception handling
        private void CheckInactivity(object? state)
        {
            // Use Task.Run to properly handle async work and exceptions in timer callback
            _ = Task.Run(async () =>
            {
                try
                {
                    var inactiveDuration = DateTime.UtcNow - _lastActivity;

                    if (inactiveDuration >= _inactivityTimeout)
                    {
                        _logger.LogWarning("⏰ User inactive for {Minutes} minutes - triggering auto-logout", inactiveDuration.TotalMinutes);

                        // Stop the timer
                        _inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                        // Logout the user
                        await _authStateProvider.LogoutAsync();

                        // Trigger the event
                        if (OnInactivityTimeout != null)
                        {
                            await OnInactivityTimeout.Invoke();
                        }
                    }
                    else
                    {
                        var remainingMinutes = (_inactivityTimeout - inactiveDuration).TotalMinutes;
                        if (remainingMinutes <= 2)
                        {
                            _logger.LogDebug("⏲️  {Minutes:F1} minutes until auto-logout", remainingMinutes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking inactivity");
                }
            });
        }

        public void ResetTimer()
        {
            _lastActivity = DateTime.UtcNow;
            _logger.LogDebug("Inactivity timer reset");
        }

        public async ValueTask DisposeAsync()
        {
            _inactivityTimer?.Dispose();

            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("cleanup");
                    await _jsModule.DisposeAsync();
                }
                catch { /* Ignore cleanup errors */ }
            }

            _dotNetReference?.Dispose();
        }
    }
}

