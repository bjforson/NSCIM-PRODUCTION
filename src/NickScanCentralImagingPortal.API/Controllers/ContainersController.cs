using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using NickScanCentralImagingPortal.API.Attributes;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ContainersController : ControllerBase
    {
        private readonly IImageProcessingOrchestrator _orchestrator;
        private readonly IContainerRepository _containerRepository;
        private readonly ILogger<ContainersController> _logger;
        private readonly IConfiguration _configuration;

        public ContainersController(
            IImageProcessingOrchestrator orchestrator,
            IContainerRepository containerRepository,
            ILogger<ContainersController> logger,
            IConfiguration configuration)
        {
            _orchestrator = orchestrator;
            _containerRepository = containerRepository;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        [Cached(300)] // Cache for 5 minutes
        public async Task<ActionResult<IEnumerable<Container>>> GetContainers()
        {
            try
            {
                var containers = await _containerRepository.GetAllAsync();
                return Ok(containers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve containers");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Container>> GetContainer(int id)
        {
            try
            {
                var container = await _containerRepository.GetByIdAsync(id);
                if (container == null)
                {
                    return NotFound();
                }
                return Ok(container);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve container {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("by-container-id/{containerId}")]
        public async Task<ActionResult<Container>> GetContainerByContainerId(string containerId)
        {
            try
            {
                var container = await _containerRepository.GetByContainerIdAsync(containerId);
                if (container == null)
                {
                    return NotFound();
                }
                return Ok(container);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve container by container ID {ContainerId}", containerId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("process")]
        public async Task<ActionResult<ScannerProcessingResult>> ProcessContainer([FromBody] ProcessContainerRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ContainerId) || string.IsNullOrEmpty(request.ScannerType))
                {
                    return BadRequest("ContainerId and ScannerType are required");
                }

                var result = await _orchestrator.ProcessContainerAsync(request.ContainerId, request.ScannerType);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process container {ContainerId}", request.ContainerId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("scanner-types")]
        public Task<ActionResult<IEnumerable<string>>> GetScannerTypes()
        {
            try
            {
                var scannerTypes = _orchestrator.GetAvailableScannerTypes();
                return Task.FromResult<ActionResult<IEnumerable<string>>>(Ok(scannerTypes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve scanner types");
                return Task.FromResult<ActionResult<IEnumerable<string>>>(StatusCode(500, "Internal server error"));
            }
        }

        /// <summary>
        /// Enriched container groups with KPIs, decision progress, and assignment info.
        /// Uses raw SQL for speed — joins completeness, decisions, and assignments.
        /// </summary>
        [HttpGet("enriched")]
        public async Task<IActionResult> GetEnrichedContainerGroups(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? stage = null,
            [FromQuery] string? cargoType = null,
            [FromQuery] string? clearanceType = null)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("NS_CIS_Connection");
                if (string.IsNullOrEmpty(connStr))
                    return StatusCode(500, new { error = "Database connection not configured" });

                var offset = (page - 1) * pageSize;

                // Build WHERE clauses
                var whereClauses = new List<string>();
                var parameters = new List<NpgsqlParameter>();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    whereClauses.Add("(g.groupidentifier ILIKE @search OR g.normalizedgroupidentifier ILIKE @search)");
                    parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
                }
                if (!string.IsNullOrWhiteSpace(stage))
                {
                    whereClauses.Add("g.status = @stage");
                    parameters.Add(new NpgsqlParameter("@stage", stage));
                }
                if (!string.IsNullOrWhiteSpace(cargoType))
                {
                    whereClauses.Add("g.grouptype = @cargoType");
                    parameters.Add(new NpgsqlParameter("@cargoType", cargoType));
                }

                var whereClause = whereClauses.Any() ? "WHERE " + string.Join(" AND ", whereClauses) : "";

                var sql = $@"
                    WITH group_data AS (
                        SELECT
                            g.id, g.groupidentifier, g.grouptype, g.status,
                            g.scannertype, COALESCE(g.totalcontainercount, 0) AS containercount,
                            g.createdatutc, g.updatedatutc,
                            COUNT(DISTINCT d.id) AS decision_count,
                            COUNT(DISTINCT CASE WHEN d.decision = 'Normal' THEN d.id END) AS normal_count,
                            COUNT(DISTINCT CASE WHEN d.decision = 'Abnormal' THEN d.id END) AS abnormal_count,
                            (SELECT aa.assignedto FROM analysisassignments aa WHERE aa.groupid = g.id AND aa.state = 'Active' ORDER BY aa.createdatutc DESC LIMIT 1) AS assigned_to,
                            (SELECT aa.role FROM analysisassignments aa WHERE aa.groupid = g.id AND aa.state = 'Active' ORDER BY aa.createdatutc DESC LIMIT 1) AS assigned_role
                        FROM analysisgroups g
                        LEFT JOIN imageanalysisdecisions d ON d.groupidentifier = g.groupidentifier
                        {whereClause}
                        GROUP BY g.id
                        ORDER BY g.updatedatutc DESC NULLS LAST, g.createdatutc DESC
                        LIMIT @pageSize OFFSET @offset
                    )
                    SELECT * FROM group_data;

                    SELECT COUNT(*) FROM analysisgroups g {whereClause};
                ";

                parameters.Add(new NpgsqlParameter("@pageSize", pageSize));
                parameters.Add(new NpgsqlParameter("@offset", offset));

                var groups = new List<object>();
                int totalCount = 0;
                int totalGroups = 0, inAnalysis = 0, inAudit = 0, completed = 0, pendingSubmit = 0;

                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    foreach (var p in parameters) cmd.Parameters.Add(p);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        groups.Add(new
                        {
                            id = reader.GetGuid(reader.GetOrdinal("id")).ToString(),
                            groupIdentifier = reader.GetString(reader.GetOrdinal("groupidentifier")),
                            cargoType = reader.IsDBNull(reader.GetOrdinal("grouptype")) ? "" : reader.GetString(reader.GetOrdinal("grouptype")),
                            status = reader.IsDBNull(reader.GetOrdinal("status")) ? "" : reader.GetString(reader.GetOrdinal("status")),
                            clearanceType = reader.IsDBNull(reader.GetOrdinal("scannertype")) ? "" : reader.GetString(reader.GetOrdinal("scannertype")),
                            containerCount = reader.GetInt32(reader.GetOrdinal("containercount")),
                            createdAt = reader.IsDBNull(reader.GetOrdinal("createdatutc")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("createdatutc")),
                            updatedAt = reader.IsDBNull(reader.GetOrdinal("updatedatutc")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updatedatutc")),
                            decisionCount = reader.GetInt64(reader.GetOrdinal("decision_count")),
                            normalCount = reader.GetInt64(reader.GetOrdinal("normal_count")),
                            abnormalCount = reader.GetInt64(reader.GetOrdinal("abnormal_count")),
                            assignedTo = reader.IsDBNull(reader.GetOrdinal("assigned_to")) ? null : reader.GetString(reader.GetOrdinal("assigned_to")),
                            assignedRole = reader.IsDBNull(reader.GetOrdinal("assigned_role")) ? null : reader.GetString(reader.GetOrdinal("assigned_role"))
                        });
                    }

                    if (await reader.NextResultAsync() && await reader.ReadAsync())
                        totalCount = reader.GetInt32(0);
                }

                // KPI query
                await using (var kpiCmd = new NpgsqlCommand(@"
                    SELECT
                        COUNT(*) AS total,
                        COUNT(*) FILTER (WHERE status IN ('Ready','AnalystAssigned')) AS in_analysis,
                        COUNT(*) FILTER (WHERE status IN ('AuditAssigned','AnalystCompleted')) AS in_audit,
                        COUNT(*) FILTER (WHERE status = 'Completed') AS completed,
                        COUNT(*) FILTER (WHERE status = 'PendingSubmission') AS pending_submit
                    FROM analysisgroups", conn))
                {
                    await using var kpiReader = await kpiCmd.ExecuteReaderAsync();
                    if (await kpiReader.ReadAsync())
                    {
                        totalGroups = kpiReader.GetInt32(0);
                        inAnalysis = kpiReader.GetInt32(1);
                        inAudit = kpiReader.GetInt32(2);
                        completed = kpiReader.GetInt32(3);
                        pendingSubmit = kpiReader.GetInt32(4);
                    }
                }

                return Ok(new
                {
                    groups,
                    totalCount,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    kpis = new { totalGroups, inAnalysis, inAudit, completed, pendingSubmit }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading enriched container groups");
                return StatusCode(500, new { error = "Failed to load container data", details = ex.Message });
            }
        }
    }

    public class ProcessContainerRequest
    {
        public string ContainerId { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
    }
}
