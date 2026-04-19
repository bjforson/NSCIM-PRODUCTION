using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CrossRecordScansController : ControllerBase
    {
        private readonly ICrossRecordScanRepository _crossRecordRepo;
        private readonly IIcumDownloadsRepository _icumDownloadsRepo;
        private readonly ILogger<CrossRecordScansController> _logger;

        public CrossRecordScansController(
            ICrossRecordScanRepository crossRecordRepo,
            IIcumDownloadsRepository icumDownloadsRepo,
            ILogger<CrossRecordScansController> logger)
        {
            _crossRecordRepo = crossRecordRepo;
            _icumDownloadsRepo = icumDownloadsRepo;
            _logger = logger;
        }

        /// <summary>
        /// Get analytics data for cross-record scans
        /// </summary>
        [HttpGet("analytics")]
        public async Task<ActionResult<CrossRecordAnalytics>> GetAnalytics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var analytics = await _crossRecordRepo.GetAnalyticsAsync(startDate, endDate);
                _logger.LogInformation("Retrieved cross-record analytics: {Count} total scans",
                    analytics.TotalCrossRecordScans);
                return Ok(analytics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cross-record analytics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get paginated list of cross-record scans
        /// </summary>
        [HttpGet("list")]
        public async Task<ActionResult<PagedResult<CrossRecordScanDto>>> GetList(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? severity = null,
            [FromQuery] string? scannerType = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var (items, totalCount) = await _crossRecordRepo.GetPagedListAsync(
                    page, pageSize, severity, scannerType, startDate, endDate);

                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var result = new PagedResult<CrossRecordScanDto>
                {
                    Data = items.Select(s => new CrossRecordScanDto
                    {
                        Id = s.Id,
                        OriginalScanRecord = s.OriginalScanRecord,
                        ScannerRecordId = s.ScannerRecordId,
                        ScannerType = s.ScannerType,
                        ScanDateTime = s.ScanDateTime,
                        Container1 = s.Container1,
                        Container1_BOE = s.Container1_BOE,
                        Container1_Consignee = s.Container1_Consignee,
                        Container1_CRMS = s.Container1_CRMS,
                        Container1_ClearanceType = s.Container1_ClearanceType,
                        Container2 = s.Container2,
                        Container2_BOE = s.Container2_BOE,
                        Container2_Consignee = s.Container2_Consignee,
                        Container2_CRMS = s.Container2_CRMS,
                        Container2_ClearanceType = s.Container2_ClearanceType,
                        CrossRecordType = s.CrossRecordType,
                        Severity = s.Severity,
                        RequiresReview = s.RequiresReview,
                        SameDeclaration = s.SameDeclaration,
                        SameConsignee = s.SameConsignee,
                        SameMasterBL = s.SameMasterBL,
                        ReviewStatus = s.ReviewStatus,
                        ReviewedAt = s.ReviewedAt,
                        ReviewedBy = s.ReviewedBy
                    }).ToList(),
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cross-record scan list");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get cross-record info for a specific container
        /// </summary>
        [HttpGet("container/{containerNumber}")]
        public async Task<ActionResult<CrossRecordImageInfo>> GetByContainer(string containerNumber)
        {
            try
            {
                var crossRecord = await _crossRecordRepo.GetByContainerAsync(containerNumber);

                if (crossRecord == null)
                {
                    return Ok(new CrossRecordImageInfo { IsCrossRecordImage = false });
                }

                var isContainer1 = crossRecord.Container1 == containerNumber;
                var siblingContainer = isContainer1 ? crossRecord.Container2 : crossRecord.Container1;

                var info = new CrossRecordImageInfo
                {
                    IsCrossRecordImage = true,
                    OriginalScanRecord = crossRecord.OriginalScanRecord,
                    CurrentContainer = containerNumber,
                    PositionInScan = isContainer1 ? 1 : 2,
                    SiblingContainer = siblingContainer,
                    CrossRecordType = crossRecord.CrossRecordType,
                    Severity = crossRecord.Severity,
                    SiblingBOE = isContainer1 ? crossRecord.Container2_BOE : crossRecord.Container1_BOE,
                    SiblingConsignee = isContainer1 ? crossRecord.Container2_Consignee : crossRecord.Container1_Consignee,
                    SiblingCRMS = isContainer1 ? crossRecord.Container2_CRMS : crossRecord.Container1_CRMS
                };

                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cross-record info for container {Container}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get single cross-record scan by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<CrossRecordScanDto>> GetById(int id)
        {
            try
            {
                var scan = await _crossRecordRepo.GetByIdAsync(id);
                if (scan == null)
                    return NotFound($"Cross-record scan with ID {id} not found");

                return Ok(new CrossRecordScanDto
                {
                    Id = scan.Id,
                    OriginalScanRecord = scan.OriginalScanRecord,
                    ScannerRecordId = scan.ScannerRecordId,
                    ScannerType = scan.ScannerType,
                    ScanDateTime = scan.ScanDateTime,
                    Container1 = scan.Container1,
                    Container1_BOE = scan.Container1_BOE,
                    Container1_Consignee = scan.Container1_Consignee,
                    Container1_CRMS = scan.Container1_CRMS,
                    Container1_ClearanceType = scan.Container1_ClearanceType,
                    Container2 = scan.Container2,
                    Container2_BOE = scan.Container2_BOE,
                    Container2_Consignee = scan.Container2_Consignee,
                    Container2_CRMS = scan.Container2_CRMS,
                    Container2_ClearanceType = scan.Container2_ClearanceType,
                    CrossRecordType = scan.CrossRecordType,
                    Severity = scan.Severity,
                    RequiresReview = scan.RequiresReview,
                    SameDeclaration = scan.SameDeclaration,
                    SameConsignee = scan.SameConsignee,
                    SameMasterBL = scan.SameMasterBL,
                    ReviewStatus = scan.ReviewStatus,
                    ReviewedAt = scan.ReviewedAt,
                    ReviewedBy = scan.ReviewedBy
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cross-record scan {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Mark a cross-record scan as reviewed
        /// </summary>
        [HttpPost("{id}/review")]
        public async Task<ActionResult> MarkAsReviewed(int id, [FromBody] ReviewRequest request)
        {
            try
            {
                await _crossRecordRepo.MarkAsReviewedAsync(id, request.ReviewedBy, request.Notes);
                _logger.LogInformation("Cross-record scan {Id} marked as reviewed by {User}", id, request.ReviewedBy);
                return Ok(new { message = "Scan marked as reviewed successfully" });
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Cross-record scan with ID {id} not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking scan {Id} as reviewed", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Analyze container pairs to show how they were processed
        /// </summary>
        [HttpPost("analyze-pairs")]
        public async Task<ActionResult<ContainerPairAnalysisResult>> AnalyzeContainerPairs(
            [FromBody] ContainerPairAnalysisRequest request)
        {
            try
            {
                var result = new ContainerPairAnalysisResult { Pairs = new List<ContainerPairAnalysis>() };

                foreach (var pair in request.Pairs)
                {
                    var analysis = new ContainerPairAnalysis
                    {
                        PairName = pair.PairName ?? $"Pair {result.Pairs.Count + 1}",
                        Container1 = pair.Container1,
                        Container2 = pair.Container2
                    };

                    // Get BOE documents via repository
                    var boeDocs1 = await _icumDownloadsRepo.GetBOEDocumentsByContainerNumberAsync(pair.Container1);
                    analysis.Container1BOEDocuments = boeDocs1.Select(b => new BOEDocumentSummary
                    {
                        Id = b.Id,
                        DeclarationNumber = b.DeclarationNumber,
                        ConsigneeName = b.ConsigneeName,
                        ClearanceType = b.ClearanceType,
                        CrmsLevel = b.CrmsLevel,
                        BlNumber = b.BlNumber,
                        RotationNumber = b.RotationNumber,
                        ProcessingStatus = b.ProcessingStatus,
                        CreatedAt = b.CreatedAt
                    }).ToList();

                    var boeDocs2 = await _icumDownloadsRepo.GetBOEDocumentsByContainerNumberAsync(pair.Container2);
                    analysis.Container2BOEDocuments = boeDocs2.Select(b => new BOEDocumentSummary
                    {
                        Id = b.Id,
                        DeclarationNumber = b.DeclarationNumber,
                        ConsigneeName = b.ConsigneeName,
                        ClearanceType = b.ClearanceType,
                        CrmsLevel = b.CrmsLevel,
                        BlNumber = b.BlNumber,
                        RotationNumber = b.RotationNumber,
                        ProcessingStatus = b.ProcessingStatus,
                        CreatedAt = b.CreatedAt
                    }).ToList();

                    // Determine relationship status
                    if (!boeDocs1.Any() || !boeDocs2.Any())
                    {
                        analysis.RelationshipStatus = "Pending BOE Data";
                        analysis.Classification = "Pending";
                    }
                    else
                    {
                        var boe1 = boeDocs1.First();
                        var boe2 = boeDocs2.First();

                        var sameDeclaration = string.Equals(boe1.DeclarationNumber, boe2.DeclarationNumber, StringComparison.OrdinalIgnoreCase);
                        var sameMasterBL = !string.IsNullOrEmpty(boe1.BlNumber) &&
                                         !string.IsNullOrEmpty(boe2.BlNumber) &&
                                         string.Equals(boe1.BlNumber, boe2.BlNumber, StringComparison.OrdinalIgnoreCase);
                        var sameConsignee = string.Equals(boe1.ConsigneeName, boe2.ConsigneeName, StringComparison.OrdinalIgnoreCase);
                        var sameClearanceType = string.Equals(boe1.ClearanceType, boe2.ClearanceType, StringComparison.OrdinalIgnoreCase);
                        var sameCRMS = string.Equals(boe1.CrmsLevel, boe2.CrmsLevel, StringComparison.OrdinalIgnoreCase);

                        if (sameDeclaration || sameMasterBL)
                        {
                            analysis.RelationshipStatus = sameDeclaration
                                ? "Same Declaration (Same Record)"
                                : "Same Master BL (Consolidated)";
                            analysis.Classification = "Normal";
                        }
                        else
                        {
                            if (!sameConsignee)
                                analysis.RelationshipStatus = "Different Importers (CROSS-RECORD)";
                            else if (!sameClearanceType)
                                analysis.RelationshipStatus = "Different Clearance Types (CROSS-RECORD)";
                            else if (!sameCRMS)
                                analysis.RelationshipStatus = "Different CRMS Levels (CROSS-RECORD)";
                            else
                                analysis.RelationshipStatus = "Different BOEs (CROSS-RECORD)";
                            analysis.Classification = "Cross-Record";
                        }
                    }

                    // Check if CrossRecordScan entry exists
                    var crossRecord = await _crossRecordRepo.FindByContainerPairAsync(pair.Container1, pair.Container2);
                    if (crossRecord != null)
                    {
                        analysis.CrossRecordScanId = crossRecord.Id;
                        analysis.CrossRecordType = crossRecord.CrossRecordType;
                        analysis.Severity = crossRecord.Severity;
                        analysis.ReviewStatus = crossRecord.ReviewStatus;
                    }

                    result.Pairs.Add(analysis);
                }

                _logger.LogInformation("Analyzed {Count} container pairs", result.Pairs.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing container pairs");
                return StatusCode(500, "Internal server error");
            }
        }

        public class ReviewRequest
        {
            public string ReviewedBy { get; set; } = string.Empty;
            public string? Notes { get; set; }
        }

        public class ContainerPairAnalysisRequest
        {
            public List<ContainerPair> Pairs { get; set; } = new();
        }

        public class ContainerPair
        {
            public string Container1 { get; set; } = string.Empty;
            public string Container2 { get; set; } = string.Empty;
            public string? PairName { get; set; }
        }

        public class ContainerPairAnalysisResult
        {
            public List<ContainerPairAnalysis> Pairs { get; set; } = new();
        }

        public class ContainerPairAnalysis
        {
            public string PairName { get; set; } = string.Empty;
            public string Container1 { get; set; } = string.Empty;
            public string Container2 { get; set; } = string.Empty;
            public List<BOEDocumentSummary> Container1BOEDocuments { get; set; } = new();
            public List<BOEDocumentSummary> Container2BOEDocuments { get; set; } = new();
            public string RelationshipStatus { get; set; } = string.Empty;
            public string Classification { get; set; } = string.Empty;
            public int? CrossRecordScanId { get; set; }
            public string? CrossRecordType { get; set; }
            public string? Severity { get; set; }
            public string? ReviewStatus { get; set; }
        }

        public class BOEDocumentSummary
        {
            public int Id { get; set; }
            public string? DeclarationNumber { get; set; }
            public string? ConsigneeName { get; set; }
            public string? ClearanceType { get; set; }
            public string? CrmsLevel { get; set; }
            public string? BlNumber { get; set; }
            public string? RotationNumber { get; set; }
            public string? ProcessingStatus { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
