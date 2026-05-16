using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.New.Models
{
    /// <summary>
    /// Aggregated data needed to render the container details experience.
    /// </summary>
    public class ContainerViewContext
    {
        /// <summary>
        /// Container identifier for this context.
        /// </summary>
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// High level summary information used by the header and summary cards.
        /// </summary>
        public NickScanWebApp.Shared.Models.ContainerBasicInfo? BasicInfo { get; set; }

        /// <summary>
        /// Optional Phase 2A source-scan resolution for scanner/image/split flows.
        /// Null means the resolver endpoint was not available or did not resolve.
        /// </summary>
        public ScanAssetResolution? ScanAssetResolution { get; set; }

        /// <summary>
        /// Flattened scanner fields for display (already grouped by category in the UI).
        /// Typically fetched as a single "all records" page.
        /// </summary>
        public NickScanWebApp.Shared.Models.PagedResult<NickScanWebApp.Shared.Models.ScannerDataRecord>? ScannerData { get; set; }

        /// <summary>
        /// Flattened ICUMS fields for display.
        /// Typically fetched as a single "all records" page.
        /// </summary>
        public NickScanWebApp.Shared.Models.PagedResult<NickScanWebApp.Shared.Models.ICUMSDataRecord>? ICUMSData { get; set; }

        /// <summary>
        /// Image metadata for the container (thumbnails + full-image URLs).
        /// </summary>
        public List<NickScanWebApp.Shared.Models.ImageMetadata>? Images { get; set; }

        /// <summary>
        /// Optional search results for the container.
        /// This is populated on-demand when the user performs a search.
        /// </summary>
        public UnifiedSearchResults? SearchResults { get; set; }
    }

    /// <summary>
    /// Aggregated data needed to render a cargo group view.
    /// </summary>
    public class CargoGroupViewContext
    {
        /// <summary>
        /// Group identifier (Master BL for consolidated, Declaration Number for non-consolidated).
        /// </summary>
        public string GroupIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Cargo type (Consolidated or NonConsolidated).
        /// </summary>
        public CargoType? Type { get; set; }

        /// <summary>
        /// High level cargo group summary information.
        /// </summary>
        public CargoGroupDto? Group { get; set; }

        /// <summary>
        /// Optional full data payload (ICUMS, scanner, images) for the group.
        /// </summary>
        public CargoGroupDataDto? GroupData { get; set; }
    }

    /// <summary>
    /// Aggregated data needed to render an audit review dialog.
    /// Contains preloaded container data for all containers in the audit group.
    /// </summary>
    public class AuditReviewViewContext
    {
        /// <summary>
        /// Group identifier for the audit group.
        /// </summary>
        public string GroupIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Preloaded container view contexts for each container in the audit group.
        /// Key: ContainerNumber, Value: Preloaded container data.
        /// </summary>
        public Dictionary<string, ContainerViewContext> ContainerContexts { get; set; } = new();

        /// <summary>
        /// Total number of containers in this audit group.
        /// </summary>
        public int ContainerCount => ContainerContexts.Count;

        /// <summary>
        /// Get container context by container number.
        /// </summary>
        public ContainerViewContext? GetContainerContext(string containerNumber)
        {
            return ContainerContexts.TryGetValue(containerNumber, out var context) ? context : null;
        }
    }

    /// <summary>
    /// Aggregated data needed to render an image analysis view dialog.
    /// Handles both consolidated (single container) and non-consolidated (multiple containers) scenarios.
    /// </summary>
    public class ImageAnalysisViewContext
    {
        /// <summary>
        /// Group identifier (container number for consolidated, BOE/Declaration for non-consolidated).
        /// </summary>
        public string GroupIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is consolidated cargo (single container) or non-consolidated (multiple containers).
        /// </summary>
        public bool IsConsolidated { get; set; }

        /// <summary>
        /// Preloaded cargo group context (for summary tab).
        /// </summary>
        public CargoGroupViewContext? CargoGroupContext { get; set; }

        /// <summary>
        /// For consolidated: Preloaded container context for the single container (GroupIdentifier is the container number).
        /// For non-consolidated: Preloaded container contexts for all containers in the group.
        /// </summary>
        public Dictionary<string, ContainerViewContext> ContainerContexts { get; set; } = new();

        /// <summary>
        /// Get container context by container number.
        /// </summary>
        public ContainerViewContext? GetContainerContext(string containerNumber)
        {
            return ContainerContexts.TryGetValue(containerNumber, out var context) ? context : null;
        }

        /// <summary>
        /// For consolidated: Get the single container context (GroupIdentifier is the container).
        /// </summary>
        public ContainerViewContext? GetConsolidatedContainerContext()
        {
            return IsConsolidated ? GetContainerContext(GroupIdentifier) : null;
        }
    }
}


