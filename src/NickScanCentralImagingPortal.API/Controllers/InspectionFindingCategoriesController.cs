using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Read endpoints for the controlled-vocabulary inspection finding categories.
    /// Backs the analyst dropdowns added under AI training flywheel Gap 1a.
    ///
    /// Two orthogonal lists are exposed:
    ///   - GET /api/inspection-finding-categories/threat
    ///       Security domain (weapons, drugs, contraband, hazmat, ...)
    ///   - GET /api/inspection-finding-categories/revenue-anomaly
    ///       Revenue assurance (undeclared goods, undervaluation, misclassification, ...)
    ///
    /// Both endpoints return only active categories ordered by SortOrder. Pass
    /// includeInactive=true to retrieve the full list (admin / audit views).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/inspection-finding-categories")]
    public class InspectionFindingCategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<InspectionFindingCategoriesController> _logger;

        public InspectionFindingCategoriesController(
            ApplicationDbContext context,
            ILogger<InspectionFindingCategoriesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("threat")]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetThreatCategories(
            [FromQuery] bool includeInactive = false)
        {
            var query = _context.ThreatCategories.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var rows = await query
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.DisplayName)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    DisplayName = c.DisplayName,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpGet("revenue-anomaly")]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetRevenueAnomalyCategories(
            [FromQuery] bool includeInactive = false)
        {
            var query = _context.RevenueAnomalyCategories.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var rows = await query
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.DisplayName)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    DisplayName = c.DisplayName,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                })
                .ToListAsync();

            return Ok(rows);
        }
    }

    /// <summary>
    /// Wire-format projection for category dropdowns. Kept deliberately small so the
    /// API stays simple to consume from Blazor or any other front-end.
    /// </summary>
    public class CategoryDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
