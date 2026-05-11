using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseAdminController : ControllerBase
    {
        private const int DefaultQueryTimeoutSeconds = 30;
        private const int MaxQueryTimeoutSeconds = 300;

        private readonly ILogger<DatabaseAdminController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public DatabaseAdminController(
            ILogger<DatabaseAdminController> logger,
            ApplicationDbContext context,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _environment = environment;
        }

        [HttpGet("connections")]
        public ActionResult<List<DatabaseConnection>> GetConnections()
        {
            try
            {
                var connections = new List<DatabaseConnection>
                {
                    new()
                    {
                        Name = "Main Database",
                        Type = "SQL Server",
                        Server = GetServerFromConnectionString(_configuration.GetConnectionString("DefaultConnection")),
                        Database = GetDatabaseFromConnectionString(_configuration.GetConnectionString("DefaultConnection")),
                        Status = "Connected",
                        LastChecked = DateTime.Now
                    },
                    new()
                    {
                        Name = "ASE Database",
                        Type = "SQL Server",
                        Server = GetServerFromConnectionString(_configuration.GetConnectionString("AseConnection")),
                        Database = GetDatabaseFromConnectionString(_configuration.GetConnectionString("AseConnection")),
                        Status = "Connected",
                        LastChecked = DateTime.Now
                    }
                };

                return Ok(connections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DB-ADMIN] Error getting connections");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("query")]
        public async Task<ActionResult<QueryResult>> ExecuteQuery([FromBody] QueryRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return BadRequest(new { error = "Query cannot be empty" });
                }

                if (IsFreeFormSqlExecutionDisabled())
                {
                    _logger.LogWarning("[DB-ADMIN] Free-form SQL execution blocked in production for user {User}",
                        User.Identity?.Name ?? "Unknown");
                    return StatusCode(403, new
                    {
                        error = "Free-form database admin SQL execution is disabled in production. Set Admin:Database:AllowFreeFormSqlExecution=true to enable it."
                    });
                }

                // Security: Only allow SELECT statements for safety
                var trimmedQuery = request.Query.Trim().ToUpper();
                if (!trimmedQuery.StartsWith("SELECT"))
                {
                    return BadRequest(new { error = "Only SELECT queries are allowed for safety" });
                }

                var connectionString = request.ConnectionName == "ASE"
                    ? _configuration.GetConnectionString("AseConnection")
                    : _configuration.GetConnectionString("DefaultConnection");

                var result = new QueryResult
                {
                    Columns = new List<string>(),
                    Rows = new List<Dictionary<string, object>>(),
                    ExecutionTimeMs = 0,
                    RowCount = 0
                };

                var startTime = DateTime.Now;
                var queryTimeoutSeconds = GetDatabaseQueryTimeoutSeconds();

                using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new NpgsqlCommand(request.Query, connection))
                    {
                        command.CommandTimeout = queryTimeoutSeconds;

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            // Get column names
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                result.Columns.Add(reader.GetName(i));
                            }

                            // Get rows (limit to 1000 for safety)
                            int rowCount = 0;
                            while (await reader.ReadAsync() && rowCount < 1000)
                            {
                                var row = new Dictionary<string, object>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[reader.GetName(i)] = value ?? "NULL";
                                }
                                result.Rows.Add(row);
                                rowCount++;
                            }

                            result.RowCount = rowCount;
                        }
                    }
                }

                result.ExecutionTimeMs = (DateTime.Now - startTime).TotalMilliseconds;

                _logger.LogInformation("[DB-ADMIN] Query executed successfully. Rows: {RowCount}, Time: {Time}ms",
                    result.RowCount, result.ExecutionTimeMs);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DB-ADMIN] Error executing query");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("tables")]
        public async Task<ActionResult<List<TableInfo>>> GetTables([FromQuery] string connection = "Main")
        {
            try
            {
                var connectionString = connection == "ASE"
                    ? _configuration.GetConnectionString("AseConnection")
                    : _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    _logger.LogWarning("[DB-ADMIN] Connection string is null for connection: {Connection}", connection);
                    return Ok(new List<TableInfo>()); // Return empty list instead of error
                }

                var tables = new List<TableInfo>();

                var query = @"
                    SELECT 
                        t.TABLE_SCHEMA,
                        t.TABLE_NAME,
                        (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA) as ColumnCount
                    FROM INFORMATION_SCHEMA.TABLES t
                    WHERE t.TABLE_TYPE = 'BASE TABLE'
                    ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME";

                using (var conn = new NpgsqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.CommandTimeout = GetDatabaseQueryTimeoutSeconds();

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(new TableInfo
                                {
                                    Schema = reader.GetString(0),
                                    Name = reader.GetString(1),
                                    ColumnCount = reader.GetInt32(2)
                                });
                            }
                        }
                    }
                }

                _logger.LogInformation("[DB-ADMIN] Retrieved {Count} tables from {Connection}", tables.Count, connection);
                return Ok(tables);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DB-ADMIN] Error getting tables for connection: {Connection}", connection);
                // Return empty list with error logged instead of 500 error
                return Ok(new List<TableInfo>());
            }
        }

        [HttpGet("table/{schema}/{tableName}/columns")]
        public async Task<ActionResult<List<ColumnInfo>>> GetTableColumns(string schema, string tableName, [FromQuery] string connection = "Main")
        {
            try
            {
                var connectionString = connection == "ASE"
                    ? _configuration.GetConnectionString("AseConnection")
                    : _configuration.GetConnectionString("DefaultConnection");

                var columns = new List<ColumnInfo>();

                var query = @"
                    SELECT 
                        COLUMN_NAME,
                        DATA_TYPE,
                        CHARACTER_MAXIMUM_LENGTH,
                        IS_NULLABLE,
                        COLUMN_DEFAULT
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName
                    ORDER BY ORDINAL_POSITION";

                using (var conn = new NpgsqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.CommandTimeout = GetDatabaseQueryTimeoutSeconds();
                        cmd.Parameters.AddWithValue("@Schema", schema);
                        cmd.Parameters.AddWithValue("@TableName", tableName);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                columns.Add(new ColumnInfo
                                {
                                    Name = reader.GetString(0),
                                    DataType = reader.GetString(1),
                                    MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                                    IsNullable = reader.GetString(3) == "YES",
                                    DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4)
                                });
                            }
                        }
                    }
                }

                return Ok(columns);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DB-ADMIN] Error getting table columns");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("test-connection")]
        public async Task<ActionResult<ConnectionTestResult>> TestConnection([FromBody] ConnectionTestRequest request)
        {
            try
            {
                var result = new ConnectionTestResult
                {
                    Success = false,
                    Message = "",
                    ResponseTimeMs = 0
                };

                var startTime = DateTime.Now;

                using (var connection = new NpgsqlConnection(request.ConnectionString))
                {
                    await connection.OpenAsync();
                    result.Success = true;
                    result.Message = "Connection successful";
                    result.ServerVersion = connection.ServerVersion;
                    result.Database = connection.Database;
                }

                result.ResponseTimeMs = (DateTime.Now - startTime).TotalMilliseconds;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Ok(new ConnectionTestResult
                {
                    Success = false,
                    Message = ex.Message,
                    ResponseTimeMs = 0
                });
            }
        }

        private string GetServerFromConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "Unknown";
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                return builder.Host;
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetDatabaseFromConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "Unknown";
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                return builder.Database;
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool IsFreeFormSqlExecutionDisabled()
        {
            return _environment.IsProduction()
                && !_configuration.GetValue<bool>("Admin:Database:AllowFreeFormSqlExecution", false);
        }

        private int GetDatabaseQueryTimeoutSeconds()
        {
            var configuredTimeout = _configuration.GetValue<int?>("Admin:Database:QueryTimeoutSeconds")
                ?? _configuration.GetValue<int?>("Database:CommandTimeoutSeconds")
                ?? DefaultQueryTimeoutSeconds;

            if (configuredTimeout <= 0)
            {
                return DefaultQueryTimeoutSeconds;
            }

            return Math.Min(configuredTimeout, MaxQueryTimeoutSeconds);
        }
    }

    public class DatabaseConnection
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; }
    }

    public class QueryRequest
    {
        public string Query { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = "Main";
    }

    public class QueryResult
    {
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public int RowCount { get; set; }
        public double ExecutionTimeMs { get; set; }
    }

    public class TableInfo
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ColumnCount { get; set; }
        public long? RowCount { get; set; }
    }

    public class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public string? DefaultValue { get; set; }
    }

    public class ConnectionTestRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    public class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public double ResponseTimeMs { get; set; }
        public string? ServerVersion { get; set; }
        public string? Database { get; set; }
    }
}

