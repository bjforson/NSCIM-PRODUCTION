using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface ICargoGroupService
    {
        Task<CargoGroupDto?> GetCargoGroupAsync(string groupIdentifier, CargoType? type = null, bool loadScannerData = true, bool loadImageData = true, bool loadICUMSData = true);
        Task<CargoGroupDataDto> GetCargoGroupDataAsync(string groupIdentifier, CargoType type, bool loadScannerData = true, bool loadImageData = true, bool loadICUMSData = true);
        Task<List<CargoGroupSummaryDto>> GetCargoGroupsAsync(CargoType? type = null, string? clearanceType = null, int page = 1, int pageSize = 50);
        Task<string?> GetAiCargoSummaryAsync(string groupIdentifier);
        Task SaveAiCargoSummaryAsync(string groupIdentifier, string summary);
    }
}
