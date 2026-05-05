using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Sprint 5G3 / audit finding 8.25 — single sink for dashboard alerts that
    /// combines persistence, deduplication, SignalR broadcast, and (for
    /// <c>Critical</c> severity) email notification via NickComms.Gateway.
    ///
    /// Replaces the earlier in-memory-only flow inside
    /// <c>ImageAnalysisDashboardBroadcastService.GetCurrentAlertsAsync</c>:
    /// alerts now survive operator absence, off-hours incidents reach on-call,
    /// and the table backs the future "alerts history" admin UI (a separate
    /// sprint).
    ///
    /// Lifetime: scoped — pulls <c>ApplicationDbContext</c> + transient
    /// <c>INickCommsClient</c> from DI; do not cache across iterations.
    /// </summary>
    public interface IDashboardAlertService
    {
        /// <summary>
        /// Persist (or touch, on dedupe hit) the alert and notify on-call when
        /// the rule promotes a previously-unseen alert to <c>Critical</c>.
        ///
        /// Dedupe rule: an existing row with the same <c>(Type, Title)</c>
        /// raised within the last 30 minutes is considered the same incident.
        /// Its <c>RaisedAtUtc</c> is updated; nothing else is sent.
        ///
        /// Returns the persisted entity so callers can echo Id / EmailSentAtUtc
        /// to SignalR clients without a re-read.
        /// </summary>
        Task<DashboardAlertEntity> RaiseAsync(
            string type,
            string severity,
            string title,
            string? description,
            string? source,
            CancellationToken ct = default);

        /// <summary>
        /// Mark an alert acknowledged by the calling operator. Idempotent —
        /// repeat calls return the row unchanged once it's already acknowledged.
        /// Returns null if the alert id is unknown / out of tenant scope.
        /// </summary>
        Task<DashboardAlertEntity?> AcknowledgeAsync(int alertId, string acknowledgedBy, CancellationToken ct = default);
    }
}
