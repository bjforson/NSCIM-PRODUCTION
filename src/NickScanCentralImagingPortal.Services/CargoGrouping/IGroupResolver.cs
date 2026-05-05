using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanCentralImagingPortal.Services.CargoGrouping
{
    /// <summary>
    /// Phase 1 — RCS-keyed group resolver. Walks the 6-step dispatch order
    /// (AG.Id → AG.NormalizedGroupIdentifier → AG.GroupIdentifier → RCS.DeclarationNumber
    ///  → RCS.ContainerGroupKey → BOE.ContainerNumber) and classifies the resulting
    /// shape into a <see cref="CargoGroupingMode"/>.
    ///
    /// Per design `docs/audit/2026-05-05/follow-up-routing-endpoint-design.md` §3.1, §3.2.
    /// </summary>
    public interface IGroupResolver
    {
        Task<GroupResolution> ResolveAsync(string groupIdentifier, string? scannerType, CancellationToken ct);
    }

    /// <summary>
    /// Outcome of <see cref="IGroupResolver.ResolveAsync"/>. All identity fields
    /// nullable — populated based on which dispatch branch fired.
    /// </summary>
    public sealed class GroupResolution
    {
        public System.Guid? AnalysisGroupId { get; init; }
        public int? RecordCompletenessStatusId { get; init; }
        public string? DeclarationNumber { get; init; }
        public string? MasterBlNumber { get; init; }
        public string? ContainerGroupKey { get; init; }
        public string? ScannerType { get; init; }
        public string? ClearanceType { get; init; }
        public string? RegimeCode { get; init; }
        public IReadOnlyList<string> ContainerNumbers { get; init; } = System.Array.Empty<string>();
        public CargoGroupingMode Mode { get; init; }
        public CargoGroupResolutionStatus Status { get; init; }
        public IReadOnlyList<string> Diagnostics { get; init; } = System.Array.Empty<string>();
    }
}
