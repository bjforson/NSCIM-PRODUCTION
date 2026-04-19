using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Services.Manifest
{
    /// <summary>
    /// Captures a frozen-in-time snapshot of the ICUMS manifest at the moment an
    /// analyst makes an inspection decision. See ManifestSnapshot for the full
    /// rationale; in short, NSCIM's link to manifest data is via a foreign key
    /// into a mutable external database, so the manifest must be copied at
    /// decision time if any future training pipeline is to reproduce the
    /// (image, manifest, decision) triple.
    ///
    /// Captures are best-effort: a missing BOE link or a missing ICUMS row
    /// produces a snapshot row with Source = "no_data" so the attempt is
    /// recorded, but never blocks the underlying SaveDecision call.
    /// </summary>
    public interface IManifestSnapshotService
    {
        /// <summary>
        /// Resolve the manifest for the given container/scanner/decision and
        /// persist a ManifestSnapshot row linked to <paramref name="imageAnalysisDecisionId"/>.
        /// Idempotent in spirit: callers should call this once per decision save;
        /// repeated calls will create additional snapshot rows (intentional —
        /// each save captures the manifest as it stood at that moment).
        /// </summary>
        /// <returns>The persisted ManifestSnapshot, or <c>null</c> if no row could
        /// be created (e.g. the decision id is invalid). Failures during ICUMS
        /// reads are absorbed and reported via the snapshot's <c>Source</c>
        /// field, not by returning null.</returns>
        Task<ManifestSnapshot?> CaptureAsync(
            int imageAnalysisDecisionId,
            string containerNumber,
            string scannerType,
            CancellationToken cancellationToken = default);
    }
}
