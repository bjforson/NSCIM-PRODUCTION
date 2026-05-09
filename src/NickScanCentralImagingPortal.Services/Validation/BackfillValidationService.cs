using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ContainerValidation;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Resilience item 2 (2026-05-09) — periodically re-applies the FYCO direction
    /// rule and the port-match rule to all active Primary <c>ContainerBOERelations</c>
    /// rows so violations created BEFORE a rule was activated still surface.
    ///
    /// Background — both rules are feature-flagged behind
    /// <c>IcumIngestion:EnablePortAssignmentRule</c> and
    /// <c>IcumIngestion:EnableFycoImportExportRule</c>, both flipped to TRUE on
    /// 2026-05-02. Any container relation created before that date never had the
    /// rule fire against it; on 2026-05-09 a probe found 4 pre-flip FYCO=EXPORT
    /// vs IMPORT-regime BOE relations sitting live in production. They were
    /// flagged manually via <c>C:\temp\nscim-probe\B7FycoFlag.cs</c>. This
    /// service productionises that flow so the next rule activation cannot leave
    /// silent legacy violations sitting in the table for a week before someone
    /// happens to run a probe.
    ///
    /// Behaviour:
    /// - Runs every <c>Validation:BackfillIntervalHours</c> hours (default 24).
    /// - Reads all active Primary <c>ContainerBOERelations</c>; for each, looks
    ///   up the latest <c>FS6000Scan.FycoPresent</c> and the canonical
    ///   <c>BOEDocument</c> via the same <c>CanonicalBoeQuery</c> the runtime
    ///   gate uses (audit 3.06). Mirrors the parser used by the live rule
    ///   (<see cref="FycoClassifier.IsExport"/> for FYCO; <c>RegimeDirectionMap</c>
    ///   for direction; substring at positions 3-5 for delivery-place port).
    /// - Output mode is FLAG-ONLY. We DO NOT deactivate the relation, write to
    ///   <c>fycopresent</c>, or alter scanner data. The only side-effect is a
    ///   <c>dashboardalerts</c> row of type <c>FycoDirectionMismatch</c> or
    ///   <c>PortMismatch</c> at <c>Severity=Warning</c>. Idempotent — skipped
    ///   if there is already an unacknowledged alert for the same container +
    ///   rule type (matched via the <c>(Type, Title)</c> dedupe key already
    ///   used by <c>IDashboardAlertService.RaiseAsync</c>).
    /// - Disable via <c>Validation:BackfillEnabled=false</c>. Default true.
    /// - The flags <c>IcumIngestion:EnablePortAssignmentRule</c> and
    ///   <c>IcumIngestion:EnableFycoImportExportRule</c> are honoured: a rule
    ///   that's switched off in the runtime gate is also skipped here, so the
    ///   sweep cannot create alerts for a rule the operator deliberately
    ///   silenced.
    ///
    /// Lifecycle: registered as a hosted service via
    /// <c>builder.Services.AddHostedService&lt;BackfillValidationService&gt;()</c>
    /// in <c>Program.cs</c>. Pulls a fresh scope per cycle so the two
    /// <c>DbContext</c> instances (<c>ApplicationDbContext</c> for production
    /// data + <c>IcumDownloadsDbContext</c> for BOE) are reusable for the next
    /// cycle without leaking state. Starts after a short delay (60s) so it
    /// doesn't pile onto the orchestrator's bootstrap.
    /// </summary>
    public class BackfillValidationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackfillValidationService> _logger;
        private readonly IConfiguration _configuration;

        // Initial delay before the first cycle so the orchestrator's
        // bootstrapper + intake have settled. Subsequent cycles run at
        // _interval cadence regardless of how long the cycle took.
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

        // Per-cycle batch processing — keep the pull off the hot path.
        private const int RelationBatchSize = 500;

        public BackfillValidationService(
            IServiceScopeFactory scopeFactory,
            ILogger<BackfillValidationService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("Validation:BackfillEnabled", true);
            if (!enabled)
            {
                _logger.LogInformation("[BACKFILL-VALIDATION] disabled via Validation:BackfillEnabled=false; service exiting.");
                return;
            }

            var intervalHours = _configuration.GetValue<int>("Validation:BackfillIntervalHours", 24);
            if (intervalHours < 1) intervalHours = 1;
            var interval = TimeSpan.FromHours(intervalHours);

            _logger.LogInformation(
                "[BACKFILL-VALIDATION] starting; first cycle in {StartupDelay}, then every {Interval}",
                StartupDelay, interval);

            try { await Task.Delay(StartupDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Cycle-level failure must not kill the background loop —
                    // the Pattern matches AiCargoSummary / CMRRedownload services
                    // (log, swallow, continue at the next interval).
                    _logger.LogError(ex, "[BACKFILL-VALIDATION] error during cycle; continuing at next interval");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[BACKFILL-VALIDATION] stopped");
        }

        /// <summary>
        /// One sweep — public so unit-test-equivalent callers can invoke a single
        /// cycle without spinning up the full BackgroundService loop. Mirrors the
        /// blast-radius probe at <c>C:\temp\nscim-probe\B7FycoBlastRadius.cs</c>.
        /// </summary>
        public async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            var portRuleEnabled = _configuration.GetValue<bool>("IcumIngestion:EnablePortAssignmentRule", false);
            var fycoRuleEnabled = _configuration.GetValue<bool>("IcumIngestion:EnableFycoImportExportRule", false);

            if (!portRuleEnabled && !fycoRuleEnabled)
            {
                _logger.LogInformation(
                    "[BACKFILL-VALIDATION] both rules feature-flagged off (EnablePortAssignmentRule + EnableFycoImportExportRule); cycle skipped.");
                return;
            }

            var startedAt = DateTime.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            var alertService = scope.ServiceProvider.GetRequiredService<IDashboardAlertService>();

            // Step 1 — pull active Primary relations, in order of CreatedAt
            // (oldest first; legacy violations have older CreatedAt). LATERAL
            // join to grab the latest FS6000 fyco for each container. Mirrors
            // the probe SQL one-for-one.
            var relations = await appDb.ContainerBOERelations
                .AsNoTracking()
                .Where(cbr => cbr.IsActive && cbr.RelationType == "Primary")
                .OrderBy(cbr => cbr.CreatedAt)
                .Select(cbr => new
                {
                    cbr.Id,
                    cbr.ContainerNumber,
                    cbr.ICUMSBOEId,
                    cbr.ScannerType,
                    cbr.CreatedAt
                })
                .ToListAsync(stoppingToken);

            if (relations.Count == 0)
            {
                _logger.LogInformation("[BACKFILL-VALIDATION] cycle done — 0 active Primary relations.");
                return;
            }

            _logger.LogInformation(
                "[BACKFILL-VALIDATION] cycle start — {Count} active Primary relation(s); portRule={PortRule}, fycoRule={FycoRule}",
                relations.Count, portRuleEnabled, fycoRuleEnabled);

            // Step 2 — pull latest FS6000 fyco per container (one-shot grouped
            // query so we don't issue one round-trip per relation). Tracking
            // off; we read only.
            var containerNumbers = relations.Select(r => r.ContainerNumber).Distinct().ToList();

            // Latest FS6000 scan per container (by ScanTime desc) — mirrors the
            // LATERAL subquery in the probe.
            var fycoByContainer = await appDb.FS6000Scans
                .AsNoTracking()
                .Where(s => containerNumbers.Contains(s.ContainerNumber))
                .GroupBy(s => s.ContainerNumber)
                .Select(g => new
                {
                    Container = g.Key,
                    Fyco = g.OrderByDescending(s => s.ScanTime).Select(s => s.FycoPresent).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.Container, x => x.Fyco, StringComparer.OrdinalIgnoreCase, stoppingToken);

            // Whether each container was scanned by ASE. For port-match we need
            // both signals — FS6000 presence is implied by the relation row
            // (RelationType=='Primary' + ScannerType column), so we only need
            // the ASE signal explicitly.
            var aseScannedSet = (await appDb.AseScans
                .AsNoTracking()
                .Where(s => containerNumbers.Contains(s.ContainerNumber))
                .Select(s => s.ContainerNumber)
                .Distinct()
                .ToListAsync(stoppingToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Step 3 — pull canonical BOE rows by id from the icums-downloads DB
            // in batches. The relation row already carries ICUMSBOEId so we
            // hit by id rather than container number. Filter by Transferred
            // ProcessingStatus for parity with the runtime gate (audit 3.06).
            var boeIds = relations.Select(r => r.ICUMSBOEId).Where(id => id != 0).Distinct().ToList();
            var boeById = new Dictionary<int, (string? RegimeCode, string? ClearanceType, string? DeliveryPlace)>();

            for (var offset = 0; offset < boeIds.Count; offset += RelationBatchSize)
            {
                var slice = boeIds.GetRange(offset, Math.Min(RelationBatchSize, boeIds.Count - offset));
                var rows = await icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => slice.Contains(b.Id) && b.ProcessingStatus == "Transferred")
                    .Select(b => new { b.Id, b.RegimeCode, b.ClearanceType, b.DeliveryPlace })
                    .ToListAsync(stoppingToken);
                foreach (var r in rows)
                {
                    boeById[r.Id] = (r.RegimeCode, r.ClearanceType, r.DeliveryPlace);
                }
            }

            // Step 4 — apply rules row by row. Each violation goes through
            // IDashboardAlertService.RaiseAsync, which already handles dedupe
            // (Type, Title within a 30-min window AND any unacknowledged row).
            // So idempotency falls out for free — we don't need a "skip if
            // alert exists" pre-check here.
            int fycoViolations = 0, portViolations = 0, alertsCreated = 0;
            foreach (var rel in relations)
            {
                if (stoppingToken.IsCancellationRequested) break;

                fycoByContainer.TryGetValue(rel.ContainerNumber, out var fyco);
                if (!boeById.TryGetValue(rel.ICUMSBOEId, out var boe))
                {
                    // No canonical BOE row → can't validate either rule. Mirrors
                    // the runtime gate's "no BOE → not applicable" early-out.
                    continue;
                }

                // ── Rule A: FYCO direction ─────────────────────────────────
                if (fycoRuleEnabled && !string.IsNullOrWhiteSpace(fyco))
                {
                    var fycoOutcome = ClassifyFyco(fyco, boe.ClearanceType, boe.RegimeCode);
                    if (fycoOutcome.IsViolation)
                    {
                        fycoViolations++;
                        var title = $"FYCO direction mismatch — {rel.ContainerNumber}";
                        var description =
                            $"ContainerBOERelations row id={rel.Id} active+Primary, container={rel.ContainerNumber}, " +
                            $"FS6000 fycopresent='{fyco}' (export) vs BOE id={rel.ICUMSBOEId} " +
                            $"clearancetype='{boe.ClearanceType}' regime='{boe.RegimeCode}' ({fycoOutcome.Reason}). " +
                            $"Relation created {rel.CreatedAt:u} on scanner={rel.ScannerType}. Review: " +
                            $"deactivate relation OR confirm legitimate override. " +
                            $"container={rel.ContainerNumber} boe={rel.ICUMSBOEId} relation={rel.Id}";

                        await alertService.RaiseAsync(
                            type: "FycoDirectionMismatch",
                            severity: "Warning",
                            title: title,
                            description: description,
                            source: nameof(BackfillValidationService),
                            ct: stoppingToken);
                        alertsCreated++;
                    }
                }

                // ── Rule B: Port match ─────────────────────────────────────
                if (portRuleEnabled)
                {
                    var portOutcome = ClassifyPort(rel.ContainerNumber, rel.ScannerType, boe.DeliveryPlace, aseScannedSet);
                    if (portOutcome.IsViolation)
                    {
                        portViolations++;
                        var title = $"Port mismatch — {rel.ContainerNumber}";
                        var description =
                            $"ContainerBOERelations row id={rel.Id} active+Primary, container={rel.ContainerNumber}, " +
                            $"relation scanner='{rel.ScannerType}' vs BOE id={rel.ICUMSBOEId} " +
                            $"deliveryplace='{boe.DeliveryPlace}' ({portOutcome.Reason}). " +
                            $"Relation created {rel.CreatedAt:u}. Review: " +
                            $"deactivate relation OR confirm transit (both-port-scanned legitimate case). " +
                            $"container={rel.ContainerNumber} boe={rel.ICUMSBOEId} relation={rel.Id}";

                        await alertService.RaiseAsync(
                            type: "PortMismatch",
                            severity: "Warning",
                            title: title,
                            description: description,
                            source: nameof(BackfillValidationService),
                            ct: stoppingToken);
                        alertsCreated++;
                    }
                }
            }

            var elapsed = DateTime.UtcNow - startedAt;
            _logger.LogInformation(
                "[BACKFILL-VALIDATION] cycle done in {Elapsed}: scanned={Scanned}, fycoViolations={FycoCount}, portViolations={PortCount}, alertsRaised={AlertsCreated}",
                elapsed, relations.Count, fycoViolations, portViolations, alertsCreated);
        }

        /// <summary>
        /// FYCO rule mirror — keeps the backfill sweep's verdict identical to
        /// <c>ContainerValidationService.ValidateFycoImportExportAsync</c>:
        ///   Layer 1: ignore non-export FYCO (we only flag fyco=EXPORT vs
        ///            import-direction BOE; the symmetric import-vs-export
        ///            mismatch is handled by the runtime gate's secondary
        ///            branch which is rare in practice and not the focus of
        ///            this backfill).
        ///   Layer 2: fyco=EXPORT + ClearanceType starts with 'IM' → violation.
        ///   Layer 3: fyco=EXPORT + non-export regime → violation. CMR
        ///            (pre-BOE) defers; export regimes per
        ///            <see cref="Core.Entities.RegimeDirectionMap.IsExport"/>.
        ///
        /// Returns IsViolation=false for ambiguous-but-not-wrong cases (CMR
        /// pre-declarations, blank regime with EX clearance, etc.) — same
        /// "soft pass" semantics as the runtime gate.
        /// </summary>
        private static FycoVerdict ClassifyFyco(string fyco, string? clearanceType, string? regimeCode)
        {
            if (!FycoClassifier.IsExport(fyco))
            {
                return new FycoVerdict(false, "fyco non-export");
            }

            var clearance = (clearanceType ?? string.Empty).Trim();
            var isClearanceImport = clearance.StartsWith("IM", StringComparison.OrdinalIgnoreCase);
            var isClearanceCmr = clearance.Equals("CMR", StringComparison.OrdinalIgnoreCase);

            // Layer 2 — FYCO export with import clearance is a hard violation.
            if (isClearanceImport)
            {
                return new FycoVerdict(true, $"layer 2 — clearance='{clearanceType}' (import) vs fyco=export");
            }

            // CMR pre-declarations defer until BOE arrives; not a violation.
            if (isClearanceCmr && string.IsNullOrWhiteSpace(regimeCode))
            {
                return new FycoVerdict(false, "CMR pre-declaration deferred");
            }

            // Layer 3 — FYCO export must match an export-direction regime.
            if (string.IsNullOrWhiteSpace(regimeCode))
            {
                // EX clearance with no regime — runtime gate treats as soft-pass.
                return new FycoVerdict(false, "EX clearance, blank regime");
            }

            if (!RegimeDirectionMap.IsExport(regimeCode))
            {
                return new FycoVerdict(true, $"layer 3 — regime='{regimeCode}' is not an export regime");
            }

            return new FycoVerdict(false, "fyco direction agrees with regime");
        }

        /// <summary>
        /// Port-match rule mirror —
        /// <c>ContainerValidationService.ValidatePortMatchAsync</c> reads
        /// <c>BOEDocument.DeliveryPlace</c> and pulls a 3-character port code
        /// from positions 3-5 (e.g. <c>WTTMA1MPS3</c> → <c>TMA</c>). FS6000
        /// scans correspond to TKD (Takoradi); ASE scans correspond to TMA
        /// (Tema). When both scanners saw the container, treat it as transit
        /// (both-port legitimate).
        ///
        /// Returns IsViolation=false when the BOE has a null/unrecognised
        /// DeliveryPlace — the runtime gate skips the rule in that case;
        /// mirroring it here keeps backfill noise floor consistent.
        /// </summary>
        private static PortVerdict ClassifyPort(string containerNumber, string relationScannerType, string? deliveryPlace, HashSet<string> aseScannedSet)
        {
            if (string.IsNullOrWhiteSpace(deliveryPlace) || deliveryPlace.Length < 5)
            {
                return new PortVerdict(false, "delivery-place blank or too short");
            }

            var code = deliveryPlace.Substring(2, 3).ToUpperInvariant();
            string? boePort = code switch { "TKD" => "TKD", "TMA" => "TMA", _ => null };
            if (boePort == null)
            {
                return new PortVerdict(false, $"unrecognised delivery-place port '{code}'");
            }

            var scannedByFs6000 = string.Equals(relationScannerType, "FS6000", StringComparison.OrdinalIgnoreCase);
            var scannedByAse = aseScannedSet.Contains(containerNumber);

            // Transit / both-port containers are legitimate — runtime gate
            // emits a "scanned at both ports" pass; mirror that.
            if (scannedByFs6000 && scannedByAse)
            {
                return new PortVerdict(false, "both-port (transit-style) match");
            }

            if (scannedByFs6000 && boePort != "TKD")
            {
                return new PortVerdict(true, $"FS6000 scanned at TKD but BOE delivery-place is {boePort} ({deliveryPlace})");
            }

            if (scannedByAse && boePort != "TMA")
            {
                return new PortVerdict(true, $"ASE scanned at TMA but BOE delivery-place is {boePort} ({deliveryPlace})");
            }

            return new PortVerdict(false, $"BOE port {boePort} agrees with scanner");
        }

        private readonly record struct FycoVerdict(bool IsViolation, string Reason);
        private readonly record struct PortVerdict(bool IsViolation, string Reason);
    }
}
