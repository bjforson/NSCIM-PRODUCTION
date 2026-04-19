using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ShiftAttendance
{
    public class ShiftAssignmentService : IShiftAssignmentService
    {
        private readonly IShiftAssignmentRepository _repository;
        private readonly ILogger<ShiftAssignmentService> _logger;

        public ShiftAssignmentService(
            IShiftAssignmentRepository repository,
            ILogger<ShiftAssignmentService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ShiftAssignment?> GetByIdAsync(Guid id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            return await _repository.GetByEmployeeIdAsync(employeeId, dateFrom, dateTo);
        }

        public async Task<IEnumerable<ShiftAssignment>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            return await _repository.GetBySiteIdAsync(siteId, dateFrom, dateTo);
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null)
        {
            return await _repository.GetByDateRangeAsync(dateFrom, dateTo, siteId);
        }

        public async Task<ShiftAssignment> CreateAsync(ShiftAssignment assignment)
        {
            // Validate no conflicts
            var hasConflict = await _repository.HasConflictAsync(
                assignment.EmployeeId,
                assignment.SiteId,
                assignment.Date,
                assignment.ShiftTemplateId);

            if (hasConflict)
            {
                throw new InvalidOperationException(
                    $"Employee already has a shift scheduled for {assignment.Date:yyyy-MM-dd} at this site.");
            }

            assignment.Id = Guid.NewGuid();
            assignment.Status = "SCHEDULED";
            assignment.CreatedAt = DateTime.UtcNow;

            return await _repository.AddAsync(assignment);
        }

        public async Task<BulkAssignmentResult> CreateBulkAsync(IEnumerable<ShiftAssignment> assignments, bool validateConflicts = true)
        {
            var result = new BulkAssignmentResult();
            var assignmentsList = assignments.ToList();

            foreach (var (assignment, index) in assignmentsList.Select((a, i) => (a, i)))
            {
                try
                {
                    if (validateConflicts)
                    {
                        var hasConflict = await _repository.HasConflictAsync(
                            assignment.EmployeeId,
                            assignment.SiteId,
                            assignment.Date,
                            assignment.ShiftTemplateId);

                        if (hasConflict)
                        {
                            result.Errors.Add(new BulkAssignmentError
                            {
                                Index = index,
                                Assignment = assignment,
                                Error = "Shift assignment conflict",
                                Code = "SHIFT_CONFLICT"
                            });
                            result.Failed++;
                            continue;
                        }
                    }

                    assignment.Id = Guid.NewGuid();
                    assignment.Status = "SCHEDULED";
                    assignment.CreatedAt = DateTime.UtcNow;

                    var created = await _repository.AddAsync(assignment);
                    result.Assignments.Add(created);
                    result.Created++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new BulkAssignmentError
                    {
                        Index = index,
                        Assignment = assignment,
                        Error = ex.Message,
                        Code = "UNKNOWN_ERROR"
                    });
                    result.Failed++;
                    _logger.LogError(ex, "Error creating shift assignment at index {Index}", index);
                }
            }

            return result;
        }

        public async Task<ShiftAssignment> UpdateAsync(ShiftAssignment assignment)
        {
            var existing = await _repository.GetByIdAsync(assignment.Id);
            if (existing == null)
            {
                throw new InvalidOperationException($"Shift assignment with ID '{assignment.Id}' not found.");
            }

            // Validate no conflicts if date/site/employee changed
            if (existing.EmployeeId != assignment.EmployeeId
                || existing.SiteId != assignment.SiteId
                || existing.Date != assignment.Date
                || existing.ShiftTemplateId != assignment.ShiftTemplateId)
            {
                var hasConflict = await _repository.HasConflictAsync(
                    assignment.EmployeeId,
                    assignment.SiteId,
                    assignment.Date,
                    assignment.ShiftTemplateId,
                    assignment.Id);

                if (hasConflict)
                {
                    throw new InvalidOperationException(
                        $"Employee already has a shift scheduled for {assignment.Date:yyyy-MM-dd} at this site.");
                }
            }

            assignment.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(assignment);
            return assignment;
        }

        public async Task<bool> UpdateStatusAsync(Guid id, string status, string? notes = null)
        {
            var assignment = await _repository.GetByIdAsync(id);
            if (assignment == null)
            {
                return false;
            }

            assignment.Status = status;
            if (!string.IsNullOrEmpty(notes))
            {
                assignment.Notes = notes;
            }
            assignment.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(assignment);
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var assignment = await _repository.GetByIdAsync(id);
            if (assignment == null)
            {
                return false;
            }

            // Soft delete by setting status to CANCELLED
            assignment.Status = "CANCELLED";
            assignment.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(assignment);
            return true;
        }

        public async Task<bool> HasConflictAsync(Guid employeeId, Guid siteId, DateTime date, Guid shiftTemplateId, Guid? excludeId = null)
        {
            return await _repository.HasConflictAsync(employeeId, siteId, date, shiftTemplateId, excludeId);
        }
    }
}

