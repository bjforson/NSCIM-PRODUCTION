using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for generating intelligent cargo content summaries from ICUMS data
    /// Uses local template-based summarization (no external AI costs)
    /// </summary>
    public interface ICargoSummaryService
    {
        /// <summary>
        /// Generate a human-readable cargo summary from cargo group data
        /// </summary>
        Task<CargoSummaryDto> GenerateSummaryAsync(CargoGroupDto cargoGroup);
    }
}

