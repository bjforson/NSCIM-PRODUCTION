using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Sprint 5G3 / audit finding 8.25 — single sink for dashboard alerts that
    /// combines persistence, open-incident deduplication, SignalR broadcast,
    /// and throttled email notification for Critical alerts via NickComms.Gateway.
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
        /// Persist (or touch, on open-incident dedupe hit) the alert and notify
        /// on-call when the rule promotes a previously-unseen alert to
        /// <c>Critical</c> and the configured email cooldown permits it.
        ///
        /// Dedupe rule: an unacknowledged row with the same stable alert key is
        /// considered the same incident. Its <c>RaisedAtUtc</c> and visible
        /// details are updated; no duplicate row is created.
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
