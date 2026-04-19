using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Schema
{
    /// <summary>
    /// Schema-level regression coverage for the entities introduced by the
    /// 2026-04-06 / 2026-04-07 work (AI training flywheel + Match Correction
    /// Tool + per-image audit + Gap 2 annotation linkage).
    ///
    /// These tests use a fresh in-memory DbContext per test (no WebApplicationFactory)
    /// so they're fast, deterministic, and don't depend on the integration test
    /// machinery. The intent is regression catching, not exhaustive behaviour
    /// testing — if any of these break, an entity / DbContext mapping has
    /// drifted in a breaking way and the build will tell us.
    /// </summary>
    public class NewEntitySchemaTests
    {
        private static ApplicationDbContext CreateInMemoryContext()
        {
            // A unique DB name per call so tests don't bleed into each other when
            // run in parallel.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"NewEntitySchemaTests_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task ImageAnalysisDecision_CanRoundTrip_FindingCategoryIds()
        {
            // Verifies the dual-domain finding category FK columns added in
            // AddThreatAndRevenueAnomalyCategories migrate cleanly into the
            // entity model and survive a save/load cycle.
            using var ctx = CreateInMemoryContext();

            var d = new ImageAnalysisDecision
            {
                ContainerNumber = "TEST1234567",
                ScannerType = "FS6000",
                Decision = "Abnormal",
                ReviewedBy = "tester",
                ReviewedAt = DateTime.UtcNow,
                ThreatCategoryId = 1,
                RevenueAnomalyCategoryId = 5,
            };
            ctx.ImageAnalysisDecisions.Add(d);
            await ctx.SaveChangesAsync();
            var savedId = d.Id;

            // Re-query via AsNoTracking on the same context — we don't need a
            // second context, the relational GetDbConnection() trick doesn't
            // work with the in-memory provider anyway.
            var loaded = await ctx.ImageAnalysisDecisions.AsNoTracking().FirstAsync(x => x.Id == savedId);
            Assert.Equal(1, loaded.ThreatCategoryId);
            Assert.Equal(5, loaded.RevenueAnomalyCategoryId);
            Assert.Equal("Abnormal", loaded.Decision);
        }

        [Fact]
        public async Task ContainerAnnotation_CanRoundTrip_DecisionLinkageAndCategoryIds()
        {
            // Gap 2: ImageAnalysisDecisionId column + per-annotation finding ids.
            using var ctx = CreateInMemoryContext();

            var ann = new ContainerAnnotation
            {
                ContainerNumber = "TEST1234567",
                Type = "Rectangle",
                X1 = 10,
                Y1 = 20,
                X2 = 110,
                Y2 = 120,
                CreatedBy = "tester",
                ImageAnalysisDecisionId = 99,
                ThreatCategoryId = 3,
                RevenueAnomalyCategoryId = null,
            };
            ctx.ContainerAnnotations.Add(ann);
            await ctx.SaveChangesAsync();

            var loaded = await ctx.ContainerAnnotations
                .AsNoTracking()
                .FirstAsync(a => a.ImageAnalysisDecisionId == 99);
            Assert.Equal(3, loaded.ThreatCategoryId);
            Assert.Null(loaded.RevenueAnomalyCategoryId);
            Assert.Equal(10, loaded.X1);
            Assert.Equal(120, loaded.Y2);
        }

        [Fact]
        public async Task MatchQualityFlag_CanPersistAndQueryByContainerAndType()
        {
            // Match Correction Tool: the prevention helper writes flags via
            // WriteMatchQualityFlagAsync which queries by (container, type, !resolved).
            // Ensure the schema supports that lookup pattern.
            using var ctx = CreateInMemoryContext();

            var flag = new MatchQualityFlag
            {
                ContainerNumber = "MATCH1234567",
                ScannerType = "FS6000",
                BOEDocumentId = 42,
                FlagType = "NullDeliveryPlace",
                Severity = "Critical",
                Description = "BOE has no DeliveryPlace; location gate cannot verify.",
                IsResolved = false,
                CreatedAtUtc = DateTime.UtcNow,
            };
            ctx.MatchQualityFlags.Add(flag);
            await ctx.SaveChangesAsync();

            var found = await ctx.MatchQualityFlags
                .Where(f => f.ContainerNumber == "MATCH1234567"
                            && f.FlagType == "NullDeliveryPlace"
                            && !f.IsResolved)
                .FirstOrDefaultAsync();
            Assert.NotNull(found);
            Assert.Equal("Critical", found!.Severity);
            Assert.Equal(42, found.BOEDocumentId);
        }

        [Fact]
        public async Task ManifestSnapshot_CanPersistWithDecisionLinkage()
        {
            // Gap 0: a snapshot is keyed to one ImageAnalysisDecision via FK.
            using var ctx = CreateInMemoryContext();

            // The in-memory provider doesn't enforce FK constraints, so we can
            // just write a snapshot row without a parent. We're testing that
            // the entity round-trips through the EF mapping, not referential
            // integrity.
            var snap = new ManifestSnapshot
            {
                ImageAnalysisDecisionId = 7,
                BOEDocumentId = 1234,
                SnapshotTakenAtUtc = DateTime.UtcNow,
                ContainerNumber = "SNAP1234567",
                ScannerType = "FS6000",
                MasterBlNumber = "MBL-001",
                DeclaredGoodsDescription = "Frozen fish",
                DeclaredHsCodesJson = "[\"030363\"]",
                ClearanceType = "IM",
                Source = "live_capture",
            };
            ctx.ManifestSnapshots.Add(snap);
            await ctx.SaveChangesAsync();

            var loaded = await ctx.ManifestSnapshots
                .AsNoTracking()
                .FirstAsync(s => s.ImageAnalysisDecisionId == 7);
            Assert.Equal("MBL-001", loaded.MasterBlNumber);
            Assert.Equal("live_capture", loaded.Source);
            Assert.Equal("[\"030363\"]", loaded.DeclaredHsCodesJson);
        }

        [Fact]
        public async Task AuditImageDecision_CanPersistMultipleChildrenForOneAuditDecision()
        {
            // Per-image audit: multiple AuditImageDecision children should
            // co-exist for the same AuditDecisionId, ordered by ImageIndex.
            using var ctx = CreateInMemoryContext();

            var children = new[]
            {
                new AuditImageDecision { AuditDecisionId = 1, ContainerNumber = "AUDIT1", ScannerType = "FS6000-Main", ImageIndex = 0, Decision = "Approved", AuditedBy = "auditor", AuditedAtUtc = DateTime.UtcNow },
                new AuditImageDecision { AuditDecisionId = 1, ContainerNumber = "AUDIT1", ScannerType = "FS6000-Side", ImageIndex = 1, Decision = "Rejected", Notes = "concern on side view", AuditedBy = "auditor", AuditedAtUtc = DateTime.UtcNow },
                new AuditImageDecision { AuditDecisionId = 1, ContainerNumber = "AUDIT1", ScannerType = "FS6000-Top", ImageIndex = 2, Decision = "Approved", AuditedBy = "auditor", AuditedAtUtc = DateTime.UtcNow },
            };
            ctx.AuditImageDecisions.AddRange(children);
            await ctx.SaveChangesAsync();

            var loaded = await ctx.AuditImageDecisions
                .AsNoTracking()
                .Where(c => c.AuditDecisionId == 1)
                .OrderBy(c => c.ImageIndex)
                .ToListAsync();
            Assert.Equal(3, loaded.Count);
            Assert.Equal("Rejected", loaded[1].Decision);
            Assert.Equal("concern on side view", loaded[1].Notes);
            // Ordering preserved
            Assert.Equal(new[] { 0, 1, 2 }, loaded.Select(c => c.ImageIndex).ToArray());
        }
    }
}
