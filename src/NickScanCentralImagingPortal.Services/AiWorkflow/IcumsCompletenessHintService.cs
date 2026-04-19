using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiWorkflow
{
    /// <summary>
    /// Phase 3: grounded hints from ContainerCompletenessStatus — no LLM, no clearance decisions.
    /// </summary>
    public class IcumsCompletenessHintService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOptions<AiWorkflowOptions> _options;

        public IcumsCompletenessHintService(ApplicationDbContext db, IOptions<AiWorkflowOptions> options)
        {
            _db = db;
            _options = options;
        }

        public async Task<CompletenessHintDto?> GetHintsAsync(string containerNumber, CancellationToken cancellationToken = default)
        {
            if (!_options.Value.Enabled || !_options.Value.IcumsHintsEnabled)
                return null;

            var row = await _db.ContainerCompletenessStatuses
                .AsNoTracking()
                .Where(c => c.ContainerNumber == containerNumber)
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (row == null)
                return new CompletenessHintDto { ContainerNumber = containerNumber, Found = false, Messages = new[] { "No completeness row for this container." } };

            var msgs = new List<string>();
            if (!row.HasICUMSData)
                msgs.Add("ICUMS data not linked — verify BOE download and document id.");
            if (row.ICUMSDataCompleteness < 100)
                msgs.Add($"ICUMS data completeness is {row.ICUMSDataCompleteness}% — review missing BOE/manifest fields.");
            if (row.ImageDataCompleteness < 100)
                msgs.Add($"Image completeness is {row.ImageDataCompleteness}% — confirm scanner images ingested.");
            if (row.ScannerDataCompleteness < 100)
                msgs.Add($"Scanner data completeness is {row.ScannerDataCompleteness}% — verify scanner pipeline.");
            if (!string.IsNullOrEmpty(row.ErrorMessage))
                msgs.Add($"Last error: {row.ErrorMessage}");

            if (msgs.Count == 0)
                msgs.Add("No obvious gaps from completeness scores — review workflow stage and business rules.");

            return new CompletenessHintDto
            {
                Found = true,
                ContainerNumber = containerNumber,
                ScannerType = row.ScannerType,
                GroupIdentifier = row.GroupIdentifier,
                WorkflowStage = row.WorkflowStage,
                OverallCompleteness = row.OverallCompleteness,
                Messages = msgs.ToArray()
            };
        }
    }

    public sealed class CompletenessHintDto
    {
        public bool Found { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? ScannerType { get; set; }
        public string? GroupIdentifier { get; set; }
        public string? WorkflowStage { get; set; }
        public int OverallCompleteness { get; set; }
        public IReadOnlyList<string> Messages { get; set; } = System.Array.Empty<string>();
    }
}
