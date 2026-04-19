using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for error investigation management
    /// Allows admins to view investigations, approve/reject fixes, and manage the error investigation workflow
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class ErrorInvestigationController : ControllerBase
    {
        private readonly IErrorInvestigationService _investigationService;
        private readonly IFixImplementationService _fixImplementationService;
        private readonly ILogger<ErrorInvestigationController> _logger;

        public ErrorInvestigationController(
            IErrorInvestigationService investigationService,
            IFixImplementationService fixImplementationService,
            ILogger<ErrorInvestigationController> logger)
        {
            _investigationService = investigationService;
            _fixImplementationService = fixImplementationService;
            _logger = logger;
        }

        /// <summary>
        /// Get all investigations with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PagedResult<ErrorInvestigationDto>>> GetInvestigations(
            [FromQuery] string? status = null,
            [FromQuery] string? priority = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var investigations = await _investigationService.GetInvestigationsAsync(
                    status, priority, search, page, pageSize);

                var totalCount = await _investigationService.GetInvestigationsAsync(
                    status, priority, search, 1, int.MaxValue);

                var result = new PagedResult<ErrorInvestigationDto>
                {
                    Data = investigations,
                    TotalCount = totalCount.Count,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount.Count / pageSize)
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving investigations");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get investigation details by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ErrorInvestigationDto>> GetInvestigation(long id)
        {
            try
            {
                var investigation = await _investigationService.GetInvestigationAsync(id, CancellationToken.None);

                if (investigation == null)
                {
                    return NotFound($"Investigation {id} not found");
                }

                return Ok(investigation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving investigation {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Approve a fix proposal and trigger implementation
        /// </summary>
        [HttpPost("{investigationId}/proposals/{proposalId}/approve")]
        public async Task<ActionResult> ApproveFixProposal(
            long investigationId,
            long proposalId,
            [FromBody] ApprovalRequest request)
        {
            try
            {
                var username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

                var result = await _investigationService.ApproveFixProposalAsync(
                    investigationId, proposalId, username, request.Notes);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }

                // Trigger implementation
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _fixImplementationService.ImplementFixAsync(proposalId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error implementing fix for proposal {ProposalId}", proposalId);
                    }
                });

                return Ok(new { message = "Fix proposal approved. Implementation started.", branchName = result.BranchName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving fix proposal {ProposalId}", proposalId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Reject a fix proposal
        /// </summary>
        [HttpPost("{investigationId}/proposals/{proposalId}/reject")]
        public async Task<ActionResult> RejectFixProposal(
            long investigationId,
            long proposalId,
            [FromBody] RejectionRequest request)
        {
            try
            {
                var username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

                var result = await _investigationService.RejectFixProposalAsync(
                    investigationId, proposalId, username, request.Reason);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }

                return Ok(new { message = "Fix proposal rejected" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting fix proposal {ProposalId}", proposalId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Mark investigation as ignored (won't be processed further)
        /// </summary>
        [HttpPost("{investigationId}/ignore")]
        public async Task<ActionResult> IgnoreInvestigation(
            long investigationId,
            [FromBody] IgnoreRequest request)
        {
            try
            {
                var username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

                var result = await _investigationService.IgnoreInvestigationAsync(
                    investigationId, username, request.Reason);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }

                return Ok(new { message = "Investigation ignored" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ignoring investigation {Id}", investigationId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get statistics for dashboard
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<InvestigationStatistics>> GetStatistics()
        {
            try
            {
                var allInvestigations = await _investigationService.GetInvestigationsAsync(
                    null, null, null, 1, int.MaxValue);

                var stats = new InvestigationStatistics
                {
                    Total = allInvestigations.Count,
                    New = allInvestigations.Count(i => i.Status == "New"),
                    Investigating = allInvestigations.Count(i => i.Status == "Investigating"),
                    Proposed = allInvestigations.Count(i => i.Status == "Proposed"),
                    Approved = allInvestigations.Count(i => i.Status == "Approved"),
                    Fixed = allInvestigations.Count(i => i.Status == "Fixed"),
                    Ignored = allInvestigations.Count(i => i.Status == "Ignored"),
                    Critical = allInvestigations.Count(i => i.Priority == "Critical"),
                    High = allInvestigations.Count(i => i.Priority == "High"),
                    Medium = allInvestigations.Count(i => i.Priority == "Medium"),
                    Low = allInvestigations.Count(i => i.Priority == "Low")
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving investigation statistics");
                return StatusCode(500, "Internal server error");
            }
        }
    }

    public class ApprovalRequest
    {
        public string? Notes { get; set; }
    }

    public class RejectionRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class IgnoreRequest
    {
        public string? Reason { get; set; }
    }

    public class InvestigationStatistics
    {
        public int Total { get; set; }
        public int New { get; set; }
        public int Investigating { get; set; }
        public int Proposed { get; set; }
        public int Approved { get; set; }
        public int Fixed { get; set; }
        public int Ignored { get; set; }
        public int Critical { get; set; }
        public int High { get; set; }
        public int Medium { get; set; }
        public int Low { get; set; }
    }

}

