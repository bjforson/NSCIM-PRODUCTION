using Microsoft.JSInterop;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Service to track user inactivity and trigger auto-logout after 15 minutes
    /// </summary>
    public class InactivityTimerService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly SimpleAuthStateProvider _authStateProvider;
        private readonly ILogger<InactivityTimerService> _logger;
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(15);
        private Timer? _inactivityTimer;
        private DateTime _lastActivity = DateTime.UtcNow;
        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<InactivityTimerService>? _dotNetReference;
        private bool _isInitialized = false;

        public event Func<Task>? OnInactivityTimeout;

        public InactivityTimerService(
            IJSRuntime jsRuntime,
            SimpleAuthStateProvider authStateProvider,
            ILogger<InactivityTimerService> logger)
        {
            _jsRuntime = jsRuntime;
            _authStateProvider = authStateProvider;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                _dotNetReference = DotNetObjectReference.Create(this);
                
                // Load the JavaScript module for activity tracking
                _jsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./js/inactivityTracker.js");

                // Initialize activity tracking in JavaScript
                await _jsModule.InvokeVoidAsync("initializeActivityTracking", _dotNetReference);

                // Start the inactivity timer
                _inactivityTimer = new Timer(CheckInactivity, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                
                _isInitialized = true;
                _logger.LogInformation("✅ Inactivity timer initialized - timeout set to {Minutes} minutes", _inactivityTimeout.TotalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize inactivity timer");
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

