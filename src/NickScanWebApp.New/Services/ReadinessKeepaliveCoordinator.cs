using System.Collections.Concurrent;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Phase B / B7-C (2026-05-09): scoped per-circuit coordination service so the
    /// layout-level <c>ReadinessKeepalive</c> component can detect whether a page
    /// (AuditReview, Workbench) on the SAME circuit already owns its own readiness
    /// hub for a given role. When a page registers itself for a role, the keepalive
    /// component's heartbeat tick skips that role — no double SignalR connection,
    /// no duplicate heartbeats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifetime: <b>Scoped</b>. Each Blazor circuit gets its own instance, so two
    /// users on different browsers don't see each other's flags. Within a single
    /// circuit, the layout and any page share the same instance.
    /// </para>
    /// <para>
    /// Why a service rather than a cascading parameter: the layout-level keepalive
    /// component starts BEFORE the page renders, so a cascading parameter set by
    /// the page wouldn't be visible at layout init time. By the time the keepalive's
    /// 30 s timer ticks, however, the page's <c>OnInitializedAsync</c> has long since
    /// run and called <see cref="MarkPageHubActive"/> — at which point the timer
    /// callback consults this service and no-ops for the page-owned role.
    /// </para>
    /// <para>
    /// Concurrency: the underlying <see cref="ConcurrentDictionary{TKey, TValue}"/>
    /// is safe for the dispose race (page Dispose vs. keepalive timer tick).
    /// </para>
    /// </remarks>
    public class ReadinessKeepaliveCoordinator
    {
        private readonly ConcurrentDictionary<string, byte> _activeRoleHubs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Called by AuditReview / Workbench during <c>OnInitializedAsync</c>
        /// (passing <c>true</c>) and during <c>Dispose</c> (passing <c>false</c>).
        /// </summary>
        public void MarkPageHubActive(string role, bool active)
        {
            if (string.IsNullOrWhiteSpace(role)) return;
            if (active)
            {
                _activeRoleHubs[role] = 1;
            }
            else
            {
                _activeRoleHubs.TryRemove(role, out _);
            }
        }

        /// <summary>
        /// Returns <c>true</c> when a page on this circuit owns the readiness hub
        /// for <paramref name="role"/>. The keepalive component skips the role when so.
        /// </summary>
        public bool IsPageHubActive(string role)
            => !string.IsNullOrWhiteSpace(role) && _activeRoleHubs.ContainsKey(role);
    }
}
