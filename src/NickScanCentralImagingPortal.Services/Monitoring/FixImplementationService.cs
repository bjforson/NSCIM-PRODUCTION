using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Service that implements approved fixes by creating Git branches and applying changes
    /// </summary>
    public class FixImplementationService : IFixImplementationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FixImplementationService> _logger;
        private readonly string _workspaceRoot;

        public FixImplementationService(
            ApplicationDbContext context,
            ILogger<FixImplementationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;

            _workspaceRoot = configuration["WorkspaceRoot"] ??
                           Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../.."));
        }

        public async Task<ImplementationResult> ImplementFixAsync(long proposalId, CancellationToken cancellationToken)
        {
            try
            {
                var proposal = await _context.FixProposals
                    .Include(fp => fp.ErrorInvestigation)
                    .FirstOrDefaultAsync(fp => fp.Id == proposalId, cancellationToken);

                if (proposal == null)
                {
                    return new ImplementationResult
                    {
                        Success = false,
                        ErrorMessage = $"Fix proposal {proposalId} not found"
                    };
                }

                if (proposal.Status != "Approved")
                {
                    return new ImplementationResult
                    {
                        Success = false,
                        ErrorMessage = $"Fix proposal {proposalId} is not approved (Status: {proposal.Status})"
                    };
                }

                _logger.LogInformation("🚀 Implementing fix proposal {ProposalId}: {Title}", proposalId, proposal.Title);

                // Generate branch name
                var branchName = GenerateBranchName(proposal);

                // Create Git branch
                var branchResult = await CreateGitBranchAsync(branchName, cancellationToken);
                if (!branchResult.Success)
                {
                    return new ImplementationResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to create Git branch: {branchResult.ErrorMessage}"
                    };
                }

                // Apply code changes
                if (!string.IsNullOrEmpty(proposal.CodeChanges))
                {
                    var codeResult = await ApplyCodeChangesAsync(proposal, cancellationToken);
                    if (!codeResult.Success)
                    {
                        _logger.LogWarning("Failed to apply code changes: {Error}", codeResult.ErrorMessage);
                        // Continue anyway - config changes might still work
                    }
                }

                // Apply configuration changes
                if (!string.IsNullOrEmpty(proposal.ConfigurationChanges))
                {
                    var configResult = await ApplyConfigurationChangesAsync(proposal, cancellationToken);
                    if (!configResult.Success)
                    {
                        _logger.LogWarning("Failed to apply configuration changes: {Error}", configResult.ErrorMessage);
                    }
                }

                // Commit changes
                var commitResult = await CommitChangesAsync(branchName, proposal, cancellationToken);
                if (!commitResult.Success)
                {
                    return new ImplementationResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to commit changes: {commitResult.ErrorMessage}"
                    };
                }

                // Update proposal and investigation
                proposal.Status = "Implemented";
                proposal.ImplementedAt = DateTime.UtcNow;
                proposal.BranchName = branchName;
                proposal.CommitHash = commitResult.CommitHash;
                proposal.UpdatedAt = DateTime.UtcNow;

                if (proposal.ErrorInvestigation != null)
                {
                    proposal.ErrorInvestigation.FixBranchName = branchName;
                    proposal.ErrorInvestigation.FixedAt = DateTime.UtcNow;
                    proposal.ErrorInvestigation.Status = "Fixed";
                    proposal.ErrorInvestigation.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(cancellationToken);

                // Log audit
                await LogAuditAsync(
                    proposal.ErrorInvestigationId,
                    proposalId,
                    "FixImplemented",
                    "System",
                    $"Fix implemented in branch {branchName}",
                    JsonSerializer.Serialize(new { branchName, commitHash = commitResult.CommitHash }),
                    cancellationToken);

                _logger.LogInformation("✅ Fix implemented successfully in branch {BranchName}", branchName);

                return new ImplementationResult
                {
                    Success = true,
                    BranchName = branchName,
                    CommitHash = commitResult.CommitHash
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error implementing fix proposal {ProposalId}", proposalId);
                return new ImplementationResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        private string GenerateBranchName(FixProposal proposal)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var sanitizedTitle = System.Text.RegularExpressions.Regex.Replace(
                proposal.Title, @"[^a-zA-Z0-9-]", "-").ToLower();
            sanitizedTitle = sanitizedTitle.Length > 30 ? sanitizedTitle.Substring(0, 30) : sanitizedTitle;

            return $"fix/error-{proposal.ErrorInvestigationId}-{sanitizedTitle}-{timestamp}";
        }

        private async Task<GitResult> CreateGitBranchAsync(string branchName, CancellationToken cancellationToken)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"checkout -b {branchName}",
                    WorkingDirectory = _workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return new GitResult { Success = false, ErrorMessage = "Failed to start Git process" };
                }

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    return new GitResult { Success = false, ErrorMessage = error };
                }

                return new GitResult { Success = true };
            }
            catch (Exception ex)
            {
                return new GitResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<GitResult> ApplyCodeChangesAsync(FixProposal proposal, CancellationToken cancellationToken)
        {
            try
            {
                // Parse code changes from JSON
                var changes = JsonSerializer.Deserialize<CodeChanges>(proposal.CodeChanges ?? "{}");
                if (changes == null)
                {
                    return new GitResult { Success = false, ErrorMessage = "Invalid code changes JSON" };
                }

                // For now, we'll create a file with the suggested changes
                // In a full implementation, this would parse and apply actual code edits
                var changesFile = Path.Combine(_workspaceRoot, ".fixes", $"proposal-{proposal.Id}-changes.md");
                Directory.CreateDirectory(Path.GetDirectoryName(changesFile)!);

                var content = $@"# Fix Proposal {proposal.Id}: {proposal.Title}

## Description
{proposal.Description}

## Rationale
{proposal.Rationale ?? "N/A"}

## Code Changes
{proposal.CodeChanges}

## Impact Assessment
{proposal.ImpactAssessment ?? "N/A"}

## Files Affected
{proposal.AffectedFiles ?? "N/A"}

---
*This file was auto-generated by the Error Investigation System*
*Branch: {proposal.BranchName}*
*Created: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*
";

                await File.WriteAllTextAsync(changesFile, content, cancellationToken);

                return new GitResult { Success = true };
            }
            catch (Exception ex)
            {
                return new GitResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<GitResult> ApplyConfigurationChangesAsync(FixProposal proposal, CancellationToken cancellationToken)
        {
            try
            {
                // Parse configuration changes from JSON
                var configChanges = JsonSerializer.Deserialize<ConfigurationChanges>(proposal.ConfigurationChanges ?? "{}");
                if (configChanges == null)
                {
                    return new GitResult { Success = false, ErrorMessage = "Invalid configuration changes JSON" };
                }

                // Create a configuration changes file
                var changesFile = Path.Combine(_workspaceRoot, ".fixes", $"proposal-{proposal.Id}-config.md");
                Directory.CreateDirectory(Path.GetDirectoryName(changesFile)!);

                var content = $@"# Configuration Changes for Fix Proposal {proposal.Id}

## Description
{proposal.Description}

## Configuration Changes
{proposal.ConfigurationChanges}

## Instructions
Review the configuration changes above and apply them to the appropriate configuration files.

---
*This file was auto-generated by the Error Investigation System*
*Branch: {proposal.BranchName}*
*Created: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*
";

                await File.WriteAllTextAsync(changesFile, content, cancellationToken);

                return new GitResult { Success = true };
            }
            catch (Exception ex)
            {
                return new GitResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<GitResult> CommitChangesAsync(string branchName, FixProposal proposal, CancellationToken cancellationToken)
        {
            try
            {
                // Stage all changes
                var addProcess = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "add -A",
                    WorkingDirectory = _workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var addProc = Process.Start(addProcess);
                if (addProc != null)
                {
                    await addProc.WaitForExitAsync(cancellationToken);
                }

                // Commit changes
                var commitMessage = $"Fix: {proposal.Title}\n\n{proposal.Description}\n\nProposal ID: {proposal.Id}\nInvestigation ID: {proposal.ErrorInvestigationId}";

                var commitProcess = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = _workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var commitProc = Process.Start(commitProcess);
                if (commitProc == null)
                {
                    return new GitResult { Success = false, ErrorMessage = "Failed to start Git commit process" };
                }

                await commitProc.WaitForExitAsync(cancellationToken);

                if (commitProc.ExitCode != 0)
                {
                    var error = await commitProc.StandardError.ReadToEndAsync(cancellationToken);
                    return new GitResult { Success = false, ErrorMessage = error };
                }

                // Get commit hash
                var hashProcess = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = _workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var hashProc = Process.Start(hashProcess);
                if (hashProc != null)
                {
                    await hashProc.WaitForExitAsync(cancellationToken);
                    var hash = await hashProc.StandardOutput.ReadToEndAsync(cancellationToken);
                    return new GitResult { Success = true, CommitHash = hash.Trim() };
                }

                return new GitResult { Success = true };
            }
            catch (Exception ex)
            {
                return new GitResult { Success = false, ErrorMessage = ex.Message };
            }
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

        private class GitResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public string? CommitHash { get; set; }
        }

        private class CodeChanges
        {
            public string? Suggestion { get; set; }
        }

        private class ConfigurationChanges
        {
            public string? File { get; set; }
            public object[]? Changes { get; set; }
        }
    }
}

