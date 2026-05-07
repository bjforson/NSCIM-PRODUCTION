using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.CargoGrouping
{
    /// <summary>
    /// Phase 1 — RCS-keyed group resolver implementation.
    ///
    /// Walks the 6-step dispatch order from design §3.1:
    ///   a) AnalysisGroups by Id (when groupIdentifier is a Guid)
    ///   b) AnalysisGroups by NormalizedGroupIdentifier
    ///   c) AnalysisGroups by GroupIdentifier (date-suffixed display form)
    ///   d) RecordCompletenessStatus by DeclarationNumber
    ///   e) RecordCompletenessStatus by ContainerGroupKey  (Pattern A)
    ///   f) IcumDownloadsRepository.BOEDocuments by ContainerNumber  (legacy AG-less rows)
    ///
    /// Then classifies the resulting shape into a <see cref="CargoGroupingMode"/>
    /// per design §3.2.
    /// </summary>
    public sealed class GroupResolver : IGroupResolver
    {
        private readonly ApplicationDbContext _appDb;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<GroupResolver> _logger;

        public GroupResolver(
            ApplicationDbContext appDb,
            IcumDownloadsDbContext icumDb,
            ILogger<GroupResolver> logger)
        {
            _appDb = appDb;
            _icumDb = icumDb;
            _logger = logger;
        }

        public async Task<GroupResolution> ResolveAsync(string groupIdentifier, string? scannerType, CancellationToken ct)
        {
            var diagnostics = new List<string>();

            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                return new GroupResolution
                {
                    Status = CargoGroupResolutionStatus.GroupIdentifierUnknown,
                    Diagnostics = new[] { "groupIdentifier is null or whitespace" }
                };
            }

            var trimmed = groupIdentifier.Trim();

            // ─── Step (a): AnalysisGroups by Id (Guid) ───────────────────────
            if (Guid.TryParse(trimmed, out var asGuid))
            {
                var ag = await _appDb.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.Id == asGuid)
                    .Where(g => scannerType == null || g.ScannerType == scannerType)
                    .FirstOrDefaultAsync(ct);
                if (ag != null)
                {
                    diagnostics.Add($"resolved-via=ag.id");
                    return await ClassifyFromAg(ag, diagnostics, ct);
                }
            }

            // ─── Step (b): AnalysisGroups by NormalizedGroupIdentifier ───────
            {
                var matches = await _appDb.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.NormalizedGroupIdentifier == trimmed)
                    .Where(g => scannerType == null || g.ScannerType == scannerType)
                    .Take(2)
                    .ToListAsync(ct);
                if (matches.Count > 1 && scannerType == null)
                {
                    diagnostics.Add("ambiguous-via=ag.normalizedgroupidentifier");
                    return new GroupResolution
                    {
                        Status = CargoGroupResolutionStatus.AmbiguousNeedsHint,
                        Diagnostics = diagnostics
                    };
                }
                if (matches.Count >= 1)
                {
                    diagnostics.Add($"resolved-via=ag.normalizedgroupidentifier");
                    return await ClassifyFromAg(matches[0], diagnostics, ct);
                }
            }

            // ─── Step (c): AnalysisGroups by GroupIdentifier (date-suffixed) ──
            {
                var matches = await _appDb.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.GroupIdentifier == trimmed)
                    .Where(g => scannerType == null || g.ScannerType == scannerType)
                    .Take(2)
                    .ToListAsync(ct);
                if (matches.Count > 1 && scannerType == null)
                {
                    diagnostics.Add("ambiguous-via=ag.groupidentifier");
                    return new GroupResolution
                    {
                        Status = CargoGroupResolutionStatus.AmbiguousNeedsHint,
                        Diagnostics = diagnostics
                    };
                }
                if (matches.Count >= 1)
                {
                    diagnostics.Add($"resolved-via=ag.groupidentifier");
                    return await ClassifyFromAg(matches[0], diagnostics, ct);
                }
            }

            // ─── Step (d): RCS by DeclarationNumber ───────────────────────────
            {
                var rcs = await _appDb.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => r.DeclarationNumber == trimmed)
                    .Where(r => scannerType == null || r.ScannerType == scannerType || r.ScannerType == null)
                    .OrderByDescending(r => r.UpdatedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (rcs != null)
                {
                    diagnostics.Add($"resolved-via=rcs.declarationnumber");
                    return await ClassifyFromRcs(rcs, diagnostics, ct);
                }
            }

            // ─── Step (e): RCS by ContainerGroupKey (Pattern A) ───────────────
            {
                var patternARows = await _appDb.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => r.ContainerGroupKey == trimmed)
                    .Where(r => scannerType == null || r.ScannerType == scannerType || r.ScannerType == null)
                    .ToListAsync(ct);
                if (patternARows.Any())
                {
                    diagnostics.Add($"resolved-via=rcs.containergroupkey;pattern_a_rows={patternARows.Count}");
                    var primary = patternARows
                        .OrderByDescending(r => r.UpdatedAtUtc)
                        .First();
                    return await ClassifyPatternA(primary, patternARows, diagnostics, ct);
                }
            }

            // ─── Step (f): BOE by ContainerNumber (legacy AG-less rows) ──────
            {
                var boeRows = await _icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.ContainerNumber == trimmed)
                    .ToListAsync(ct);
                if (boeRows.Any())
                {
                    diagnostics.Add($"resolved-via=boe.containernumber;rows={boeRows.Count}");
                    return ClassifyFromBoe(trimmed, boeRows, diagnostics);
                }
            }

            diagnostics.Add("no-match-in-any-of-6-dispatch-branches");
            return new GroupResolution
            {
                Status = CargoGroupResolutionStatus.GroupIdentifierUnknown,
                Diagnostics = diagnostics
            };
        }

        // ── Classification helpers ───────────────────────────────────────────

        private async Task<GroupResolution> ClassifyFromAg(
            NickScanCentralImagingPortal.Core.Entities.Analysis.AnalysisGroup ag,
            List<string> diagnostics,
            CancellationToken ct)
        {
            // Look up the linked RCS (if any) for richer classification.
            NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus? rcs = null;
            if (ag.RecordCompletenessStatusId.HasValue)
            {
                rcs = await _appDb.RecordCompletenessStatuses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == ag.RecordCompletenessStatusId.Value, ct);
            }

            // Wave-AG fallback: Timeout/Auto-close waves are created without a direct
            // RecordCompletenessStatusId FK (only InitialBatch waves get it set). Recover
            // the RCS via the AG's NormalizedGroupIdentifier, which strips any "_W{N}"
            // suffix and equals the BL/declaration that the RCS row keys on.
            // Without this, wave AGs (e.g. "10326204603_W1") fall through to the
            // legacy/unknown branch and the cargo summary tab shows no ICUMS data.
            if (rcs == null && !string.IsNullOrWhiteSpace(ag.NormalizedGroupIdentifier))
            {
                var normalizedKey = ag.NormalizedGroupIdentifier;
                rcs = await _appDb.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => r.DeclarationNumber == normalizedKey || r.BlNumber == normalizedKey || r.ContainerGroupKey == normalizedKey)
                    .Where(r => ag.ScannerType == null || r.ScannerType == null || r.ScannerType == ag.ScannerType)
                    .OrderByDescending(r => r.UpdatedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (rcs != null)
                {
                    diagnostics.Add($"ag.rcs.via-normalized={rcs.Id}");
                }
            }

            if (rcs != null)
            {
                diagnostics.Add($"ag.rcs.id={rcs.Id}");
                return await ClassifyFromRcs(rcs, diagnostics, ct, agOverride: ag);
            }

            // Legacy AG without an RCS link — pre-1.15.0 row.
            diagnostics.Add("ag.no-rcs-link;legacy-or-unknown");

            // Best-effort container list: the AG's NormalizedGroupIdentifier may itself
            // be a container number (consolidated case) or a declaration (non-consolidated).
            var containers = new List<string>();
            var keyForLookup = ag.NormalizedGroupIdentifier ?? ag.GroupIdentifier;
            if (!string.IsNullOrWhiteSpace(keyForLookup))
            {
                // Try as ContainerNumber first
                var byContainer = await _icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.ContainerNumber == keyForLookup)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .ToListAsync(ct);
                if (byContainer.Any())
                {
                    containers = byContainer;
                }
                else
                {
                    var byDeclaration = await _icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.DeclarationNumber == keyForLookup)
                        .Select(b => b.ContainerNumber)
                        .Distinct()
                        .ToListAsync(ct);
                    containers = byDeclaration;
                }
            }

            return new GroupResolution
            {
                AnalysisGroupId = ag.Id,
                ScannerType = ag.ScannerType,
                ContainerNumbers = containers,
                Mode = CargoGroupingMode.LegacyOrUnknown,
                Status = CargoGroupResolutionStatus.FoundButPartial,
                Diagnostics = diagnostics
            };
        }

        private async Task<GroupResolution> ClassifyFromRcs(
            NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus rcs,
            List<string> diagnostics,
            CancellationToken ct,
            NickScanCentralImagingPortal.Core.Entities.Analysis.AnalysisGroup? agOverride = null)
        {
            var expectedContainers = await _appDb.RecordExpectedContainers
                .AsNoTracking()
                .Where(rec => rec.RecordId == rcs.Id)
                .Select(rec => rec.ContainerNumber)
                .Distinct()
                .ToListAsync(ct);

            // Match the AG that points at this RCS, if not given.
            Guid? agId = agOverride?.Id;
            if (!agId.HasValue)
            {
                agId = await _appDb.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.RecordCompletenessStatusId == rcs.Id)
                    .Where(g => rcs.ScannerType == null || g.ScannerType == rcs.ScannerType || g.ScannerType == null)
                    .OrderByDescending(g => g.UpdatedAtUtc ?? g.CreatedAtUtc)
                    .Select(g => (Guid?)g.Id)
                    .FirstOrDefaultAsync(ct);
            }

            // Pattern A?  RCS.ContainerGroupKey set ⇒ multi-decl, single physical container.
            if (!string.IsNullOrWhiteSpace(rcs.ContainerGroupKey))
            {
                diagnostics.Add("classified=PatternAUsedCars");
                var siblingRcs = await _appDb.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => r.ContainerGroupKey == rcs.ContainerGroupKey)
                    .ToListAsync(ct);
                return new GroupResolution
                {
                    AnalysisGroupId = agId,
                    RecordCompletenessStatusId = rcs.Id,
                    DeclarationNumber = rcs.DeclarationNumber,
                    MasterBlNumber = rcs.BlNumber,
                    ContainerGroupKey = rcs.ContainerGroupKey,
                    ScannerType = agOverride?.ScannerType ?? rcs.ScannerType,
                    ClearanceType = rcs.ClearanceType,
                    RegimeCode = rcs.RegimeCode,
                    ContainerNumbers = expectedContainers.Any()
                        ? expectedContainers
                        : new List<string> { rcs.ContainerGroupKey! },
                    Mode = CargoGroupingMode.PatternAUsedCars,
                    Status = CargoGroupResolutionStatus.Found,
                    Diagnostics = diagnostics
                };
            }

            // BOE shape — count distinct BOEs sharing the *same single container*.
            int distinctBoesForFirstContainer = 0;
            if (expectedContainers.Count == 1)
            {
                var firstCn = expectedContainers[0];
                distinctBoesForFirstContainer = await _icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.ContainerNumber == firstCn)
                    .Select(b => b.DeclarationNumber)
                    .Distinct()
                    .CountAsync(ct);
            }

            CargoGroupingMode mode;
            if (expectedContainers.Count >= 2)
            {
                // Multiple containers, all share the same single declaration.
                mode = CargoGroupingMode.SingleDeclarationMultipleContainers;
                diagnostics.Add($"classified=SingleDeclarationMultipleContainers;containers={expectedContainers.Count}");
            }
            else if (expectedContainers.Count == 1 && distinctBoesForFirstContainer >= 2)
            {
                // Single container with multiple distinct BOE rows ⇒ consolidated multi-HBL.
                mode = CargoGroupingMode.ConsolidatedMultiHouseBl;
                diagnostics.Add($"classified=ConsolidatedMultiHouseBl;distinct_boes={distinctBoesForFirstContainer}");
            }
            else
            {
                mode = CargoGroupingMode.SingleDeclarationSingleContainer;
                diagnostics.Add($"classified=SingleDeclarationSingleContainer");
            }

            var status = expectedContainers.Any()
                ? CargoGroupResolutionStatus.Found
                : CargoGroupResolutionStatus.FoundButPartial;

            return new GroupResolution
            {
                AnalysisGroupId = agId,
                RecordCompletenessStatusId = rcs.Id,
                DeclarationNumber = rcs.DeclarationNumber,
                MasterBlNumber = rcs.BlNumber,
                ContainerGroupKey = rcs.ContainerGroupKey,
                ScannerType = agOverride?.ScannerType ?? rcs.ScannerType,
                ClearanceType = rcs.ClearanceType,
                RegimeCode = rcs.RegimeCode,
                ContainerNumbers = expectedContainers,
                Mode = mode,
                Status = status,
                Diagnostics = diagnostics
            };
        }

        private async Task<GroupResolution> ClassifyPatternA(
            NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus primary,
            List<NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus> siblings,
            List<string> diagnostics,
            CancellationToken ct)
        {
            var agId = await _appDb.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.RecordCompletenessStatusId == primary.Id)
                .OrderByDescending(g => g.UpdatedAtUtc ?? g.CreatedAtUtc)
                .Select(g => (Guid?)g.Id)
                .FirstOrDefaultAsync(ct);

            // The Pattern A grouping key IS the container number; surface it as the only container.
            diagnostics.Add($"classified=PatternAUsedCars;sibling_decls={siblings.Count}");
            return new GroupResolution
            {
                AnalysisGroupId = agId,
                RecordCompletenessStatusId = primary.Id,
                DeclarationNumber = primary.DeclarationNumber,
                MasterBlNumber = primary.BlNumber,
                ContainerGroupKey = primary.ContainerGroupKey,
                ScannerType = primary.ScannerType,
                ClearanceType = primary.ClearanceType,
                RegimeCode = primary.RegimeCode,
                ContainerNumbers = new List<string> { primary.ContainerGroupKey! },
                Mode = CargoGroupingMode.PatternAUsedCars,
                Status = CargoGroupResolutionStatus.Found,
                Diagnostics = diagnostics
            };
        }

        private static GroupResolution ClassifyFromBoe(
            string trimmedKey,
            List<NickScanCentralImagingPortal.Core.Models.BOEDocument> boeRows,
            List<string> diagnostics)
        {
            // Decide on shape from the BOE rows alone — no RCS available.
            var distinctDecls = boeRows.Select(b => b.DeclarationNumber).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
            var distinctHbls = boeRows.Select(b => b.HouseBl).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();
            var firstDecl = distinctDecls.FirstOrDefault();
            var firstBl = boeRows.Select(b => b.BlNumber).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
            var firstClearance = boeRows.Select(b => b.ClearanceType).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            var firstRegime = boeRows.Select(b => b.RegimeCode).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

            CargoGroupingMode mode;
            if (distinctDecls.Count >= 2 && distinctHbls.Count >= 2)
            {
                mode = CargoGroupingMode.ConsolidatedMultiHouseBl;
                diagnostics.Add($"boe-classified=ConsolidatedMultiHouseBl;decls={distinctDecls.Count};hbls={distinctHbls.Count}");
            }
            else
            {
                mode = CargoGroupingMode.LegacyOrUnknown;
                diagnostics.Add($"boe-classified=LegacyOrUnknown");
            }

            return new GroupResolution
            {
                AnalysisGroupId = null,
                RecordCompletenessStatusId = null,
                DeclarationNumber = firstDecl ?? "",
                MasterBlNumber = firstBl,
                ScannerType = null,
                ClearanceType = firstClearance,
                RegimeCode = firstRegime,
                ContainerNumbers = new List<string> { trimmedKey },
                Mode = mode,
                Status = CargoGroupResolutionStatus.FoundButPartial,
                Diagnostics = diagnostics
            };
        }
    }
}
