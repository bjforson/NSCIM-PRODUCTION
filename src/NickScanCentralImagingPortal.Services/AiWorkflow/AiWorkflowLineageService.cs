using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiWorkflow
{
    public class AiWorkflowLineageService : IAiWorkflowLineageService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOptions<AiWorkflowOptions> _options;
        private readonly ILogger<AiWorkflowLineageService> _logger;

        public AiWorkflowLineageService(
            ApplicationDbContext db,
            IOptions<AiWorkflowOptions> options,
            ILogger<AiWorkflowLineageService> logger)
        {
            _db = db;
            _options = options;
            _logger = logger;
        }

        public async Task NotifyHumanDecisionAsync(
            string containerNumber,
            string scannerType,
            string? normalizedDecision,
            string? normalizedGroupIdentifierForStorage,
            string reviewedBy,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Value.Enabled)
                return;

            var pending = await _db.AiImageAnalysisSuggestions
                .AsTracking()
                .Where(s => s.ContainerNumber == containerNumber
                            && s.ScannerType == scannerType
                            && s.ResolvedAtUtc == null)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
                return;

            foreach (var row in pending)
            {
                row.HumanFinalDecision = normalizedDecision;
                row.HumanReviewedBy = string.IsNullOrWhiteSpace(reviewedBy) ? "System" : reviewedBy.Trim();
                row.ResolvedAtUtc = System.DateTime.UtcNow;
                if (!string.IsNullOrEmpty(normalizedGroupIdentifierForStorage))
                    row.GroupIdentifier ??= normalizedGroupIdentifierForStorage;

                if (!string.IsNullOrEmpty(row.SuggestedDecision) && !string.IsNullOrEmpty(normalizedDecision))
                    row.ResolvedDiffersFromSuggestion = !string.Equals(row.SuggestedDecision, normalizedDecision, System.StringComparison.OrdinalIgnoreCase);
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("AI lineage: resolved {Count} suggestion(s) for {Container} {Scanner}", pending.Count, containerNumber, scannerType);
        }
    }
}
