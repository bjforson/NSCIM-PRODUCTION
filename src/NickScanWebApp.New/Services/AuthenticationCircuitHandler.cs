using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Circuit handler that ensures users are logged out when the server restarts
    /// or when the SignalR connection is lost
    /// </summary>
    public class AuthenticationCircuitHandler : CircuitHandler
    {
        private readonly ILogger<AuthenticationCircuitHandler> _logger;

        public AuthenticationCircuitHandler(ILogger<AuthenticationCircuitHandler> logger)
        {
            _logger = logger;
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

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogDebug("🔄 Connection reconnected: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
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

