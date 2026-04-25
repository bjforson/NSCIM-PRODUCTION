using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class LogManagementController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LogManagementController> _logger;
        private readonly string _connectionString;

        public LogManagementController(ApplicationDbContext context, ILogger<LogManagementController> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            // No hardcoded fallback — fail fast at construction time if the connection string is missing,
            // so the app can't accidentally point at a developer-local DB in production.
            _connectionString = configuration.GetConnectionString("NS_CIS_Connection")
                ?? throw new InvalidOperationException(
                    "Connection string 'NS_CIS_Connection' is not configured. " +
                    "Set ConnectionStrings:NS_CIS_Connection in appsettings.json or environment.");
        }

        [HttpGet("logs")]
        public async Task<ActionResult<PagedResult<LogEntry>>> GetLogs(
            [FromQuery] string? serviceId = null,
            [FromQuery] string? level = null,
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            try
            {
                var logs = new List<LogEntry>();
                var totalCount = 0;

                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var whereClause = "WHERE 1=1";
                var parameters = new List<NpgsqlParameter>();

                if (!string.IsNullOrEmpty(level) && level != "All")
                {
                    whereClause += " AND level = @level";
                    parameters.Add(new NpgsqlParameter("@level", level));
                }

                if (!string.IsNullOrEmpty(search))
                {
                    whereClause += " AND (message ILIKE @search OR exception ILIKE @search OR logger ILIKE @search)";
                    parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
                }

                if (fromDate.HasValue)
                {
                    whereClause += " AND timestamp >= @fromDate";
                    parameters.Add(new NpgsqlParameter("@fromDate", DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc)));
                }

                if (toDate.HasValue)
                {
                    whereClause += " AND timestamp <= @toDate";
                    parameters.Add(new NpgsqlParameter("@toDate", DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc)));
                }

                var countQuery = $"SELECT COUNT(*) FROM applicationlogs {whereClause}";
                using (var countCommand = new NpgsqlCommand(countQuery, connection))
                {
                    foreach (var p in parameters) countCommand.Parameters.Add(p.Clone());
                    var countResult = await countCommand.ExecuteScalarAsync();
                    totalCount = countResult != null ? Convert.ToInt32(countResult) : 0;
                }

                var offset = (page - 1) * pageSize;
                var query = $@"
                    SELECT id, timestamp, level, logger, message, exception, properties
                    FROM applicationlogs
                    {whereClause}
                    ORDER BY timestamp DESC
                    LIMIT {pageSize} OFFSET {offset}";

                using var command = new NpgsqlCommand(query, connection);
                foreach (var p in parameters) command.Parameters.Add(p.Clone());

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    logs.Add(new LogEntry
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                        Level = reader.IsDBNull(reader.GetOrdinal("level")) ? "Unknown" : reader.GetString(reader.GetOrdinal("level")),
                        ServiceId = reader.IsDBNull(reader.GetOrdinal("logger")) ? null : reader.GetString(reader.GetOrdinal("logger")),
                        Message = reader.IsDBNull(reader.GetOrdinal("message")) ? "" : reader.GetString(reader.GetOrdinal("message")),
                        Exception = reader.IsDBNull(reader.GetOrdinal("exception")) ? null : reader.GetString(reader.GetOrdinal("exception")),
                        Properties = reader.IsDBNull(reader.GetOrdinal("properties")) ? null : reader.GetString(reader.GetOrdinal("properties"))
                    });
                }

                return Ok(new PagedResult<LogEntry>
                {
                    Data = logs,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving logs");
                return StatusCode(500, new { error = "Failed to retrieve logs", detail = ex.Message });
            }
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<LogStatistics>> GetLogStatistics([FromQuery] int hoursBack = 24)
        {
            try
            {
                var statistics = new LogStatistics();

                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT COALESCE(logger, 'Unknown') AS serviceid, level,
                           COUNT(*) AS logcount,
                           MIN(timestamp) AS firstlog,
                           MAX(timestamp) AS lastlog
                    FROM applicationlogs
                    WHERE timestamp >= NOW() - INTERVAL '1 hour' * @hoursBack
                    GROUP BY logger, level
                    ORDER BY logcount DESC";
                using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@hoursBack", hoursBack);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    statistics.ServiceStats.Add(new ServiceLogStats
                    {
                        ServiceId = reader.GetString(reader.GetOrdinal("serviceid")),
                        Level = reader.IsDBNull(reader.GetOrdinal("level")) ? "Unknown" : reader.GetString(reader.GetOrdinal("level")),
                        LogCount = reader.GetInt32(reader.GetOrdinal("logcount")),
                        FirstLog = reader.GetDateTime(reader.GetOrdinal("firstlog")),
                        LastLog = reader.GetDateTime(reader.GetOrdinal("lastlog"))
                    });
                }

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving log statistics");
                return StatusCode(500, new { error = "Failed to retrieve statistics" });
            }
        }

        [HttpPost("cleanup")]
        public async Task<ActionResult> CleanupOldLogs([FromQuery] int daysToKeep = 30)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new NpgsqlCommand("sp_CleanupOldLogs", connection);
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@DaysToKeep", daysToKeep);

                var result = await command.ExecuteScalarAsync();

                _logger.LogInformation("Log cleanup completed: {Result}", result);
                return Ok(new { message = "Log cleanup completed", result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log cleanup");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("services")]
        public async Task<ActionResult<List<string>>> GetServiceIds()
        {
            try
            {
                var serviceIds = new List<string>();

                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new NpgsqlCommand("SELECT DISTINCT logger FROM applicationlogs WHERE logger IS NOT NULL ORDER BY logger", connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    serviceIds.Add(reader.GetString(0));
                }

                return Ok(serviceIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving service IDs");
                return StatusCode(500, new { error = "Failed to retrieve services" });
            }
        }

        [HttpGet("levels")]
        public async Task<ActionResult<List<string>>> GetLogLevels()
        {
            try
            {
                var levels = new List<string>();
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new NpgsqlCommand("SELECT DISTINCT level FROM applicationlogs WHERE level IS NOT NULL ORDER BY level", connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) levels.Add(reader.GetString(0));
                return Ok(levels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving log levels");
                return StatusCode(500, new { error = "Failed to retrieve levels" });
            }
        }
    }

    public class LogEntry
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string? ServiceId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? Properties { get; set; }
    }

    public class LogStatistics
    {
        public List<ServiceLogStats> ServiceStats { get; set; } = new List<ServiceLogStats>();
    }

    public class ServiceLogStats
    {
        public string ServiceId { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public int LogCount { get; set; }
        public DateTime FirstLog { get; set; }
        public DateTime LastLog { get; set; }
    }
}
