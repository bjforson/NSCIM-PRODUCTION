using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Manifest
{
    /// <summary>
    /// EF Core implementation of <see cref="IManifestSnapshotService"/>. Reads
    /// the manifest from <see cref="IcumDownloadsDbContext"/> and persists a
    /// ManifestSnapshot row into <see cref="ApplicationDbContext"/>.
    ///
    /// Best-effort by design: any failure during the ICUMS read produces a
    /// snapshot with <c>Source = "no_data"</c> instead of throwing, so that
    /// SaveDecision is never blocked by ICUMS connectivity issues. Three outcomes
    /// are possible:
    ///
    ///   - "live_capture" — full manifest copied successfully.
    ///   - "no_data"      — BOE link existed but ICUMS read returned nothing
    ///                      (purged, missing, or read failure). The snapshot row
    ///                      records the attempt and the FK so the gap is visible
    ///                      to training-data curation later.
    ///   - (no row)       — no BOE link existed at all on the completeness row.
    ///                      A future LEFT JOIN at training time will mark this
    ///                      decision as "manifest unknown".
    /// </summary>
    public class ManifestSnapshotService : IManifestSnapshotService
    {
        private readonly ApplicationDbContext _appDb;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<ManifestSnapshotService> _logger;

        // JSON output is intentionally compact and culture-invariant. Snapshots
        // sit in the database for years and are read by future training jobs that
        // may run on completely different machines.
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
        };

        public ManifestSnapshotService(
            ApplicationDbContext appDb,
            IcumDownloadsDbContext icumDb,
            ILogger<ManifestSnapshotService> logger)
        {
            _appDb = appDb;
            _icumDb = icumDb;
            _logger = logger;
        }

        public async Task<ManifestSnapshot?> CaptureAsync(
            int imageAnalysisDecisionId,
            string containerNumber,
            string scannerType,
            CancellationToken cancellationToken = default)
        {
            if (imageAnalysisDecisionId <= 0)
            {
                _logger.LogWarning(
                    "ManifestSnapshotService.CaptureAsync called with invalid decision id {DecisionId}",
                    imageAnalysisDecisionId);
                return null;
            }

            // Resolve the BOE link via the completeness table — that's where
            // ContainerNumber → BOEDocumentId is authoritative inside NSCIM.
            int? boeDocumentId = null;
            try
            {
                boeDocumentId = await _appDb.ContainerCompletenessStatuses
                    .AsNoTracking()
                    .Where(s => s.ContainerNumber == containerNumber)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(s => s.BOEDocumentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ManifestSnapshotService: failed to resolve BOEDocumentId for {Container}",
                    containerNumber);
            }

            if (boeDocumentId is null or 0)
            {
                // No BOE link exists. Don't create a snapshot row at all — the
                // absence of a row is itself meaningful (means the decision was
                // made without manifest context).
                return null;
            }

            BOEDocument? boe = null;
            List<DownloadedManifestItem> items = new();
            string source = "live_capture";

            try
            {
                boe = await _icumDb.BOEDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == boeDocumentId.Value, cancellationToken);

                if (boe != null)
                {
                    items = await _icumDb.ManifestItems
                        .AsNoTracking()
                        .Where(i => i.BOEDocumentId == boe.Id)
                        .OrderBy(i => i.ItemIndex)
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    source = "no_data";
                    _logger.LogInformation(
                        "ManifestSnapshotService: BOEDocumentId {BoeId} not found in ICUMS for {Container}; recording no_data snapshot",
                        boeDocumentId, containerNumber);
                }
            }
            catch (Exception ex)
            {
                source = "no_data";
                _logger.LogWarning(ex,
                    "ManifestSnapshotService: ICUMS read failed for BOE {BoeId} ({Container}); recording no_data snapshot",
                    boeDocumentId, containerNumber);
            }

            var snapshot = BuildSnapshot(
                imageAnalysisDecisionId,
                containerNumber,
                scannerType,
                boeDocumentId,
                boe,
                items,
                source);

            try
            {
                _appDb.ManifestSnapshots.Add(snapshot);
                await _appDb.SaveChangesAsync(cancellationToken);
                return snapshot;
            }
            catch (Exception ex)
            {
                // Persist failure is logged but does not throw — the calling
                // SaveDecision must not be blocked by snapshot infrastructure.
                _logger.LogWarning(ex,
                    "ManifestSnapshotService: failed to persist snapshot for decision {DecisionId} ({Container})",
                    imageAnalysisDecisionId, containerNumber);
                return null;
            }
        }

        private ManifestSnapshot BuildSnapshot(
            int imageAnalysisDecisionId,
            string containerNumber,
            string scannerType,
            int? boeDocumentId,
            BOEDocument? boe,
            List<DownloadedManifestItem> items,
            string source)
        {
            var snapshot = new ManifestSnapshot
            {
                ImageAnalysisDecisionId = imageAnalysisDecisionId,
                BOEDocumentId = boeDocumentId,
                SnapshotTakenAtUtc = DateTime.UtcNow,
                ContainerNumber = containerNumber,
                ScannerType = scannerType,
                Source = source,
            };

            if (boe == null)
            {
                return snapshot;
            }

            // ── Identifiers ─────────────────────────────────────────────────
            snapshot.MasterBlNumber = boe.MasterBlNumber;
            snapshot.HouseBlNumber = boe.HouseBl;
            snapshot.RotationNumber = boe.RotationNumber;
            snapshot.DeclarationNumber = boe.DeclarationNumber;
            snapshot.RegimeCode = boe.RegimeCode;
            snapshot.ClearanceType = boe.ClearanceType;

            // ── Cargo ───────────────────────────────────────────────────────
            snapshot.DeclaredGoodsDescription = boe.GoodsDescription;
            snapshot.DeclaredLineItemCount = items.Count;
            snapshot.IsConsolidated = boe.IsConsolidated;
            snapshot.CrmsLevel = boe.CrmsLevel;
            snapshot.TotalDeclaredDutyPaid = boe.TotalDutyPaid;

            if (items.Count > 0)
            {
                snapshot.DeclaredHsCodesJson = JsonSerializer.Serialize(
                    items.Where(i => !string.IsNullOrWhiteSpace(i.HsCode))
                         .Select(i => i.HsCode)
                         .Distinct()
                         .ToList(),
                    _jsonOptions);

                snapshot.DeclaredQuantitiesJson = JsonSerializer.Serialize(
                    items.Select(i => new
                    {
                        i.HsCode,
                        i.Description,
                        i.Quantity,
                        i.Unit,
                        i.Weight,
                    }).ToList(),
                    _jsonOptions);

                snapshot.DeclaredValuesJson = JsonSerializer.Serialize(
                    items.Select(i => new
                    {
                        i.HsCode,
                        i.ItemFob,
                        i.FobCurrency,
                        i.ItemDutyPaid,
                        i.CountryOfOrigin,
                    }).ToList(),
                    _jsonOptions);

                // Roll up totals (best effort — items may have null fields).
                snapshot.TotalDeclaredFob = items.Sum(i => i.ItemFob ?? 0m);
                snapshot.TotalDeclaredWeight = items.Sum(i => i.Weight ?? 0m);
                snapshot.FobCurrency = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.FobCurrency))
                    .Select(i => i.FobCurrency)
                    .FirstOrDefault();
            }

            // ── Routing & parties ───────────────────────────────────────────
            snapshot.CountryOfOrigin = boe.CountryOfOrigin;
            snapshot.DeliveryPlace = boe.DeliveryPlace;
            snapshot.ImporterName = boe.ImpName ?? boe.ImpExpName;
            snapshot.ImporterAddress = boe.ImpAddress ?? boe.ImpExpAddress;
            snapshot.ConsigneeName = boe.ConsigneeName;
            snapshot.ConsigneeAddress = boe.ConsigneeAddress;
            snapshot.ShipperName = boe.ShipperName;

            // ── Forensic full payload ───────────────────────────────────────
            // Prefer the original raw JSON the ingestion stored if it's there;
            // otherwise round-trip a structured copy of what we just read.
            if (!string.IsNullOrWhiteSpace(boe.RawJsonData))
            {
                snapshot.RawManifestJson = boe.RawJsonData;
            }
            else
            {
                snapshot.RawManifestJson = JsonSerializer.Serialize(
                    new { boe, items },
                    _jsonOptions);
            }

            return snapshot;
        }
    }
}
