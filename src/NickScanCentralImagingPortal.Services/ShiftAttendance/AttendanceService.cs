using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ShiftAttendance
{
    public class AttendanceService : IAttendanceService
    {
        private readonly IAttendanceRecordRepository _attendanceRepository;
        private readonly IShiftAssignmentRepository _shiftRepository;
        private readonly IShiftTemplateRepository _templateRepository;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(
            IAttendanceRecordRepository attendanceRepository,
            IShiftAssignmentRepository shiftRepository,
            IShiftTemplateRepository templateRepository,
            ILogger<AttendanceService> logger)
        {
            _attendanceRepository = attendanceRepository;
            _shiftRepository = shiftRepository;
            _templateRepository = templateRepository;
            _logger = logger;
        }

        public async Task<AttendanceRecord?> GetByIdAsync(Guid id)
        {
            return await _attendanceRepository.GetByIdAsync(id);
        }

        public async Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateTime date)
        {
            return await _attendanceRepository.GetByEmployeeAndDateAsync(employeeId, date);
        }

        public async Task<IEnumerable<AttendanceRecord>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            return await _attendanceRepository.GetByEmployeeIdAsync(employeeId, dateFrom, dateTo);
        }

        public async Task<IEnumerable<AttendanceRecord>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            return await _attendanceRepository.GetBySiteIdAsync(siteId, dateFrom, dateTo);
        }

        public async Task<AttendanceRecord> CheckInAsync(Guid? shiftAssignmentId, Guid employeeId, Guid siteId, DateTime date, DateTime checkInTime, string source = "MANUAL")
        {
            // Check if record already exists
            var existing = await _attendanceRepository.GetByEmployeeAndDateAsync(employeeId, date);

            if (existing != null)
            {
                if (existing.CheckInTime.HasValue)
                {
                    throw new InvalidOperationException("Employee has already checked in for this date.");
                }

                existing.CheckInTime = checkInTime;
                existing.Source = source;
                existing.UpdatedAt = DateTime.UtcNow;

                await CalculateAttendanceMetricsAsync(existing);
                await _attendanceRepository.UpdateAsync(existing);
                return existing;
            }

            // Create new record
            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                ShiftAssignmentId = shiftAssignmentId,
                EmployeeId = employeeId,
                SiteId = siteId,
                Date = date.Date,
                CheckInTime = checkInTime,
                Source = source,
                CreatedAt = DateTime.UtcNow
            };

            await CalculateAttendanceMetricsAsync(record);
            return await _attendanceRepository.AddAsync(record);
        }

        public async Task<AttendanceRecord> CheckOutAsync(Guid attendanceRecordId, DateTime checkOutTime, string source = "MANUAL")
        {
            var record = await _attendanceRepository.GetByIdAsync(attendanceRecordId);
            if (record == null)
            {
                throw new InvalidOperationException($"Attendance record with ID '{attendanceRecordId}' not found.");
            }

            if (!record.CheckInTime.HasValue)
            {
                throw new InvalidOperationException("Cannot check out without checking in first.");
            }

            if (record.CheckOutTime.HasValue)
            {
                throw new InvalidOperationException("Employee has already checked out for this date.");
            }

            record.CheckOutTime = checkOutTime;
            record.Source = source;
            record.UpdatedAt = DateTime.UtcNow;

            await CalculateAttendanceMetricsAsync(record);
            await _attendanceRepository.UpdateAsync(record);
            return record;
        }

        public async Task<AttendanceRecord> CreateOrUpdateAsync(AttendanceRecord record)
        {
            if (record.Id == Guid.Empty)
            {
                record.Id = Guid.NewGuid();
                record.CreatedAt = DateTime.UtcNow;
                await CalculateAttendanceMetricsAsync(record);
                return await _attendanceRepository.AddAsync(record);
            }
            else
            {
                record.UpdatedAt = DateTime.UtcNow;
                await CalculateAttendanceMetricsAsync(record);
                await _attendanceRepository.UpdateAsync(record);
                return record;
            }
        }

        public async Task<AttendanceRecord> UpdateAsync(AttendanceRecord record)
        {
            var existing = await _attendanceRepository.GetByIdAsync(record.Id);
            if (existing == null)
            {
                throw new InvalidOperationException($"Attendance record with ID '{record.Id}' not found.");
            }

            record.UpdatedAt = DateTime.UtcNow;
            await CalculateAttendanceMetricsAsync(record);
            await _attendanceRepository.UpdateAsync(record);
            return record;
        }

        public async Task CalculateAttendanceMetricsAsync(AttendanceRecord record)
        {
            if (record.ShiftAssignmentId.HasValue)
            {
                var shiftAssignment = await _shiftRepository.GetByIdAsync(record.ShiftAssignmentId.Value);
                if (shiftAssignment != null)
                {
                    var template = await _templateRepository.GetByIdAsync(shiftAssignment.ShiftTemplateId);
                    if (template != null)
                    {
                        var shiftDate = shiftAssignment.Date;
                        var expectedStart = shiftDate.Date.Add(template.StartTime);
                        var expectedEnd = shiftDate.Date.Add(template.EndTime);

                        // Handle night shifts
                        if (template.IsNightShift && template.EndTime < template.StartTime)
                        {
                            expectedEnd = expectedEnd.AddDays(1);
                        }

                        // Calculate late minutes
                        if (record.CheckInTime.HasValue && record.CheckInTime.Value > expectedStart)
                        {
                            record.LateMinutes = (int)(record.CheckInTime.Value - expectedStart).TotalMinutes;
                            record.Status = record.LateMinutes > 5 ? "LATE" : "PRESENT";
                        }
                        else if (record.CheckInTime.HasValue)
                        {
                            record.Status = "PRESENT";
                        }

                        // Calculate early leave and overtime
                        if (record.CheckOutTime.HasValue)
                        {
                            if (record.CheckOutTime.Value < expectedEnd)
                            {
                                record.EarlyLeaveMinutes = (int)(expectedEnd - record.CheckOutTime.Value).TotalMinutes;
                                if (record.EarlyLeaveMinutes > 0)
                                {
                                    record.Status = "EARLY_LEAVE";
                                }
                            }
                            else if (record.CheckOutTime.Value > expectedEnd)
                            {
                                record.OvertimeMinutes = (int)(record.CheckOutTime.Value - expectedEnd).TotalMinutes;
                            }
                        }
                    }
                }
            }

            // If no shift assignment, status is already set or defaults to PRESENT
            if (string.IsNullOrEmpty(record.Status))
            {
                record.Status = record.CheckInTime.HasValue ? "PRESENT" : "ABSENT";
            }
        }
    }
}

