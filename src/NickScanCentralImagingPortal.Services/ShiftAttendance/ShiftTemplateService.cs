using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ShiftAttendance
{
    public class ShiftTemplateService : IShiftTemplateService
    {
        private readonly IShiftTemplateRepository _repository;
        private readonly ILogger<ShiftTemplateService> _logger;

        public ShiftTemplateService(
            IShiftTemplateRepository repository,
            ILogger<ShiftTemplateService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ShiftTemplate?> GetByIdAsync(Guid id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<ShiftTemplate?> GetByCodeAsync(string code)
        {
            return await _repository.GetByCodeAsync(code);
        }

        public async Task<IEnumerable<ShiftTemplate>> GetAllAsync(Guid? siteId = null, bool activeOnly = true)
        {
            var templates = await _repository.GetBySiteIdAsync(siteId);

            if (activeOnly)
            {
                templates = templates.Where(t => t.Status == "ACTIVE");
            }

            return templates;
        }

        public async Task<ShiftTemplate> CreateAsync(ShiftTemplate template)
        {
            // Validate code uniqueness
            var existing = await _repository.GetByCodeAsync(template.Code);
            if (existing != null)
            {
                throw new InvalidOperationException($"Shift template with code '{template.Code}' already exists.");
            }

            // Calculate duration and night shift flag
            CalculateShiftDuration(template);

            template.Id = Guid.NewGuid();
            template.CreatedAt = DateTime.UtcNow;

            return await _repository.AddAsync(template);
        }

        public async Task<ShiftTemplate> UpdateAsync(ShiftTemplate template)
        {
            var existing = await _repository.GetByIdAsync(template.Id);
            if (existing == null)
            {
                throw new InvalidOperationException($"Shift template with ID '{template.Id}' not found.");
            }

            // Validate code uniqueness if changed
            if (existing.Code != template.Code)
            {
                var codeExists = await _repository.GetByCodeAsync(template.Code);
                if (codeExists != null)
                {
                    throw new InvalidOperationException($"Shift template with code '{template.Code}' already exists.");
                }
            }

            // Recalculate duration
            CalculateShiftDuration(template);

            template.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(template);
            return template;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var template = await _repository.GetByIdAsync(id);
            if (template == null)
            {
                return false;
            }

            // Soft delete by setting status to INACTIVE
            template.Status = "INACTIVE";
            template.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(template);
            return true;
        }

        public async Task<bool> ExistsAsync(string code)
        {
            var template = await _repository.GetByCodeAsync(code);
            return template != null;
        }

        private void CalculateShiftDuration(ShiftTemplate template)
        {
            if (template.EndTime >= template.StartTime)
            {
                // Same day shift
                template.DurationHours = (decimal)(template.EndTime - template.StartTime).TotalHours;
                template.IsNightShift = false;
            }
            else
            {
                // Overnight shift (crosses midnight)
                var duration = (TimeSpan.FromDays(1) - template.StartTime) + template.EndTime;
                template.DurationHours = (decimal)duration.TotalHours;
                template.IsNightShift = true;
            }
        }
    }
}

