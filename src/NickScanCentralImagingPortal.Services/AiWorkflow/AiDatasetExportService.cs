using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiWorkflow
{
    /// <summary>
    /// Phase 2 close: JSONL export of resolved suggestions for offline eval / future training pipelines.
    /// </summary>
    public class AiDatasetExportService : IAiDatasetExportService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOptions<AiWorkflowOptions> _options;
        private readonly ILogger<AiDatasetExportService> _logger;

        public AiDatasetExportService(
            ApplicationDbContext db,
            IOptions<AiWorkflowOptions> options,
            ILogger<AiDatasetExportService> logger)
        {
            _db = db;
            _options = options;
            _logger = logger;
        }

        public async Task<AiDatasetSnapshot> CreateSnapshotAsync(
            string name,
            string createdBy,
            DateTime? fromUtc,
            DateTime? toUtc,
            bool optInOnly,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Value.Enabled || !_options.Value.TrainingExportEnabled)
                throw new InvalidOperationException("Training export is disabled (AiWorkflow.TrainingExportEnabled).");

            var root = _options.Value.ExportRootPath;
            if (string.IsNullOrWhiteSpace(root))
                root = "Data/AiTrainingExports";

            Directory.CreateDirectory(root);

            var q = _db.AiImageAnalysisSuggestions.AsNoTracking().Where(s => s.ResolvedAtUtc != null);
            if (fromUtc.HasValue)
                q = q.Where(s => s.ResolvedAtUtc >= fromUtc);
            if (toUtc.HasValue)
                q = q.Where(s => s.ResolvedAtUtc <= toUtc);
            if (optInOnly)
                q = q.Where(s => s.DatasetOptIn && s.EligibleForTrainingExport);

            var rows = await q.OrderBy(s => s.Id).Take(500_000).ToListAsync(cancellationToken);

            var filter = JsonSerializer.Serialize(new { fromUtc, toUtc, optInOnly });
            var snapshot = new AiDatasetSnapshot
            {
                Name = name.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                FilterJson = filter,
                SchemaVersion = "1",
                RowCountEstimate = rows.Count,
                Notes = "JSONL export of AiImageAnalysisSuggestions"
            };

            var fileName = $"{snapshot.Id:N}.jsonl";
            var path = Path.Combine(root, fileName);

            await using (var fs = File.Create(path))
            await using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                foreach (var r in rows)
                {
                    var line = JsonSerializer.Serialize(r);
                    await writer.WriteLineAsync(line);
                }
            }

            using (var sha = SHA256.Create())
            await using (var fs = File.OpenRead(path))
            {
                var hash = await sha.ComputeHashAsync(fs, cancellationToken);
                snapshot.ChecksumSha256 = Convert.ToHexString(hash);
            }

            snapshot.ExportPath = path;
            _db.AiDatasetSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("AI dataset snapshot {SnapshotId} wrote {Rows} rows to {Path}", snapshot.Id, rows.Count, path);
            return snapshot;
        }
    }
}
