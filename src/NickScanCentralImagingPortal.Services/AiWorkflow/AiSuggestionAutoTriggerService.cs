using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiWorkflow
{
    /// <summary>
    /// Background service that auto-generates AI suggestions for newly assigned analysis groups.
    /// Polls every 30 seconds for groups in "AnalystAssigned" status that don't yet have suggestions.
    /// </summary>
    public class AiSuggestionAutoTriggerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AiSuggestionAutoTriggerService> _logger;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        public AiSuggestionAutoTriggerService(
            IServiceScopeFactory scopeFactory,
            ILogger<AiSuggestionAutoTriggerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AI suggestion auto-trigger service starting");

            // Initial delay to let the application fully start
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessNewAssignmentsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI auto-trigger cycle failed");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }

            _logger.LogInformation("AI suggestion auto-trigger service stopped");
        }

        private async Task ProcessNewAssignmentsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<AiWorkflowOptions>>().Value;

            if (!options.Enabled || !options.ImageAssistEnabled || !options.AutoTriggerEnabled)
                return;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Find groups that are assigned but don't have any AI suggestions yet
            var assignedGroupIds = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == "AnalystAssigned")
                .Select(g => g.Id)
                .ToListAsync(cancellationToken);

            if (assignedGroupIds.Count == 0)
                return;

            // Filter out groups that already have suggestions
            var groupsWithSuggestions = await db.AiImageAnalysisSuggestions
                .AsNoTracking()
                .Where(s => s.AnalysisGroupId != null && assignedGroupIds.Contains(s.AnalysisGroupId.Value))
                .Select(s => s.AnalysisGroupId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var newGroupIds = assignedGroupIds.Except(groupsWithSuggestions).ToList();

            if (newGroupIds.Count == 0)
                return;

            _logger.LogInformation("AI auto-trigger: found {Count} new assigned groups without suggestions", newGroupIds.Count);

            var assistService = scope.ServiceProvider.GetRequiredService<AiImageAssistService>();

            foreach (var groupId in newGroupIds.Take(10)) // Process max 10 per cycle
            {
                try
                {
                    var suggestions = await assistService.GenerateSuggestionsForGroupAsync(groupId, cancellationToken);
                    _logger.LogInformation("AI auto-trigger: generated {Count} suggestions for group {GroupId}",
                        suggestions.Count, groupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI auto-trigger: failed for group {GroupId}", groupId);
                }
            }
        }
    }
}
