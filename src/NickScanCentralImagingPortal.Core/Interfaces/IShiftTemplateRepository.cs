using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftTemplateRepository : IRepositoryGuid<ShiftTemplate>
    {
        Task<ShiftTemplate?> GetByCodeAsync(string code);
        Task<IEnumerable<ShiftTemplate>> GetBySiteIdAsync(Guid? siteId);
        Task<IEnumerable<ShiftTemplate>> GetActiveTemplatesAsync();
    }
}

