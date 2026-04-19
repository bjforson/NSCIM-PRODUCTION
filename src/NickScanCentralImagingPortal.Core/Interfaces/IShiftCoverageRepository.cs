using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftCoverageRepository : IRepositoryGuid<ShiftCoverageRequirement>
    {
        Task<IEnumerable<ShiftCoverageRequirement>> GetBySiteIdAsync(Guid siteId, bool activeOnly = true);
        Task<IEnumerable<ShiftCoverageRequirement>> GetByLaneIdAsync(Guid laneId, bool activeOnly = true);
        Task<IEnumerable<ShiftCoverageRequirement>> GetByShiftTemplateIdAsync(Guid shiftTemplateId, bool activeOnly = true);
        Task<IEnumerable<ShiftCoverageRequirement>> GetActiveRequirementsAsync(DateTime date, Guid? siteId = null);
    }
}

