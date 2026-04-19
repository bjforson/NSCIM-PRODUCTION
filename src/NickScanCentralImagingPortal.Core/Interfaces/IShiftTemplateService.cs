using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftTemplateService
    {
        Task<ShiftTemplate?> GetByIdAsync(Guid id);
        Task<ShiftTemplate?> GetByCodeAsync(string code);
        Task<IEnumerable<ShiftTemplate>> GetAllAsync(Guid? siteId = null, bool activeOnly = true);
        Task<ShiftTemplate> CreateAsync(ShiftTemplate template);
        Task<ShiftTemplate> UpdateAsync(ShiftTemplate template);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsAsync(string code);
    }
}

