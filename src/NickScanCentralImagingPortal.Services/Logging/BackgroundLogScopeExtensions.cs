using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
    /// <summary>
    /// Audit 8.10 (2026-05-05, Sprint 5G2): cycle-scoped CorrelationId helper
    /// for <c>BackgroundService</c> classes hosted inside <c>NSCIM_API</c>.
    ///
    /// Companion to <c>NickScanCentralImagingPortal.API.Logging.BackgroundCycleEnricher</c>:
    /// the enricher provides the <c>"no-cycle"</c> default for log events emitted
    /// outside a scope; this extension method is the per-iteration scope.
    ///
    /// Location note: the spec asked for both files under
    /// <c>NickScanCentralImagingPortal.API/Logging/</c>, but
    /// <c>NickScanCentralImagingPortal.Services</c> does not (and must not)
    /// reference <c>NickScanCentralImagingPortal.API</c>. Background services
    /// live in this assembly, so the extension method lives here too. The
    /// enricher itself remains in API.Logging (it only needs to be visible to
    /// <c>Program.cs</c> for Serilog registration).
    ///
    /// Usage from a <c>BackgroundService.ExecuteAsync</c> loop:
    /// <code>
    /// while (!stoppingToken.IsCancellationRequested)
    /// {
    ///     using var _scope = _logger.BeginCycle(nameof(MyService));
    ///     // existing iteration body
    /// }
    /// </code>
    ///
    /// Mechanism: pushes a <c>CorrelationId</c> property of the form
    /// <c>"{ServiceId}-{Guid:N}"</c> onto the MEL logging scope. Serilog's
    /// <c>UseSerilog()</c> integration bridges <c>ILogger.BeginScope</c>
    /// dictionaries onto <c>Serilog.Context.LogContext</c>, which
    /// <c>Enrich.FromLogContext()</c> then projects into every log event
    /// written inside the scope. The output template's <c>{Properties:j}</c>
    /// segment renders it.
    /// </summary>
    public static class BackgroundLogScopeExtensions
    {
        /// <summary>
        /// Begin a per-cycle correlation scope. Dispose at end of iteration to
        /// pop the property. Safe to call repeatedly; each iteration mints a
        /// fresh GUID so cycles cannot bleed CorrelationIds into each other.
        /// </summary>
        /// <param name="logger">The worker's <see cref="ILogger"/>.</param>
        /// <param name="serviceId">
        /// Stable identifier for the service (typically <c>nameof(MyService)</c>).
        /// Forms the prefix of the rendered CorrelationId so an operator
        /// grepping for "ImageAnalysisOrchestratorService-" can isolate one
        /// worker's iterations.
        /// </param>
        /// <returns>
        /// An <see cref="IDisposable"/> that pops the CorrelationId off the
        /// scope when disposed. Never null.
        /// </returns>
        public static IDisposable BeginCycle(this ILogger logger, string serviceId)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            // Service-id can legitimately be empty (defensive): we still want
            // a unique cycle id so iterations are still separable in the file
            // sink.
            var safeServiceId = string.IsNullOrWhiteSpace(serviceId) ? "worker" : serviceId.Trim();
            var correlationId = $"{safeServiceId}-{Guid.NewGuid():N}";

            // BeginScope returns NullScope when no provider implements scopes
            // (e.g. tests with NullLogger). The "?? NoOpDisposable.Instance"
            // guard ensures the using-pattern at the call site never hits a
            // NullReferenceException on Dispose, regardless of provider.
            return logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
            }) ?? NoOpDisposable.Instance;
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
