using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Service that performs deep investigation of errors and generates fix proposals
    /// Uses AI-powered analysis to understand root causes and propose solutions
    /// </summary>
    public class ErrorInvestigationService : IErrorInvestigationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ErrorInvestigationService> _logger;
        private readonly string _workspaceRoot;

        public ErrorInvestigationService(
            ApplicationDbContext context,
            ILogger<ErrorInvestigationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;

            // Get workspace root from configuration or use default
            _workspaceRoot = configuration["WorkspaceRoot"] ??
                           Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../.."));
        }

        public async Task ProcessErrorGroupAsync(ErrorGroupDto errorGroup, CancellationToken cancellationToken)
        {
            try
            {
                if (errorGroup == null || errorGroup.Errors.Count == 0) return;

                var pattern = errorGroup.ErrorPattern ?? "";
                var errorCode = TruncateForColumn(errorGroup.ErrorCode, 50);
                var serviceId = TruncateForColumn(errorGroup.ServiceId, 200);
                var operation = TruncateForColumn(errorGroup.Operation, 200);
                var exceptionType = TruncateForColumn(errorGroup.ExceptionType, 200);
                var errors = errorGroup.Errors;

                // Generate unique group ID
                var groupId = GenerateGroupId(pattern, errorCode, serviceId);

                // Check if investigation already exists
                var existingInvestigation = await _context.ErrorInvestigations
                    .AsTracking()
                    .FirstOrDefaultAsync(ei => ei.InvestigationGroupId == groupId, cancellationToken);

                if (existingInvestigation != null)
                {
                    // Update existing investigation
                    existingInvestigation.OccurrenceCount += errors.Count;
                    existingInvestigation.LastSeen = DateTime.UtcNow;

                    // Update sample error if this one is more recent
                    var latestError = errors.OrderByDescending(e => e.Timestamp).FirstOrDefault();
                    if (latestError != null)
                    {
                        existingInvestigation.SampleErrorMessage = latestError.Message;
                        existingInvestigation.SampleStackTrace = latestError.Exception;
                    }

                    // Update related log IDs
                    var logIds = new List<string>();
                    if (!string.IsNullOrEmpty(existingInvestigation.RelatedLogIds))
                    {
                        logIds.AddRange(existingInvestigation.RelatedLogIds.Split(','));
                    }
                    foreach (var error in errors)
                    {
                        var id = error.Id.ToString();
                        if (!string.IsNullOrEmpty(id) && !logIds.Contains(id))
                        {
                            logIds.Add(id);
                        }
                    }
                    existingInvestigation.RelatedLogIds = string.Join(",", logIds);
                    existingInvestigation.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Updated investigation {GroupId} with {Count} new occurrences", groupId, errors.Count);

                    if (existingInvestigation.Status == "New")
                    {
                        await InvestigateErrorAsync(existingInvestigation.Id, cancellationToken);
                    }
                }
                else
                {
                    // Create new investigation
                    var latestError = errors.OrderByDescending(e => e.Timestamp).FirstOrDefault();
                    var firstError = errors.OrderBy(e => e.Timestamp).FirstOrDefault();

                    var investigation = new ErrorInvestigation
                    {
                        InvestigationGroupId = groupId,
                        ErrorPattern = pattern,
                        ErrorCode = errorCode,
                        ServiceId = serviceId,
                        Operation = operation,
                        ExceptionType = exceptionType,
                        OccurrenceCount = errors.Count,
                        FirstSeen = firstError?.Timestamp ?? DateTime.UtcNow,
                        LastSeen = latestError?.Timestamp ?? DateTime.UtcNow,
                        Status = "New",
                        Priority = DeterminePriority(exceptionType, errorCode, errors.Count),
                        SampleErrorMessage = latestError?.Message,
                        SampleStackTrace = latestError?.Exception,
                        RelatedLogIds = string.Join(",", errors.Select(e => e.Id.ToString())),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.ErrorInvestigations.Add(investigation);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("Created new investigation {GroupId} for {Count} errors", groupId, errors.Count);

                    // Log audit
                    await LogAuditAsync(investigation.Id, null, "InvestigationCreated", "System",
                        $"Created investigation for error pattern: {pattern}", null, cancellationToken);

                    await InvestigateErrorAsync(investigation.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing error group");
            }
        }

        public async Task InvestigateErrorAsync(long investigationId, CancellationToken cancellationToken)
        {
            try
            {
                var investigation = await _context.ErrorInvestigations
                    .AsTracking()
                    .FirstOrDefaultAsync(ei => ei.Id == investigationId, cancellationToken);

                if (investigation == null)
                {
                    _logger.LogWarning("Investigation {Id} not found", investigationId);
                    return;
                }

                if (investigation.Status != "New" && investigation.Status != "Investigating")
                {
                    _logger.LogDebug("Investigation {Id} already processed (Status: {Status})", investigationId, investigation.Status);
                    return;
                }

                investigation.Status = "Investigating";
                investigation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("🔍 Starting deep investigation for error {Id}: {Pattern}", investigationId, investigation.ErrorPattern);

                // Perform deep investigation
                var investigationResult = await PerformDeepInvestigationAsync(investigation, cancellationToken);

                // Update investigation with findings
                investigation.InvestigationSummary = investigationResult.Summary;
                investigation.InvestigationDetails = JsonSerializer.Serialize(investigationResult.Details);
                investigation.Status = "Proposed"; // Ready for fix proposal generation
                investigation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                await LogAuditAsync(investigationId, null, "InvestigationCompleted", "System",
                    $"Investigation completed. Summary: {investigationResult.Summary?.Substring(0, Math.Min(100, investigationResult.Summary?.Length ?? 0))}",
                    null, cancellationToken);

                await GenerateFixProposalsAsync(investigationId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error investigating error {Id}", investigationId);

                try
                {
                    var investigation = await _context.ErrorInvestigations
                        .AsTracking()
                        .FirstOrDefaultAsync(ei => ei.Id == investigationId, cancellationToken);
                    if (investigation != null)
                    {
                        investigation.Status = "New";
                        investigation.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogWarning("Could not reset investigation {Id} status — DbContext already disposed", investigationId);
                }
            }
        }

        public async Task GenerateFixProposalsAsync(long investigationId, CancellationToken cancellationToken)
        {
            try
            {
                var investigation = await _context.ErrorInvestigations
                    .AsTracking()
                    .Include(ei => ei.FixProposals)
                    .FirstOrDefaultAsync(ei => ei.Id == investigationId, cancellationToken);

                if (investigation == null || investigation.Status != "Proposed")
                {
                    return;
                }

                _logger.LogInformation("💡 Generating fix proposals for investigation {Id}", investigationId);

                // Generate proposals based on investigation
                var proposals = await GenerateProposalsAsync(investigation, cancellationToken);

                foreach (var proposal in proposals)
                {
                    proposal.ErrorInvestigationId = investigationId;
                    proposal.Status = "Proposed";
                    proposal.CreatedAt = DateTime.UtcNow;
                    proposal.UpdatedAt = DateTime.UtcNow;

                    _context.FixProposals.Add(proposal);
                }

                investigation.HasProposedFix = proposals.Count > 0;
                investigation.Status = "Proposed";
                investigation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                await LogAuditAsync(investigationId, null, "FixProposed", "System",
                    $"Generated {proposals.Count} fix proposal(s)", null, cancellationToken);

                _logger.LogInformation("✅ Generated {Count} fix proposal(s) for investigation {Id}", proposals.Count, investigationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating fix proposals for investigation {Id}", investigationId);
            }
        }

        private async Task<InvestigationResult> PerformDeepInvestigationAsync(ErrorInvestigation investigation, CancellationToken cancellationToken)
        {
            var result = new InvestigationResult
            {
                Summary = "",
                Details = new Dictionary<string, object>()
            };

            try
            {
                var findings = new List<string>();
                var relatedFiles = new List<string>();
                var similarErrors = new List<object>();
                var configIssues = new List<string>();

                // 1. Analyze exception type and message
                if (!string.IsNullOrEmpty(investigation.ExceptionType))
                {
                    findings.Add($"Exception Type: {investigation.ExceptionType}");

                    // Common exception analysis
                    if (investigation.ExceptionType.Contains("NullReference"))
                    {
                        findings.Add("Root Cause: Null reference exception suggests missing null checks or uninitialized objects");
                        relatedFiles.AddRange(await FindRelatedCodeFilesAsync(investigation.ServiceId, investigation.Operation, cancellationToken));
                    }
                    else if (investigation.ExceptionType.Contains("DbUpdate") || investigation.ExceptionType.Contains("SqlException"))
                    {
                        findings.Add("Root Cause: Database operation failure - check connection strings, constraints, or data integrity");
                        configIssues.Add("Database connection configuration");
                    }
                    else if (investigation.ExceptionType.Contains("Timeout"))
                    {
                        findings.Add("Root Cause: Operation timeout - may need to increase timeout values or optimize query");
                        configIssues.Add("Timeout configuration settings");
                    }
                    else if (investigation.ExceptionType.Contains("HttpRequest") || investigation.ExceptionType.Contains("HttpClient"))
                    {
                        findings.Add("Root Cause: External API call failure - check API endpoints, authentication, or network connectivity");
                        configIssues.Add("External API configuration");
                    }
                }

                // 2. Analyze error message for patterns
                if (!string.IsNullOrEmpty(investigation.SampleErrorMessage))
                {
                    var messageAnalysis = AnalyzeErrorMessage(investigation.SampleErrorMessage);
                    findings.AddRange(messageAnalysis);
                }

                // 3. Find related code files
                if (!string.IsNullOrEmpty(investigation.ServiceId) || !string.IsNullOrEmpty(investigation.Operation))
                {
                    var files = await FindRelatedCodeFilesAsync(investigation.ServiceId, investigation.Operation, cancellationToken);
                    relatedFiles.AddRange(files);
                }

                // 4. Check for similar past errors
                var similar = await FindSimilarPastErrorsAsync(investigation, cancellationToken);
                similarErrors.AddRange(similar);

                // 5. Check configuration
                var configAnalysis = await AnalyzeConfigurationAsync(investigation, cancellationToken);
                configIssues.AddRange(configAnalysis);

                // Compile summary
                result.Summary = CompileInvestigationSummary(findings, relatedFiles, similarErrors, configIssues);

                result.Details["Findings"] = findings;
                result.Details["RelatedFiles"] = relatedFiles;
                result.Details["SimilarPastErrors"] = similarErrors;
                result.Details["ConfigurationIssues"] = configIssues;
                result.Details["ExceptionType"] = investigation.ExceptionType;
                result.Details["ServiceId"] = investigation.ServiceId;
                result.Details["Operation"] = investigation.Operation;
                result.Details["ErrorCode"] = investigation.ErrorCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing deep investigation");
                result.Summary = "Investigation encountered an error. Manual review recommended.";
                result.Details["Error"] = ex.Message;
            }

            return result;
        }

        private Task<List<string>> FindRelatedCodeFilesAsync(string? serviceId, string? operation, CancellationToken cancellationToken)
        {
            var files = new List<string>();

            try
            {
                if (string.IsNullOrEmpty(serviceId) && string.IsNullOrEmpty(operation))
                    return Task.FromResult(files);

                // Search for files that might be related
                var searchTerms = new List<string>();
                if (!string.IsNullOrEmpty(serviceId))
                {
                    searchTerms.Add(serviceId.Replace("BackgroundService", "").Replace("Service", ""));
                }
                if (!string.IsNullOrEmpty(operation))
                {
                    searchTerms.Add(operation);
                }

                // Search in source directories
                var sourceDirs = new[]
                {
                    Path.Combine(_workspaceRoot, "src"),
                    Path.Combine(_workspaceRoot, "src", "NickScanCentralImagingPortal.Services"),
                    Path.Combine(_workspaceRoot, "src", "NickScanCentralImagingPortal.API")
                };

                foreach (var dir in sourceDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var term in searchTerms)
                    {
                        if (string.IsNullOrEmpty(term)) continue;

                        var matchingFiles = Directory.GetFiles(dir, $"*{term}*.cs", SearchOption.AllDirectories)
                            .Take(10) // Limit results
                            .Select(f => Path.GetRelativePath(_workspaceRoot, f))
                            .ToList();

                        files.AddRange(matchingFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding related code files");
            }

            return Task.FromResult(files.Distinct().ToList());
        }

        private List<string> AnalyzeErrorMessage(string message)
        {
            var findings = new List<string>();

            if (message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Error suggests connection issue - check network, database, or API connectivity");
            }
            if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Timeout detected - operation may be taking too long or timeout values too low");
            }
            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Resource not found - check if file, record, or endpoint exists");
            }
            if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) || message.Contains("access denied", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Permission/access issue - check authorization and file system permissions");
            }
            if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Duplicate entry detected - check unique constraints and data validation");
            }

            return findings;
        }

        private async Task<List<object>> FindSimilarPastErrorsAsync(ErrorInvestigation investigation, CancellationToken cancellationToken)
        {
            var similar = new List<object>();

            try
            {
                // Find investigations with similar patterns
                var similarInvestigations = await _context.ErrorInvestigations
                    .Where(ei => ei.Id != investigation.Id
                        && (ei.ExceptionType == investigation.ExceptionType
                            || ei.ErrorCode == investigation.ErrorCode
                            || ei.ServiceId == investigation.ServiceId))
                    .OrderByDescending(ei => ei.LastSeen)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                foreach (var sim in similarInvestigations)
                {
                    similar.Add(new
                    {
                        Id = sim.Id,
                        Pattern = sim.ErrorPattern,
                        Status = sim.Status,
                        LastSeen = sim.LastSeen,
                        OccurrenceCount = sim.OccurrenceCount
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding similar past errors");
            }

            return similar;
        }

        private Task<List<string>> AnalyzeConfigurationAsync(ErrorInvestigation investigation, CancellationToken cancellationToken)
        {
            var issues = new List<string>();

            try
            {
                // Check for common configuration issues based on error type
                if (investigation.ExceptionType?.Contains("Timeout") == true)
                {
                    issues.Add("Check timeout settings in appsettings.json");
                }
                if (investigation.ExceptionType?.Contains("DbUpdate") == true || investigation.ExceptionType?.Contains("SqlException") == true)
                {
                    issues.Add("Verify database connection string configuration");
                    issues.Add("Check database constraints and schema");
                }
                if (investigation.ExceptionType?.Contains("HttpRequest") == true)
                {
                    issues.Add("Verify external API endpoint URLs and authentication");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analyzing configuration");
            }

            return Task.FromResult(issues);
        }

        private string CompileInvestigationSummary(List<string> findings, List<string> relatedFiles, List<object> similarErrors, List<string> configIssues)
        {
            var summary = new System.Text.StringBuilder();

            summary.AppendLine($"Investigation completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            summary.AppendLine();

            if (findings.Any())
            {
                summary.AppendLine("Key Findings:");
                foreach (var finding in findings)
                {
                    summary.AppendLine($"  • {finding}");
                }
                summary.AppendLine();
            }

            if (relatedFiles.Any())
            {
                summary.AppendLine($"Related Code Files ({relatedFiles.Count}):");
                foreach (var file in relatedFiles.Take(5))
                {
                    summary.AppendLine($"  • {file}");
                }
                if (relatedFiles.Count > 5)
                {
                    summary.AppendLine($"  ... and {relatedFiles.Count - 5} more");
                }
                summary.AppendLine();
            }

            if (similarErrors.Any())
            {
                summary.AppendLine($"Similar Past Errors: {similarErrors.Count} found");
                summary.AppendLine();
            }

            if (configIssues.Any())
            {
                summary.AppendLine("Configuration Checks Needed:");
                foreach (var issue in configIssues)
                {
                    summary.AppendLine($"  • {issue}");
                }
            }

            return summary.ToString();
        }

        private Task<List<FixProposal>> GenerateProposalsAsync(ErrorInvestigation investigation, CancellationToken cancellationToken)
        {
            var proposals = new List<FixProposal>();

            try
            {
                // Generate proposals based on error type and investigation
                if (investigation.ExceptionType?.Contains("NullReference") == true)
                {
                    proposals.Add(CreateNullReferenceFixProposal(investigation));
                }
                else if (investigation.ExceptionType?.Contains("Timeout") == true)
                {
                    proposals.Add(CreateTimeoutFixProposal(investigation));
                }
                else if (investigation.ExceptionType?.Contains("DbUpdate") == true || investigation.ExceptionType?.Contains("SqlException") == true)
                {
                    proposals.Add(CreateDatabaseFixProposal(investigation));
                }
                else if (investigation.ExceptionType?.Contains("HttpRequest") == true)
                {
                    proposals.Add(CreateHttpRequestFixProposal(investigation));
                }
                else
                {
                    // Generic fix proposal
                    proposals.Add(CreateGenericFixProposal(investigation));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating proposals");
            }

            return Task.FromResult(proposals);
        }

        private FixProposal CreateNullReferenceFixProposal(ErrorInvestigation investigation)
        {
            return new FixProposal
            {
                FixType = "CodeChange",
                Title = "Add Null Reference Checks",
                Description = "Add null checks and defensive programming to prevent NullReferenceException",
                Rationale = "The error indicates a null reference access. Adding null checks will prevent this exception.",
                ImpactAssessment = "Low risk - adding null checks improves code safety",
                RiskLevel = "Low",
                CodeChanges = JsonSerializer.Serialize(new
                {
                    suggestion = "Add null checks before accessing object properties/methods",
                    example = "if (obj != null) { obj.DoSomething(); }"
                })
            };
        }

        private FixProposal CreateTimeoutFixProposal(ErrorInvestigation investigation)
        {
            return new FixProposal
            {
                FixType = "Both",
                Title = "Increase Timeout Values",
                Description = "Increase timeout configuration values and add retry logic",
                Rationale = "Operation is timing out, suggesting timeout values are too low or operation needs optimization",
                ImpactAssessment = "Medium risk - increasing timeouts may delay failure detection",
                RiskLevel = "Medium",
                ConfigurationChanges = JsonSerializer.Serialize(new
                {
                    file = "appsettings.json",
                    changes = new object[]
                    {
                        new { section = "ICUMS", key = "TimeoutSeconds", action = "increase", current = "30", suggested = "60" },
                        new { section = "Database", key = "CommandTimeout", action = "increase", current = "30", suggested = "60" }
                    }
                }),
                CodeChanges = JsonSerializer.Serialize(new
                {
                    suggestion = "Add retry logic with exponential backoff for transient failures"
                })
            };
        }

        private FixProposal CreateDatabaseFixProposal(ErrorInvestigation investigation)
        {
            return new FixProposal
            {
                FixType = "Both",
                Title = "Fix Database Operation Error",
                Description = "Review and fix database operation - check constraints, connection, and data integrity",
                Rationale = "Database operation is failing - may be due to constraint violations, connection issues, or data problems",
                ImpactAssessment = "High risk - database errors can affect data integrity",
                RiskLevel = "High",
                CodeChanges = JsonSerializer.Serialize(new
                {
                    suggestion = "Add proper error handling and validation before database operations",
                    checkConstraints = true,
                    validateData = true
                }),
                ConfigurationChanges = JsonSerializer.Serialize(new
                {
                    file = "appsettings.json",
                    changes = new object[]
                    {
                        new { section = "ConnectionStrings", key = "NS_CIS_Connection", action = "verify", note = "Ensure connection string is correct" }
                    }
                })
            };
        }

        private FixProposal CreateHttpRequestFixProposal(ErrorInvestigation investigation)
        {
            return new FixProposal
            {
                FixType = "Both",
                Title = "Fix External API Request Error",
                Description = "Add retry logic, improve error handling, and verify API configuration",
                Rationale = "External API calls are failing - may need retry logic, better error handling, or configuration fixes",
                ImpactAssessment = "Medium risk - external API failures are often transient",
                RiskLevel = "Medium",
                CodeChanges = JsonSerializer.Serialize(new
                {
                    suggestion = "Add HttpClient retry policy with exponential backoff",
                    errorHandling = "Improve error handling for HTTP status codes"
                }),
                ConfigurationChanges = JsonSerializer.Serialize(new
                {
                    file = "appsettings.json",
                    changes = new object[]
                    {
                        new { section = "ICUMS", key = "BaseUrl", action = "verify", note = "Ensure API endpoint is correct" },
                        new { section = "ICUMS", key = "RetryCount", action = "increase", current = "3", suggested = "5" }
                    }
                })
            };
        }

        private FixProposal CreateGenericFixProposal(ErrorInvestigation investigation)
        {
            return new FixProposal
            {
                FixType = "CodeChange",
                Title = "Improve Error Handling",
                Description = "Add comprehensive error handling and logging for this error scenario",
                Rationale = "Generic error suggests need for better error handling and defensive programming",
                ImpactAssessment = "Low risk - improving error handling generally reduces issues",
                RiskLevel = "Low",
                CodeChanges = JsonSerializer.Serialize(new
                {
                    suggestion = "Add try-catch blocks, null checks, and proper error logging",
                    defensiveProgramming = true
                })
            };
        }

        private string GenerateGroupId(string pattern, string? errorCode, string? serviceId)
        {
            var parts = new List<string> { pattern.GetHashCode().ToString("X") };
            if (!string.IsNullOrEmpty(errorCode)) parts.Add(errorCode);
            if (!string.IsNullOrEmpty(serviceId)) parts.Add(serviceId.GetHashCode().ToString("X"));
            var id = string.Join("-", parts);
            return id.Length <= 100 ? id : id[..100];
        }

        private static string? TruncateForColumn(string? value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLen ? value : value[..maxLen];
        }

        private string DeterminePriority(string? exceptionType, string? errorCode, int occurrenceCount)
        {
            // Critical: Database errors, authentication errors, high occurrence
            if (exceptionType?.Contains("DbUpdate") == true ||
                exceptionType?.Contains("SqlException") == true ||
                errorCode?.StartsWith("ERR_2") == true || // Auth errors
                occurrenceCount > 10)
            {
                return "Critical";
            }

            // High: Timeout errors, external API errors, medium occurrence
            if (exceptionType?.Contains("Timeout") == true ||
                exceptionType?.Contains("HttpRequest") == true ||
                occurrenceCount > 5)
            {
                return "High";
            }

            // Medium: Other exceptions, low occurrence
            if (occurrenceCount > 1)
            {
                return "Medium";
            }

            return "Low";
        }

        private async Task LogAuditAsync(long? investigationId, long? proposalId, string actionType, string performedBy,
            string description, string? details, CancellationToken cancellationToken)
        {
            try
            {
                var auditLog = new FixAuditLog
                {
                    ErrorInvestigationId = investigationId,
                    FixProposalId = proposalId,
                    ActionType = actionType,
                    PerformedBy = performedBy,
                    Description = description,
                    Details = details,
                    CreatedAt = DateTime.UtcNow
                };

                _context.FixAuditLogs.Add(auditLog);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error logging audit entry");
            }
        }

        public async Task<ErrorInvestigationDto?> GetInvestigationAsync(long investigationId, CancellationToken cancellationToken)
        {
            var investigation = await _context.ErrorInvestigations
                .Include(ei => ei.FixProposals)
                .FirstOrDefaultAsync(ei => ei.Id == investigationId, cancellationToken);

            if (investigation == null) return null;

            return MapToDto(investigation);
        }

        public async Task<List<ErrorInvestigationDto>> GetInvestigationsAsync(
            string? status = null,
            string? priority = null,
            string? search = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var query = _context.ErrorInvestigations
                .Include(ei => ei.FixProposals)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(ei => ei.Status == status);
            }

            if (!string.IsNullOrEmpty(priority))
            {
                query = query.Where(ei => ei.Priority == priority);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(ei =>
                    (ei.ErrorPattern != null && ei.ErrorPattern.Contains(s)) ||
                    (ei.SampleErrorMessage != null && ei.SampleErrorMessage.Contains(s)) ||
                    (ei.ExceptionType != null && ei.ExceptionType.Contains(s)) ||
                    (ei.ServiceId != null && ei.ServiceId.Contains(s)) ||
                    (ei.Operation != null && ei.Operation.Contains(s)) ||
                    (ei.InvestigationGroupId != null && ei.InvestigationGroupId.Contains(s)));
            }

            var investigations = await query
                .OrderByDescending(ei => ei.LastSeen)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return investigations.Select(MapToDto).ToList();
        }

        private ErrorInvestigationDto MapToDto(ErrorInvestigation investigation)
        {
            return new ErrorInvestigationDto
            {
                Id = investigation.Id,
                InvestigationGroupId = investigation.InvestigationGroupId,
                ErrorPattern = investigation.ErrorPattern,
                ErrorCode = investigation.ErrorCode,
                ServiceId = investigation.ServiceId,
                Operation = investigation.Operation,
                ExceptionType = investigation.ExceptionType,
                OccurrenceCount = investigation.OccurrenceCount,
                FirstSeen = investigation.FirstSeen,
                LastSeen = investigation.LastSeen,
                Status = investigation.Status,
                Priority = investigation.Priority,
                InvestigationSummary = investigation.InvestigationSummary,
                InvestigationDetails = investigation.InvestigationDetails,
                SampleErrorMessage = investigation.SampleErrorMessage,
                SampleStackTrace = investigation.SampleStackTrace,
                HasProposedFix = investigation.HasProposedFix,
                FixProposals = investigation.FixProposals.Select(fp => new FixProposalDto
                {
                    Id = fp.Id,
                    ErrorInvestigationId = fp.ErrorInvestigationId,
                    FixType = fp.FixType,
                    Title = fp.Title,
                    Description = fp.Description,
                    Rationale = fp.Rationale,
                    ImpactAssessment = fp.ImpactAssessment,
                    CodeChanges = fp.CodeChanges,
                    ConfigurationChanges = fp.ConfigurationChanges,
                    AffectedFiles = fp.AffectedFiles,
                    RiskLevel = fp.RiskLevel,
                    Status = fp.Status,
                    CreatedAt = fp.CreatedAt
                }).ToList(),
                CreatedAt = investigation.CreatedAt,
                UpdatedAt = investigation.UpdatedAt
            };
        }

        public async Task<ApprovalResult> ApproveFixProposalAsync(long investigationId, long proposalId, string username, string? notes)
        {
            try
            {
                var proposal = await _context.FixProposals
                    .AsTracking()
                    .Include(fp => fp.ErrorInvestigation)
                    .FirstOrDefaultAsync(fp => fp.Id == proposalId && fp.ErrorInvestigationId == investigationId);

                if (proposal == null)
                {
                    return new ApprovalResult
                    {
                        Success = false,
                        ErrorMessage = "Fix proposal not found"
                    };
                }

                if (proposal.Status != "Proposed")
                {
                    return new ApprovalResult
                    {
                        Success = false,
                        ErrorMessage = $"Proposal is not in Proposed status (Current: {proposal.Status})"
                    };
                }

                proposal.Status = "Approved";
                proposal.ApprovedBy = username;
                proposal.ApprovedAt = DateTime.UtcNow;
                proposal.ApprovalNotes = notes;
                proposal.UpdatedAt = DateTime.UtcNow;

                if (proposal.ErrorInvestigation != null)
                {
                    proposal.ErrorInvestigation.Status = "Approved";
                    proposal.ErrorInvestigation.ApprovedBy = username;
                    proposal.ErrorInvestigation.ApprovedAt = DateTime.UtcNow;
                    proposal.ErrorInvestigation.ApprovalNotes = notes;
                    proposal.ErrorInvestigation.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                await LogAuditAsync(investigationId, proposalId, "FixApproved", username,
                    $"Fix proposal approved by {username}", notes, CancellationToken.None);

                _logger.LogInformation("Fix proposal {ProposalId} approved by {Username}", proposalId, username);

                return new ApprovalResult
                {
                    Success = true,
                    BranchName = null // Will be set when implemented
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving fix proposal {ProposalId}", proposalId);
                return new ApprovalResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<RejectionResult> RejectFixProposalAsync(long investigationId, long proposalId, string username, string reason)
        {
            try
            {
                var proposal = await _context.FixProposals
                    .AsTracking()
                    .Include(fp => fp.ErrorInvestigation)
                    .FirstOrDefaultAsync(fp => fp.Id == proposalId && fp.ErrorInvestigationId == investigationId);

                if (proposal == null)
                {
                    return new RejectionResult
                    {
                        Success = false,
                        ErrorMessage = "Fix proposal not found"
                    };
                }

                proposal.Status = "Rejected";
                proposal.ApprovedBy = username;
                proposal.ApprovedAt = DateTime.UtcNow;
                proposal.ApprovalNotes = $"REJECTED: {reason}";
                proposal.UpdatedAt = DateTime.UtcNow;

                if (proposal.ErrorInvestigation != null)
                {
                    proposal.ErrorInvestigation.Status = "Proposed"; // Back to proposed so new proposals can be generated
                    proposal.ErrorInvestigation.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                await LogAuditAsync(investigationId, proposalId, "FixRejected", username,
                    $"Fix proposal rejected by {username}: {reason}", reason, CancellationToken.None);

                _logger.LogInformation("Fix proposal {ProposalId} rejected by {Username}: {Reason}", proposalId, username, reason);

                return new RejectionResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting fix proposal {ProposalId}", proposalId);
                return new RejectionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<IgnoreResult> IgnoreInvestigationAsync(long investigationId, string username, string? reason)
        {
            try
            {
                var investigation = await _context.ErrorInvestigations
                    .AsTracking()
                    .FirstOrDefaultAsync(ei => ei.Id == investigationId);

                if (investigation == null)
                {
                    return new IgnoreResult
                    {
                        Success = false,
                        ErrorMessage = "Investigation not found"
                    };
                }

                investigation.Status = "Ignored";
                investigation.ApprovedBy = username;
                investigation.ApprovedAt = DateTime.UtcNow;
                investigation.ApprovalNotes = reason ?? "Investigation ignored by user";
                investigation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await LogAuditAsync(investigationId, null, "InvestigationIgnored", username,
                    $"Investigation ignored by {username}", reason, CancellationToken.None);

                _logger.LogInformation("Investigation {Id} ignored by {Username}", investigationId, username);

                return new IgnoreResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ignoring investigation {Id}", investigationId);
                return new IgnoreResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private class InvestigationResult
        {
            public string Summary { get; set; } = string.Empty;
            public Dictionary<string, object> Details { get; set; } = new();
        }
    }
}

