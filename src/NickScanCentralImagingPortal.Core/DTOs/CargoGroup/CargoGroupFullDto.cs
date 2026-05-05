using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.DTOs.CargoGroup
{
    /// <summary>
    /// Phase 1 — Server-side disambiguation DTO for the unified cargo-group endpoint.
    /// Replaces the dialog's frontend `IsConsolidated` branching (`ImageAnalysisViewDialog.razor`
    /// 5-callsite dispatch) with a single round-trip that returns identity + 4 tab payloads.
    ///
    /// Per design `docs/audit/2026-05-05/follow-up-routing-endpoint-design.md` §2.
    /// Backend ships dark in Phase 1; frontend cutover is Phase 2.
    /// </summary>
    public sealed class CargoGroupFullDto
    {
        // ── Identity (server-resolved, never the raw GroupIdentifier) ──
        public Guid? AnalysisGroupId { get; init; }
        public int? RecordCompletenessStatusId { get; init; }
        public string DeclarationNumber { get; init; } = "";
        public string? MasterBlNumber { get; init; }
        public string? ContainerGroupKey { get; init; }
        public CargoGroupingMode GroupingMode { get; init; }
        public string? ScannerType { get; init; }
        public string? ClearanceType { get; init; }
        public string? RegimeCode { get; init; }

        // ── Membership ──
        public IReadOnlyList<string> ContainerNumbers { get; init; } = Array.Empty<string>();
        public IReadOnlyList<HouseBLDetail> HouseBls { get; init; } = Array.Empty<HouseBLDetail>();

        // ── Tab-shaped payloads ──
        public ScannerDataPayload ScannerData { get; init; } = new();
        public IcumsDataPayload IcumsData { get; init; } = new();
        public ImageDecisionsPayload ImageDecisions { get; init; } = new();
        public CargoSummaryPayload Summary { get; init; } = new();

        // ── Server-classified status (replaces "200 + empty == ???") ──
        public CargoGroupResolutionStatus Status { get; init; }
        public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Cargo-grouping mode classified from RCS shape (NOT BoeDocument.IsConsolidated).
    /// Pattern A (used cars in one container) is first-class — 2,774 RCS rows in prod
    /// were previously squashed into the consolidated branch by frontend dispatch.
    /// </summary>
    public enum CargoGroupingMode
    {
        SingleDeclarationSingleContainer,    // ~most common: 1 decl, 1 cn
        SingleDeclarationMultipleContainers, // non-consolidated, multi-cn
        ConsolidatedMultiHouseBl,            // 1 cn, many decls/HBLs (current "consolidated")
        PatternAUsedCars,                    // RCS.ContainerGroupKey set: multi-decl, 1 cn
        LegacyOrUnknown                      // pre-1.15.0 AG with no RCS link
    }

    /// <summary>
    /// Resolution outcome for the input groupIdentifier — distinguishes
    /// "container number unknown" (404) from "found but partial" (200 + marker)
    /// (closes findings 6.02, 6.03).
    /// </summary>
    public enum CargoGroupResolutionStatus
    {
        Found,                  // canonical record located, all data present
        FoundButPartial,        // record found, some payloads empty (e.g. no scans yet)
        GroupIdentifierUnknown, // input did not resolve to any RCS, AG, or BOE
        AmbiguousNeedsHint      // input matched multiple records; client must pass ?scannerType=
    }

    /// <summary>
    /// One House BL row inside a consolidated container.
    /// Mirrors the fields the existing dialog's House BL tab renders
    /// (`ImageAnalysisViewDialog.razor:1707-1730`).
    /// </summary>
    public sealed class HouseBLDetail
    {
        public string HouseBl { get; init; } = "";
        public string? MasterBl { get; init; }
        public string? DeclarationNumber { get; init; }
        public string? ConsigneeName { get; init; }
        public string? ClearanceType { get; init; }
        public string? RotationNumber { get; init; }
        public string? GoodsDescription { get; init; }
        public decimal? TotalDutyPaid { get; init; }
        public string? DeclarationDate { get; init; }
        public int BoeId { get; init; }
        public string? ContainerNumber { get; init; }
    }

    /// <summary>
    /// Scanner Data tab payload. Driven by `ScannerDataTab.razor`'s expected shape
    /// (Field/Value/Category/Timestamp tuples + total record count).
    /// </summary>
    public sealed class ScannerDataPayload
    {
        public IReadOnlyList<ScannerDataGroupDto> Groups { get; init; } = Array.Empty<ScannerDataGroupDto>();
        public int TotalRecords { get; init; }
    }

    /// <summary>
    /// ICUMS Data tab payload. Driven by `ICUMSDataTab.razor`'s expected shape
    /// (per-container or per-HBL records + BOE detail rows for expandable view).
    /// </summary>
    public sealed class IcumsDataPayload
    {
        public IReadOnlyList<ICUMSDataGroupDto> Groups { get; init; } = Array.Empty<ICUMSDataGroupDto>();
        public int TotalRecords { get; init; }
    }

    /// <summary>
    /// Images & Decisions tab payload — overall decision rollup + per-container decisions.
    /// Mirrors `RefreshOverallDecision` (`ImageAnalysisViewDialog.razor:1969-2030`).
    /// </summary>
    public sealed class ImageDecisionsPayload
    {
        public string? OverallDecision { get; init; }
        public int TotalImages { get; init; }
        public int NormalCount { get; init; }
        public int AbnormalCount { get; init; }
        public string? LastReviewedBy { get; init; }
        public IReadOnlyList<ContainerDecisionDto> ContainerDecisions { get; init; } = Array.Empty<ContainerDecisionDto>();
    }

    /// <summary>
    /// One row per container in the group — current decision, reviewer, tags.
    /// </summary>
    public sealed class ContainerDecisionDto
    {
        public string ContainerNumber { get; init; } = "";
        public string ScannerType { get; init; } = "";
        public string Decision { get; init; } = "Pending";
        public string? Comments { get; init; }
        public string? Tags { get; init; }
        public string? ReviewedBy { get; init; }
        public DateTime? ReviewedAt { get; init; }
    }

    /// <summary>
    /// Summary tab payload — high-level rollup the operator sees first.
    /// Driven by the Summary tab in `ImageAnalysisViewDialog.razor:178-186` and the
    /// `CargoGroupDto`-shaped data the dialog already builds via
    /// `LoadCargoGroupForSummary` (lines 1463-1693).
    /// </summary>
    public sealed class CargoSummaryPayload
    {
        public int TotalContainers { get; init; }
        public int TotalHouseBls { get; init; }
        public int TotalBoes { get; init; }
        public int TotalIcumsRecords { get; init; }
        public int TotalScannerRecords { get; init; }
        public int TotalImages { get; init; }
        public DateTime? LatestUpdateDate { get; init; }
        public string? AiCargoSummary { get; init; }
        public string? ConsigneeName { get; init; }
    }
}
