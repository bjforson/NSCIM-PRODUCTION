using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Circuit handler that ensures users are logged out when the server restarts
    /// or when the SignalR connection is lost.
    ///
    /// 6.13 (Sprint 4): OnConnectionUpAsync now verifies the JWT is still valid
    /// after a reconnect and surfaces an expired session via auth-state change.
    /// Previously every override was a no-op LogDebug, so a token that expired
    /// during a connection drop simply continued silently — the user kept hitting
    /// 401s without any visible indicator that re-login was needed.
    ///
    /// Note: NavigationManager is not directly injectable into a scoped CircuitHandler
    /// reliably in Blazor Server (it's per-circuit but the handler runs at the circuit
    /// boundary). Instead we trigger SimpleAuthStateProvider's expiry path which
    /// notifies subscribers; the cascading auth state then redirects via the
    /// AuthorizeRouteView fallback.
    /// </summary>
    public class AuthenticationCircuitHandler : CircuitHandler
    {
        private readonly ILogger<AuthenticationCircuitHandler> _logger;
        private readonly IServiceProvider _serviceProvider;

        public AuthenticationCircuitHandler(
            ILogger<AuthenticationCircuitHandler> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogDebug("🔌 Circuit opened: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogDebug("🔌 Circuit closed: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 6.13: verify the JWT is still valid on reconnect. If expired or invalid,
        /// SimpleAuthStateProvider.GetAuthenticationStateAsync() invokes its own
        /// auto-logout path and notifies authentication-state subscribers, which
        /// the AuthorizeRouteView in App.razor handles by redirecting to /login.
        /// </summary>
        public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogDebug("🔄 Connection reconnected: {CircuitId}", circuit.Id);

            try
            {
                // Resolve the auth state provider from the circuit's scoped container.
                // The fallback to anonymous is fine — if no provider is registered,
                // there's nothing to verify and we shouldn't crash the reconnect path.
                var authProvider = _serviceProvider.GetService<SimpleAuthStateProvider>();
                if (authProvider == null)
                {
                    _logger.LogDebug("OnConnectionUpAsync: SimpleAuthStateProvider not resolved (anonymous circuit)");
                    return;
                }

                // GetAuthenticationStateAsync re-checks the JWT 'exp' claim and triggers
                // LogoutAsync if expired (existing behaviour in SimpleAuthStateProvider).
                // The notification cascades via NotifyAuthenticationStateChanged so the
                // UI's AuthorizeView reacts — including AuthorizeRouteView's redirect.
                var state = await authProvider.GetAuthenticationStateAsync();
                if (state.User?.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("⏰ Token invalid/expired on reconnect for circuit {CircuitId} — auth state cleared", circuit.Id);
                }
                else
                {
                    _logger.LogDebug("✅ Token still valid on reconnect for {Username} (circuit {CircuitId})",
                        state.User.Identity.Name, circuit.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnConnectionUpAsync token verification failed for circuit {CircuitId}", circuit.Id);
            }
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            // This is normal - Blazor Server connections drop and reconnect frequently
            // Only log at Debug level to avoid alarming console output
            _logger.LogDebug("🔌 Connection temporarily lost: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }
}
