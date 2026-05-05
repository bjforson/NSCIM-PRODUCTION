using Serilog.Core;
using Serilog.Events;

namespace NickScanCentralImagingPortal.API.Logging
{
    /// <summary>
    /// Audit 8.10 (2026-05-05, Sprint 5G2): per-cycle CorrelationId enricher for
    /// background-worker logs.
    ///
    /// Background: NSCIM_API hosts 35+ <c>BackgroundService</c> classes. Their log
    /// entries never carry CorrelationId because they don't enter the HTTP request
    /// pipeline (where <c>CorrelationIdMiddleware</c> calls <c>BeginScope</c>).
    /// The result: tracing a "scan came in -> BOE matched -> CCS row -> AG created
    /// -> assignment -> decision" sequence across the in-process workers requires
    /// manual timestamp correlation; there is no shared key.
    ///
    /// How it works: a worker wraps each <c>ExecuteAsync</c> iteration in
    /// <c>using var _ = _logger.BeginCycle(nameof(MyService))</c> (see
    /// <c>BackgroundLogScopeExtensions</c> in <c>NickScanCentralImagingPortal.Services.Logging</c>).
    /// The scope pushes a <c>CorrelationId</c> property of the form
    /// <c>"{ServiceId}-{Guid:N}"</c> onto the MEL logging scope, which Serilog
    /// projects via <c>Enrich.FromLogContext()</c> into every log event written
    /// inside that scope.
    ///
    /// What this enricher does: when a log event is emitted OUTSIDE any cycle scope
    /// (e.g. raw startup/shutdown lines, controller lines, or an early-return
    /// before <c>BeginCycle</c>), the property is missing entirely. To make
    /// "outside any worker cycle" visually distinct from "inside a cycle whose
    /// CorrelationId happens to render empty," this enricher stamps a literal
    /// <c>"no-cycle"</c> default. Operators reading the file sink can then grep
    /// for <c>CorrelationId="no-cycle"</c> vs <c>CorrelationId="{Service}-..."</c>
    /// to distinguish HTTP/controller logs from worker logs.
    ///
    /// Registration: see <c>Program.cs</c> Serilog configuration block. The
    /// <c>FromLogContext()</c> enricher must run BEFORE this one so that scope
    /// pushes win over the default.
    /// </summary>
    public sealed class BackgroundCycleEnricher : ILogEventEnricher
    {
        public const string CorrelationIdProperty = "CorrelationId";
        public const string DefaultValue = "no-cycle";

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent is null)
            {
                return;
            }

            // Don't clobber a CorrelationId that was already pushed onto the
            // LogContext by an HTTP-pipeline scope (CorrelationIdMiddleware) or
            // by a worker's BeginCycle call. Serilog.Context.LogContext properties
            // attach to the event before enrichers run, so by the time we're here,
            // a present property is authoritative.
            if (logEvent.Properties.ContainsKey(CorrelationIdProperty))
            {
                return;
            }

            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(CorrelationIdProperty, DefaultValue));
        }
    }
}
