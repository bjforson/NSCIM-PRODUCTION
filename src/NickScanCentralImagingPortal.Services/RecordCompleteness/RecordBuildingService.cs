using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.RecordCompleteness
{
    /// <summary>
    /// Event-driven record building service. Called immediately after any BOE data
    /// is ingested (batch or individual download) and when container images become
    /// available. Replaces the 30-minute polling cycle of RecordReconciliationWorker
    /// as the primary record builder.
    ///
    /// All methods are idempotent — safe to call multiple times for the same
    /// declaration or container.
    /// </summary>
    public interface IRecordBuildingService
    {
        /// <summary>
        /// Build or update the RecordCompletenessStatus for a declaration number.
        /// Creates the record + expected containers if new, or amends with new
        /// containers if the record already exists. Then promotes children
        /// (AwaitingScan → Pending → Ready) and recomputes rollups.
        /// </summary>
        Task BuildOrUpdateRecordAsync(string declarationNumber, CancellationToken ct = default);

        /// <summary>
        /// Promote a single container within a record when its images become available.
        /// Flips the matching RecordExpectedContainer from Pending → Ready and
        /// recomputes the parent record rollups. Called by ContainerCompletenessService
        /// when HasImageData flips to true.
        /// </summary>
        Task PromoteContainerAndRecomputeAsync(string declarationNumber, string containerNumber, CancellationToken ct = default);
    }

    public class RecordBuildingService : IRecordBuildingService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecordBuildingService> _logger;
        private const string SERVICE_ID = "[RECORD-BUILD]";

        public RecordBuildingService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecordBuildingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task BuildOrUpdateRecordAsync(string declarationNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(declarationNumber))
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                // 1. Load all BOE documents for this declaration
                var declarationRows = await icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.DeclarationNumber == declarationNumber
                             && (b.ClearanceType == "IM" || b.ClearanceType == "EX"))
                    .ToListAsync(ct);

                if (declarationRows.Count == 0)
                {
                    _logger.LogDebug("{ServiceId} No IM/EX BOE rows for declaration {Decl} — skipping", SERVICE_ID, declarationNumber);
                    return;
                }

                // 2. Pattern A detection: sibling declarations sharing a container
                var containerNumbers = declarationRows
                    .Select(r => r.ContainerNumber?.Trim().ToUpperInvariant())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();

                var siblings = new List<BOEDocument>();
                if (containerNumbers.Count == 1)
                {
                    siblings = await icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => (b.ClearanceType == "IM" || b.ClearanceType == "EX")
                                 && b.ContainerNumber == containerNumbers[0]
                                 && b.DeclarationNumber != null
                                 && b.DeclarationNumber != declarationNumber)
                        .Take(50)
                        .ToListAsync(ct);
                }

                // 3. Build the record
                var built = RecordCompletenessBuilder.Build(declarationRows, siblings, DateTime.UtcNow);

                var existing = await appDb.RecordCompletenessStatuses
                    .AsTracking()
                    .Include(r => r.ExpectedContainers)
                    .FirstOrDefaultAsync(r => r.DeclarationNumber == declarationNumber, ct);

                if (existing == null)
                {
                    // New record
                    appDb.RecordCompletenessStatuses.Add(built.Record);
                    await appDb.SaveChangesAsync(ct);

                    foreach (var child in built.Children)
                        child.RecordId = built.Record.Id;
                    appDb.RecordExpectedContainers.AddRange(built.Children);
                    await appDb.SaveChangesAsync(ct);

                    _logger.LogInformation("{ServiceId} Created record for {Decl}: {Count} expected containers",
                        SERVICE_ID, declarationNumber, built.Children.Count);

                    existing = built.Record;
                    existing.ExpectedContainers = built.Children;
                }
                else
                {
                    // Amend existing record with any new containers
                    var existingContainerNumbers = new HashSet<string>(
                        existing.ExpectedContainers.Select(c => c.ContainerNumber),
                        StringComparer.OrdinalIgnoreCase);

                    var newChildren = built.Children
                        .Where(c => !existingContainerNumbers.Contains(c.ContainerNumber))
                        .ToList();

                    if (newChildren.Count > 0)
                    {
                        foreach (var child in newChildren)
                            child.RecordId = existing.Id;
                        appDb.RecordExpectedContainers.AddRange(newChildren);
                        existing.LastNewContainerAtUtc = DateTime.UtcNow;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                        await appDb.SaveChangesAsync(ct);

                        _logger.LogInformation("{ServiceId} Amended {Decl}: +{NewCount} containers",
                            SERVICE_ID, declarationNumber, newChildren.Count);
                    }
                }

                // 4. Promote AwaitingScan → Pending/Ready based on completeness evidence
                await PromoteChildrenAsync(appDb, existing, ct);

                // 5. Recompute rollups
                var allChildren = await appDb.RecordExpectedContainers
                    .AsTracking()
                    .Where(c => c.RecordId == existing.Id)
                    .ToListAsync(ct);

                RecordCompletenessBuilder.Recompute(existing, allChildren);
                await appDb.SaveChangesAsync(ct);

                _logger.LogInformation("{ServiceId} Record {Decl} status={Status} (total={Total}, ready={Ready}, awaiting={Awaiting})",
                    SERVICE_ID, declarationNumber, existing.Status,
                    existing.TotalExpectedContainers, existing.ContainersReady, existing.ContainersAwaitingScan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error building record for declaration {Decl}", SERVICE_ID, declarationNumber);
            }
        }

        public async Task PromoteContainerAndRecomputeAsync(string declarationNumber, string containerNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(declarationNumber) || string.IsNullOrWhiteSpace(containerNumber))
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var record = await appDb.RecordCompletenessStatuses
                    .AsTracking()
                    .FirstOrDefaultAsync(r => r.DeclarationNumber == declarationNumber, ct);

                if (record == null)
                    return;

                var child = await appDb.RecordExpectedContainers
                    .AsTracking()
                    .FirstOrDefaultAsync(c => c.RecordId == record.Id
                        && c.ContainerNumber == containerNumber
                        && (c.Status == "AwaitingScan" || c.Status == "Pending"), ct);

                if (child == null)
                    return;

                child.Status = "Ready";
                child.BecameReadyUtc = DateTime.UtcNow;
                record.LastNewContainerAtUtc = DateTime.UtcNow;

                // Recompute rollups
                var allChildren = await appDb.RecordExpectedContainers
                    .AsTracking()
                    .Where(c => c.RecordId == record.Id)
                    .ToListAsync(ct);

                RecordCompletenessBuilder.Recompute(record, allChildren);
                await appDb.SaveChangesAsync(ct);

                _logger.LogInformation("{ServiceId} Promoted container {Container} to Ready in record {Decl} (status now {Status})",
                    SERVICE_ID, containerNumber, declarationNumber, record.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error promoting container {Container} in {Decl}", SERVICE_ID, containerNumber, declarationNumber);
            }
        }

        /// <summary>
        /// Promote children based on ContainerCompletenessStatuses evidence.
        /// </summary>
        private async Task PromoteChildrenAsync(ApplicationDbContext appDb, RecordCompletenessStatus record, CancellationToken ct)
        {
            var children = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => c.RecordId == record.Id && (c.Status == "AwaitingScan" || c.Status == "Pending"))
                .ToListAsync(ct);

            if (children.Count == 0) return;

            var containerNumbers = children.Select(c => c.ContainerNumber).Distinct().ToList();

            var evidence = await appDb.ContainerCompletenessStatuses
                .Where(c => containerNumbers.Contains(c.ContainerNumber))
                .Select(c => new { c.ContainerNumber, c.ScannerType, c.InspectionId, c.HasImageData })
                .ToListAsync(ct);

            var evidenceByContainer = evidence
                .GroupBy(c => c.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.HasImageData).First(), StringComparer.OrdinalIgnoreCase);

            int promoted = 0;
            var nowUtc = DateTime.UtcNow;

            foreach (var child in children)
            {
                if (!evidenceByContainer.TryGetValue(child.ContainerNumber, out var ev)) continue;

                if (child.Status == "AwaitingScan")
                {
                    child.ScannedAtUtc = nowUtc;
                    child.InspectionId = ev.InspectionId;
                    child.ScannerType = ev.ScannerType;
                    child.Status = ev.HasImageData ? "Ready" : "Pending";
                    if (ev.HasImageData)
                        child.BecameReadyUtc = nowUtc;
                    promoted++;
                }
                else if (child.Status == "Pending" && ev.HasImageData)
                {
                    child.Status = "Ready";
                    child.BecameReadyUtc = nowUtc;
                    promoted++;
                }
            }

            if (promoted > 0)
            {
                record.LastNewContainerAtUtc = nowUtc;
                record.UpdatedAtUtc = nowUtc;
                await appDb.SaveChangesAsync(ct);
                _logger.LogInformation("{ServiceId} Promoted {Count} containers for record {Decl}", SERVICE_ID, promoted, record.DeclarationNumber);
            }
        }
    }
}
