using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API Controller for managing business rules and validation logic
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BusinessRulesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BusinessRulesController> _logger;

        public BusinessRulesController(
            ApplicationDbContext context,
            ILogger<BusinessRulesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all business rules
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<BusinessRuleDto>>> GetBusinessRules()
        {
            try
            {
                _logger.LogInformation("Getting business rules");

                var rules = await _context.BusinessRules
                    .OrderBy(r => r.ExecutionOrder)
                    .ThenBy(r => r.CreatedAt)
                    .ToListAsync();

                var dtos = rules.Select(r => MapToDto(r)).ToList();

                _logger.LogInformation("Retrieved {Count} business rules", dtos.Count);
                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting business rules");
                return StatusCode(500, new { error = "Failed to retrieve business rules", message = ex.Message });
            }
        }

        /// <summary>
        /// Get a specific business rule by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<BusinessRuleDto>> GetBusinessRule(int id)
        {
            try
            {
                _logger.LogInformation("Getting business rule {Id}", id);

                var rule = await _context.BusinessRules.AsTracking().FirstOrDefaultAsync(r => r.Id == id);

                if (rule == null)
                {
                    _logger.LogWarning("Business rule {Id} not found", id);
                    return NotFound(new { error = $"Business rule with ID {id} not found" });
                }

                return Ok(MapToDto(rule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting business rule {Id}", id);
                return StatusCode(500, new { error = "Failed to retrieve business rule", message = ex.Message });
            }
        }

        /// <summary>
        /// Create a new business rule
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<BusinessRuleDto>> CreateBusinessRule([FromBody] CreateBusinessRuleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation("Creating business rule: {Name}", request.Name);

                var username = User.Identity?.Name ?? "System";

                var rule = new BusinessRule
                {
                    Name = request.Name,
                    Description = request.Description,
                    Category = request.Category,
                    Priority = request.Priority,
                    ConditionExpression = request.ConditionExpression,
                    ActionType = request.ActionType,
                    ActionMessage = request.ActionMessage,
                    IsActive = request.IsActive,
                    ExecutionOrder = request.ExecutionOrder,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = username
                };

                _context.BusinessRules.Add(rule);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created business rule {Id}: {Name}", rule.Id, rule.Name);

                return CreatedAtAction(nameof(GetBusinessRule), new { id = rule.Id }, MapToDto(rule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating business rule");
                return StatusCode(500, new { error = "Failed to create business rule", message = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing business rule
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<BusinessRuleDto>> UpdateBusinessRule(int id, [FromBody] UpdateBusinessRuleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation("Updating business rule {Id}", id);

                var rule = await _context.BusinessRules.AsTracking().FirstOrDefaultAsync(r => r.Id == id);
                if (rule == null)
                {
                    _logger.LogWarning("Business rule {Id} not found", id);
                    return NotFound(new { error = $"Business rule with ID {id} not found" });
                }

                var username = User.Identity?.Name ?? "System";

                rule.Name = request.Name;
                rule.Description = request.Description;
                rule.Category = request.Category;
                rule.Priority = request.Priority;
                rule.ConditionExpression = request.ConditionExpression;
                rule.ActionType = request.ActionType;
                rule.ActionMessage = request.ActionMessage;
                rule.IsActive = request.IsActive;
                rule.ExecutionOrder = request.ExecutionOrder;
                rule.UpdatedAt = DateTime.UtcNow;
                rule.UpdatedBy = username;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Updated business rule {Id}: {Name}", rule.Id, rule.Name);

                return Ok(MapToDto(rule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating business rule {Id}", id);
                return StatusCode(500, new { error = "Failed to update business rule", message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a business rule
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteBusinessRule(int id)
        {
            try
            {
                _logger.LogInformation("Deleting business rule {Id}", id);

                var rule = await _context.BusinessRules.AsTracking().FirstOrDefaultAsync(r => r.Id == id);
                if (rule == null)
                {
                    _logger.LogWarning("Business rule {Id} not found", id);
                    return NotFound(new { error = $"Business rule with ID {id} not found" });
                }

                _context.BusinessRules.Remove(rule);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Deleted business rule {Id}: {Name}", id, rule.Name);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting business rule {Id}", id);
                return StatusCode(500, new { error = "Failed to delete business rule", message = ex.Message });
            }
        }

        /// <summary>
        /// Toggle active status of a business rule
        /// </summary>
        [HttpPut("{id}/status")]
        public async Task<ActionResult<BusinessRuleDto>> ToggleBusinessRuleStatus(int id, [FromBody] ToggleStatusRequest? request = null)
        {
            try
            {
                _logger.LogInformation("Toggling status for business rule {Id}", id);

                var rule = await _context.BusinessRules.AsTracking().FirstOrDefaultAsync(r => r.Id == id);
                if (rule == null)
                {
                    _logger.LogWarning("Business rule {Id} not found", id);
                    return NotFound(new { error = $"Business rule with ID {id} not found" });
                }

                var username = User.Identity?.Name ?? "System";

                rule.IsActive = request?.IsActive ?? !rule.IsActive;
                rule.UpdatedAt = DateTime.UtcNow;
                rule.UpdatedBy = username;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Toggled business rule {Id} status to {IsActive}", id, rule.IsActive);

                return Ok(MapToDto(rule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling business rule {Id} status", id);
                return StatusCode(500, new { error = "Failed to toggle business rule status", message = ex.Message });
            }
        }

        /// <summary>
        /// Map BusinessRule entity to DTO
        /// </summary>
        private BusinessRuleDto MapToDto(BusinessRule rule)
        {
            return new BusinessRuleDto
            {
                Id = rule.Id,
                Name = rule.Name,
                Description = rule.Description,
                Category = rule.Category,
                Priority = rule.Priority,
                ConditionExpression = rule.ConditionExpression,
                ActionType = rule.ActionType,
                ActionMessage = rule.ActionMessage,
                IsActive = rule.IsActive,
                ExecutionOrder = rule.ExecutionOrder,
                CreatedAt = rule.CreatedAt,
                CreatedBy = rule.CreatedBy,
                UpdatedAt = rule.UpdatedAt,
                UpdatedBy = rule.UpdatedBy
            };
        }

        /// <summary>
        /// Seed the database with default business rules (idempotent -- only seeds when table is empty)
        /// </summary>
        [HttpPost("seed")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> SeedBusinessRules()
        {
            try
            {
                var existingCount = await _context.BusinessRules.CountAsync();
                if (existingCount > 0)
                {
                    return Ok(new { message = $"Skipped seeding -- {existingCount} rules already exist.", seeded = 0 });
                }

                var samples = GetSampleBusinessRules();
                var entities = samples.Select(dto => new BusinessRule
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    Category = dto.Category,
                    Priority = dto.Priority,
                    ConditionExpression = dto.ConditionExpression,
                    ActionType = dto.ActionType,
                    ActionMessage = dto.ActionMessage,
                    IsActive = dto.IsActive,
                    ExecutionOrder = dto.ExecutionOrder,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = User.Identity?.Name ?? "System"
                }).ToList();

                _context.BusinessRules.AddRange(entities);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Seeded {Count} default business rules", entities.Count);
                return Ok(new { message = $"Seeded {entities.Count} default business rules.", seeded = entities.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding business rules");
                return StatusCode(500, new { error = "Failed to seed business rules", message = ex.Message });
            }
        }

        /// <summary>
        /// Get sample business rules (for migration/seeding purposes)
        /// </summary>
        private List<BusinessRuleDto> GetSampleBusinessRules()
        {
            return new List<BusinessRuleDto>
            {
                new BusinessRuleDto
                {
                    Id = 1,
                    Name = "Container Number Format Validation",
                    Description = "Validates that container numbers follow the ISO 6346 standard format (11 characters: 4 letters + 7 digits)",
                    Category = "Container Validation",
                    Priority = "High",
                    ConditionExpression = "ContainerNumber.Length == 11 AND ContainerNumber matches '[A-Z]{4}[0-9]{7}'",
                    ActionType = "Reject",
                    ActionMessage = "Invalid container number format. Must follow ISO 6346 standard.",
                    IsActive = true,
                    ExecutionOrder = 1,
                    CreatedAt = DateTime.UtcNow.AddDays(-30),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 2,
                    Name = "CMR Clearance Type Requirements",
                    Description = "CMR (Container Manifest Release) containers require Rotation Number + Container Number + BL/HouseBL. BOE Number NOT required.",
                    Category = "Document Validation",
                    Priority = "Critical",
                    ConditionExpression = "ClearanceType == 'CMR' AND (RotationNumber IS NULL OR (BLNumber IS NULL AND HouseBL IS NULL))",
                    ActionType = "Block",
                    ActionMessage = "CMR containers require Rotation Number and BL/HouseBL. BOE Number not required.",
                    IsActive = true,
                    ExecutionOrder = 2,
                    CreatedAt = DateTime.UtcNow.AddDays(-28),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 3,
                    Name = "IMEX Clearance Type Requirements",
                    Description = "IMEX (Import/Export) containers require BOE Number + Container Number + BL/HouseBL. Rotation Number NOT required.",
                    Category = "Document Validation",
                    Priority = "Critical",
                    ConditionExpression = "ClearanceType == 'IMEX' AND (BOENumber IS NULL OR (BLNumber IS NULL AND HouseBL IS NULL))",
                    ActionType = "Block",
                    ActionMessage = "IMEX containers require BOE Number and BL/HouseBL. Rotation Number not required.",
                    IsActive = true,
                    ExecutionOrder = 3,
                    CreatedAt = DateTime.UtcNow.AddDays(-28),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 4,
                    Name = "BOE Number Required for IMEX",
                    Description = "IMEX clearance type requires a valid BOE (Bill of Entry) number. CMR clearance does not require BOE.",
                    Category = "Document Validation",
                    Priority = "Critical",
                    ConditionExpression = "ClearanceType == 'IMEX' AND (BOENumber IS NULL OR BOENumber == '')",
                    ActionType = "Block",
                    ActionMessage = "BOE number is required for IMEX clearance type containers",
                    IsActive = true,
                    ExecutionOrder = 4,
                    CreatedAt = DateTime.UtcNow.AddDays(-25),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 5,
                    Name = "ICUMS Data Completeness Check",
                    Description = "Validates that required ICUMS fields are present before submission",
                    Category = "ICUMS Integration",
                    Priority = "High",
                    ConditionExpression = "ConsigneeCode IS NOT NULL AND ExitPort IS NOT NULL AND DestinationCountry IS NOT NULL",
                    ActionType = "Block",
                    ActionMessage = "Missing required ICUMS fields. Cannot submit to ICUMS.",
                    IsActive = true,
                    ExecutionOrder = 5,
                    CreatedAt = DateTime.UtcNow.AddDays(-20),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 6,
                    Name = "Data Completeness Threshold",
                    Description = "Requires 100% data completeness before allowing ICUMS submission",
                    Category = "Data Completeness",
                    Priority = "High",
                    ConditionExpression = "DataCompletenessPercentage < 100 AND IsReadyForSubmission == true",
                    ActionType = "Block",
                    ActionMessage = "Data completeness must be 100% before submission. Current: {DataCompletenessPercentage}%",
                    IsActive = true,
                    ExecutionOrder = 6,
                    CreatedAt = DateTime.UtcNow.AddDays(-18),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 7,
                    Name = "Image Quality Threshold",
                    Description = "Ensures scanned images meet minimum quality standards",
                    Category = "Image Analysis",
                    Priority = "Medium",
                    ConditionExpression = "ImageResolution >= 1024 AND FileSize > 50KB",
                    ActionType = "Warn",
                    ActionMessage = "Image quality below recommended threshold. Manual review required.",
                    IsActive = true,
                    ExecutionOrder = 7,
                    CreatedAt = DateTime.UtcNow.AddDays(-15),
                    CreatedBy = "admin"
                },

                new BusinessRuleDto
                {
                    Id = 8,
                    Name = "Image Analysis Status Transition Validation",
                    Description = "Enforces valid status transitions: Ready → AnalystAssigned → AnalystCompleted → AuditAssigned → AuditCompleted → Submitted → Completed",
                    Category = "Image Analysis",
                    Priority = "High",
                    ConditionExpression = "StatusTransition IN ('Ready→AnalystAssigned', 'AnalystAssigned→AnalystCompleted', 'AnalystCompleted→AuditAssigned', 'AuditAssigned→AuditCompleted', 'AuditCompleted→Submitted', 'Submitted→Completed')",
                    ActionType = "Block",
                    ActionMessage = "Invalid status transition. Only allowed transitions: Ready→AnalystAssigned→AnalystCompleted→AuditAssigned→AuditCompleted→Submitted→Completed",
                    IsActive = true,
                    ExecutionOrder = 8,
                    CreatedAt = DateTime.UtcNow.AddDays(-12),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 9,
                    Name = "Workflow Stage Progression",
                    Description = "Enforces sequential workflow progression: Pending → ImageAnalysis → Audit → Completed. Stages cannot be skipped.",
                    Category = "Image Analysis",
                    Priority = "Medium",
                    ConditionExpression = "WorkflowStage IN ('Pending', 'ImageAnalysis', 'Audit', 'Completed') AND NextStage == Sequential(CurrentStage)",
                    ActionType = "Block",
                    ActionMessage = "Cannot skip workflow stages. Must progress sequentially: Pending → ImageAnalysis → Audit → Completed",
                    IsActive = true,
                    ExecutionOrder = 9,
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 10,
                    Name = "Loose Cargo Identification",
                    Description = "Identifies records with null/empty/N/A container numbers as loose cargo (VIN records, non-containerized cargo, bulk commodities)",
                    Category = "Data Completeness",
                    Priority = "Medium",
                    ConditionExpression = "ContainerNumber IS NULL OR ContainerNumber == '' OR ContainerNumber == 'N/A' OR ContainerNumber IN ('BULK', 'LOOSE')",
                    ActionType = "Flag",
                    ActionMessage = "Record identified as loose cargo (no container number). Process as VIN/non-containerized cargo.",
                    IsActive = true,
                    ExecutionOrder = 10,
                    CreatedAt = DateTime.UtcNow.AddDays(-8),
                    CreatedBy = "System"
                },

                new BusinessRuleDto
                {
                    Id = 11,
                    Name = "Duplicate Container Check",
                    Description = "Prevents processing of duplicate container scans within 24 hours",
                    Category = "Data Completeness",
                    Priority = "Medium",
                    ConditionExpression = "NOT EXISTS (SELECT 1 FROM Containers WHERE ContainerNumber = @ContainerNumber AND ScanDate >= DATEADD(HOUR, -24, GETDATE()))",
                    ActionType = "Block",
                    ActionMessage = "Container already scanned within the last 24 hours",
                    IsActive = false,
                    ExecutionOrder = 11,
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    CreatedBy = "admin"
                },

                new BusinessRuleDto
                {
                    Id = 12,
                    Name = "Vehicle Registration Validation",
                    Description = "Validates vehicle registration numbers against Ghana format",
                    Category = "Container Validation",
                    Priority = "Low",
                    ConditionExpression = "VehicleReg matches 'GH-[0-9]+-[A-Z0-9]+' OR VehicleReg matches '[A-Z]{2,3}-[0-9]+-[A-Z0-9]+'",
                    ActionType = "Warn",
                    ActionMessage = "Vehicle registration format may be incorrect",
                    IsActive = true,
                    ExecutionOrder = 12,
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                    CreatedBy = "admin"
                },

                new BusinessRuleDto
                {
                    Id = 13,
                    Name = "Invalid Container Number Filtering",
                    Description = "Filters out invalid/placeholder container numbers (XXXX, SSSS, Unknown, PLACEHOLDER, CONTAINER). Container numbers must be at least 8 characters, start with 4 letters, and contain no spaces.",
                    Category = "Container Validation",
                    Priority = "Critical",
                    ConditionExpression = "ContainerNumber.Length >= 8 AND ContainerNumber starts with 4 letters AND ContainerNumber NOT IN ('XXXX', 'SSSS', 'Unknown', 'PLACEHOLDER', 'CONTAINER') AND ContainerNumber NOT LIKE '% %'",
                    ActionType = "Filter",
                    ActionMessage = "Invalid container number filtered out. Must be at least 8 characters, start with 4 letters, and not be a placeholder value.",
                    IsActive = true,
                    ExecutionOrder = 13,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    CreatedBy = "System"
                }
            };
        }
    }

    /// <summary>
    /// Business Rule DTO matching frontend BusinessRuleItem model
    /// </summary>
    public class BusinessRuleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Priority { get; set; } = "Medium"; // Critical, High, Medium, Low
        public string ConditionExpression { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // Block, Reject, Warn, Flag, Filter, Notify
        public string ActionMessage { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int ExecutionOrder { get; set; } = 0;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Request DTO for creating a business rule
    /// </summary>
    public class CreateBusinessRuleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Priority { get; set; } = "Medium";
        public string ConditionExpression { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionMessage { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int ExecutionOrder { get; set; } = 0;
    }

    /// <summary>
    /// Request DTO for updating a business rule
    /// </summary>
    public class UpdateBusinessRuleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Priority { get; set; } = "Medium";
        public string ConditionExpression { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionMessage { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int ExecutionOrder { get; set; } = 0;
    }

    public class ToggleStatusRequest
    {
        public bool IsActive { get; set; }
    }
}

