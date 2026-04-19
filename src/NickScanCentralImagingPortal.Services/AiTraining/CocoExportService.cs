using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiTraining
{
    /// <summary>
    /// Builds a COCO Object Detection JSON corpus from NSCIM analyst decisions.
    ///
    /// This is the load-bearing AI-training-flywheel exporter (Gap 4 of the
    /// approved plan). It joins, in one query pass per dataset:
    ///
    ///   - ImageAnalysisDecisions  (the analyst's verdict + structured finding ids)
    ///   - ContainerAnnotations    (drawn rectangles with optional category id)
    ///   - SuspiciousAreas JSON    (legacy embedded rectangle blob, parsed inline)
    ///   - ManifestSnapshots       (frozen-at-decision-time manifest for revenue training)
    ///   - FS6000Images / AseScans (image references for the COCO `images` array)
    ///   - ThreatCategories + RevenueAnomalyCategories (the two finding lookups)
    ///
    /// Output schema mirrors the PhD reference at C:\AI\sample_training_export.json
    /// produced by inspection_recorder.export_to_coco — same field names, same
    /// shape, same conventions. A trainer that consumes one can consume the other.
    ///
    /// Two design choices worth flagging:
    ///
    /// 1. Categories are EMITTED FROM THE LOOKUP TABLES, not from the union of
    ///    categories that appear in this dataset. Trainers expect a stable id
    ///    space across exports — pinning ids to threatcategories.id /
    ///    revenueanomalycategories.id makes that automatic. Security ids are
    ///    used as-is; revenue ids are offset by 1000 so the two domains share
    ///    the same `category_id` space without colliding.
    ///
    /// 2. Inclusion criteria are explicit and conservative. Default behaviour:
    ///    only decisions where at least one finding category is set AND the
    ///    decision is not Pending get exported. Pending / un-categorised rows
    ///    are skipped to avoid polluting the training set with "operator hadn't
    ///    decided yet" noise. Override with includeUncategorized = true.
    /// </summary>
    public class CocoExportService
    {
        private readonly ApplicationDbContext _appDb;
        private readonly ILogger<CocoExportService> _logger;

        // Revenue category ids share the same `category_id` space as threat ids.
        // Offset chosen to leave room for ~999 security categories (we have 13).
        public const int RevenueCategoryIdOffset = 1000;

        public CocoExportService(ApplicationDbContext appDb, ILogger<CocoExportService> logger)
        {
            _appDb = appDb;
            _logger = logger;
        }

        /// <summary>
        /// Build a COCO JSON object for the matched analyst decisions.
        /// </summary>
        /// <param name="from">Lower bound on ImageAnalysisDecision.ReviewedAt (UTC). Null = no lower bound.</param>
        /// <param name="to">Upper bound on ImageAnalysisDecision.ReviewedAt (UTC). Null = no upper bound.</param>
        /// <param name="includeUncategorized">When false (default), skip decisions with no ThreatCategoryId AND no RevenueAnomalyCategoryId. When true, include them with no annotations.</param>
        /// <param name="maxRows">Cap on the number of decision rows to load. Defaults to 50,000 — enough for any realistic training run, small enough to avoid OOM on the server.</param>
        public async Task<CocoExportResult> BuildAsync(
            DateTime? from = null,
            DateTime? to = null,
            bool includeUncategorized = false,
            int maxRows = 50_000,
            CancellationToken cancellationToken = default)
        {
            // ─── 1. Pull the lookup tables once ─────────────────────────────────
            var threatCategories = await _appDb.ThreatCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync(cancellationToken);
            var revenueCategories = await _appDb.RevenueAnomalyCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync(cancellationToken);

            // ─── 2. Pull eligible decisions ──────────────────────────────────────
            var query = _appDb.ImageAnalysisDecisions.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(d => d.ReviewedAt >= from.Value);
            if (to.HasValue) query = query.Where(d => d.ReviewedAt <= to.Value);
            if (!includeUncategorized)
            {
                query = query.Where(d => d.ThreatCategoryId != null || d.RevenueAnomalyCategoryId != null);
            }

            var decisions = await query
                .Where(d => d.Decision != "Pending")
                .OrderBy(d => d.Id)
                .Take(maxRows)
                .ToListAsync(cancellationToken);

            // ─── 3. Pull related annotations (typed rows) and manifest snapshots ─
            var decisionIds = decisions.Select(d => d.Id).ToList();
            var containerNumbers = decisions.Select(d => d.ContainerNumber).Distinct().ToList();

            // 1.12.0: prefer the canonical decision-linked path. After the Gap 2
            // backfill every historical decision with SuspiciousAreas has typed rows
            // tied to its id, so a decision lookup is reliable. Container-keyed rows
            // are still loaded as a fallback for the (now narrow) case where a
            // decision has no decision-linked annotations but the container does.
            var typedAnnotations = await _appDb.ContainerAnnotations
                .AsNoTracking()
                .Where(a => !a.IsDeleted
                            && (a.ThreatCategoryId != null || a.RevenueAnomalyCategoryId != null)
                            && (a.ImageAnalysisDecisionId != null
                                && decisionIds.Contains(a.ImageAnalysisDecisionId.Value)))
                .ToListAsync(cancellationToken);

            var typedAnnotationsContainerFallback = await _appDb.ContainerAnnotations
                .AsNoTracking()
                .Where(a => containerNumbers.Contains(a.ContainerNumber)
                            && a.ImageAnalysisDecisionId == null
                            && !a.IsDeleted
                            && (a.ThreatCategoryId != null || a.RevenueAnomalyCategoryId != null))
                .ToListAsync(cancellationToken);

            var manifestSnapshots = await _appDb.ManifestSnapshots
                .AsNoTracking()
                .Where(s => decisionIds.Contains(s.ImageAnalysisDecisionId))
                .ToListAsync(cancellationToken);

            // ─── 4. Build the COCO structure ─────────────────────────────────────
            var coco = new CocoDataset
            {
                Info = new CocoInfo
                {
                    Description = "NSCIM X-ray cargo inspection dataset (human-labelled)",
                    Version = "1.0.0",
                    Year = DateTime.UtcNow.Year,
                    Contributor = "NSCIM Production",
                    DateCreated = DateTime.UtcNow.ToString("O"),
                },
                Licenses = new List<CocoLicense>
                {
                    new() { Id = 1, Name = "Restricted research use" },
                },
                Categories = new List<CocoCategory>(),
                Images = new List<CocoImage>(),
                Annotations = new List<CocoAnnotation>(),
            };

            foreach (var c in threatCategories.Where(c => c.IsActive))
            {
                coco.Categories.Add(new CocoCategory
                {
                    Id = c.Id,
                    Name = c.Code,
                    Supercategory = "threat",
                });
            }
            foreach (var c in revenueCategories.Where(c => c.IsActive))
            {
                coco.Categories.Add(new CocoCategory
                {
                    Id = c.Id + RevenueCategoryIdOffset,
                    Name = c.Code,
                    Supercategory = "revenue",
                });
            }

            // Build images and annotations. Each decision row becomes one
            // COCO image entry; each typed/legacy annotation becomes one
            // COCO annotation entry tied to that image_id.
            int imageId = 0;
            int annotationId = 0;

            var typedAnnotationsByDecisionId = typedAnnotations
                .Where(a => a.ImageAnalysisDecisionId.HasValue)
                .GroupBy(a => a.ImageAnalysisDecisionId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var typedAnnotationsByContainer = typedAnnotationsContainerFallback
                .GroupBy(a => a.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var snapshotsByDecisionId = manifestSnapshots
                .GroupBy(s => s.ImageAnalysisDecisionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SnapshotTakenAtUtc).First());

            foreach (var d in decisions)
            {
                imageId++;

                var snapshot = snapshotsByDecisionId.GetValueOrDefault(d.Id);

                coco.Images.Add(new CocoImage
                {
                    Id = imageId,
                    FileName = BuildImageFileName(d),
                    ContainerNumber = d.ContainerNumber,
                    ScannerType = d.ScannerType,
                    ReviewedBy = d.ReviewedBy,
                    ReviewedAt = d.ReviewedAt.ToString("O"),
                    DecisionId = d.Id,
                    Decision = d.Decision,
                    Width = 0,   // Image dimensions not stored on the decision; trainer reads them from the image bytes.
                    Height = 0,
                    Manifest = snapshot == null ? null : new CocoManifestSummary
                    {
                        BoeDocumentId = snapshot.BOEDocumentId,
                        DeclaredGoodsDescription = snapshot.DeclaredGoodsDescription,
                        ClearanceType = snapshot.ClearanceType,
                        CountryOfOrigin = snapshot.CountryOfOrigin,
                        DeclaredHsCodesJson = snapshot.DeclaredHsCodesJson,
                        DeclaredValuesJson = snapshot.DeclaredValuesJson,
                        TotalDeclaredFob = snapshot.TotalDeclaredFob,
                        Source = snapshot.Source,
                    },
                });

                // ── Decision-level annotation: the operator picked a category but
                //    drew no bounding box, so emit a "whole image" annotation. This
                //    is the case for most security flags where the analyst tags
                //    the image as "narcotic" without drawing a region.
                EmitWholeImageAnnotation(coco, d, ref annotationId, imageId);

                // ── Typed annotations from ContainerAnnotation table.
                // Prefer decision-linked rows (canonical, post-Gap-2 backfill).
                // Fall back to container-keyed rows for free-floating annotations.
                var hasDecisionLinkedTyped = false;
                if (typedAnnotationsByDecisionId.TryGetValue(d.Id, out var decisionAnns))
                {
                    hasDecisionLinkedTyped = decisionAnns.Count > 0;
                    EmitTypedAnnotations(coco, decisionAnns, ref annotationId, imageId);
                }
                else if (typedAnnotationsByContainer.TryGetValue(d.ContainerNumber, out var fallbackAnns))
                {
                    EmitTypedAnnotations(coco, fallbackAnns, ref annotationId, imageId);
                }

                // ── Legacy embedded suspicious areas (JSON blob on the decision row).
                // Only emit if we have NO decision-linked typed rows for this id —
                // otherwise we'd double-count every backfilled box.
                if (!hasDecisionLinkedTyped && !string.IsNullOrWhiteSpace(d.SuspiciousAreas))
                {
                    EmitLegacySuspiciousAreas(coco, d, ref annotationId, imageId);
                }
            }

            _logger.LogInformation(
                "CocoExportService: built dataset with {ImageCount} images, {AnnotationCount} annotations, {CategoryCount} categories",
                coco.Images.Count, coco.Annotations.Count, coco.Categories.Count);

            return new CocoExportResult
            {
                Coco = coco,
                ImageCount = coco.Images.Count,
                AnnotationCount = coco.Annotations.Count,
                CategoryCount = coco.Categories.Count,
            };
        }

        private static string BuildImageFileName(ImageAnalysisDecision d)
        {
            // Stable, deterministic placeholder. The trainer reads bytes by
            // joining ContainerNumber + ScannerType + ScanDate against
            // FS6000Images / AseScans, so this string is informational only.
            return $"{d.ContainerNumber}__{d.ScannerType}__{d.Id}.png";
        }

        private (int? categoryId, string? supercategory) ResolveAnnotationCategoryId(ContainerAnnotation ann)
        {
            if (ann.ThreatCategoryId.HasValue)
            {
                return (ann.ThreatCategoryId.Value, "threat");
            }
            if (ann.RevenueAnomalyCategoryId.HasValue)
            {
                return (ann.RevenueAnomalyCategoryId.Value + RevenueCategoryIdOffset, "revenue");
            }
            return (null, null);
        }

        private void EmitTypedAnnotations(
            CocoDataset coco,
            List<ContainerAnnotation> anns,
            ref int annotationId,
            int imageId)
        {
            foreach (var ann in anns)
            {
                var (catId, supercategory) = ResolveAnnotationCategoryId(ann);
                if (catId == null) continue;

                var w = Math.Abs(ann.X2 - ann.X1);
                var h = Math.Abs(ann.Y2 - ann.Y1);
                var x = Math.Min(ann.X1, ann.X2);
                var y = Math.Min(ann.Y1, ann.Y2);

                annotationId++;
                coco.Annotations.Add(new CocoAnnotation
                {
                    Id = annotationId,
                    ImageId = imageId,
                    CategoryId = catId.Value,
                    Bbox = new List<double> { x, y, w, h },
                    Area = w * h,
                    IsCrowd = 0,
                    Score = 1.0,
                    Source = "container_annotation",
                    Supercategory = supercategory,
                });
            }
        }

        private void EmitWholeImageAnnotation(
            CocoDataset coco,
            ImageAnalysisDecision d,
            ref int annotationId,
            int imageId)
        {
            // Threat category gets one entry, revenue category gets another. A
            // decision tagged with both produces two annotations. The bbox is
            // empty because the operator marked the whole image; trainers can
            // either ignore zero-area annotations or treat them as image-level
            // labels.
            if (d.ThreatCategoryId.HasValue)
            {
                annotationId++;
                coco.Annotations.Add(new CocoAnnotation
                {
                    Id = annotationId,
                    ImageId = imageId,
                    CategoryId = d.ThreatCategoryId.Value,
                    Bbox = new List<double> { 0, 0, 0, 0 },
                    Area = 0,
                    IsCrowd = 0,
                    Score = 1.0,
                    Source = "decision_whole_image",
                    Supercategory = "threat",
                });
            }
            if (d.RevenueAnomalyCategoryId.HasValue)
            {
                annotationId++;
                coco.Annotations.Add(new CocoAnnotation
                {
                    Id = annotationId,
                    ImageId = imageId,
                    CategoryId = d.RevenueAnomalyCategoryId.Value + RevenueCategoryIdOffset,
                    Bbox = new List<double> { 0, 0, 0, 0 },
                    Area = 0,
                    IsCrowd = 0,
                    Score = 1.0,
                    Source = "decision_whole_image",
                    Supercategory = "revenue",
                });
            }
        }

        private void EmitLegacySuspiciousAreas(
            CocoDataset coco,
            ImageAnalysisDecision d,
            ref int annotationId,
            int imageId)
        {
            // Legacy SuspiciousAreas blob: an array of {x,y,width,height,createdBy}.
            // These predate the ContainerAnnotation FK columns. They have no
            // explicit category so we attach them to whichever finding the
            // decision row carries (threat preferred, then revenue). Best-effort
            // parser — malformed JSON is logged and skipped.
            try
            {
                using var doc = JsonDocument.Parse(d.SuspiciousAreas!);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

                var (catId, supercategory) = (
                    d.ThreatCategoryId.HasValue
                        ? ((int?)d.ThreatCategoryId.Value, "threat")
                        : d.RevenueAnomalyCategoryId.HasValue
                            ? (d.RevenueAnomalyCategoryId.Value + RevenueCategoryIdOffset, "revenue")
                            : (null, null));

                if (catId == null) return;

                foreach (var rect in doc.RootElement.EnumerateArray())
                {
                    if (!rect.TryGetProperty("x", out var xEl)) continue;
                    if (!rect.TryGetProperty("y", out var yEl)) continue;
                    if (!rect.TryGetProperty("width", out var wEl)) continue;
                    if (!rect.TryGetProperty("height", out var hEl)) continue;

                    var x = xEl.GetDouble();
                    var y = yEl.GetDouble();
                    var w = wEl.GetDouble();
                    var h = hEl.GetDouble();

                    annotationId++;
                    coco.Annotations.Add(new CocoAnnotation
                    {
                        Id = annotationId,
                        ImageId = imageId,
                        CategoryId = catId.Value,
                        Bbox = new List<double> { x, y, w, h },
                        Area = w * h,
                        IsCrowd = 0,
                        Score = 1.0,
                        Source = "legacy_suspicious_areas",
                        Supercategory = supercategory,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Skipping malformed SuspiciousAreas JSON for decision {DecisionId} ({Container})",
                    d.Id, d.ContainerNumber);
            }
        }
    }

    // ─── Wire-format DTOs (mirror C:\AI\sample_training_export.json) ──────────
    // These deliberately use System.Text.Json camelCase via JsonPropertyName so
    // a Python trainer can consume them with the standard COCO loaders unchanged.

    public class CocoExportResult
    {
        public CocoDataset Coco { get; set; } = new();
        public int ImageCount { get; set; }
        public int AnnotationCount { get; set; }
        public int CategoryCount { get; set; }
    }

    public class CocoDataset
    {
        [System.Text.Json.Serialization.JsonPropertyName("info")] public CocoInfo Info { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("licenses")] public List<CocoLicense> Licenses { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("categories")] public List<CocoCategory> Categories { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("images")] public List<CocoImage> Images { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("annotations")] public List<CocoAnnotation> Annotations { get; set; } = new();
    }

    public class CocoInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("description")] public string Description { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("version")] public string Version { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("year")] public int Year { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("contributor")] public string Contributor { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("date_created")] public string DateCreated { get; set; } = "";
    }

    public class CocoLicense
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public class CocoCategory
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("supercategory")] public string Supercategory { get; set; } = "";
    }

    public class CocoImage
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("file_name")] public string FileName { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("width")] public int Width { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("height")] public int Height { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("container_number")] public string ContainerNumber { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("scanner_type")] public string ScannerType { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("reviewed_by")] public string ReviewedBy { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("reviewed_at")] public string ReviewedAt { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("decision_id")] public int DecisionId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("manifest")] public CocoManifestSummary? Manifest { get; set; }
    }

    public class CocoManifestSummary
    {
        [System.Text.Json.Serialization.JsonPropertyName("boe_document_id")] public int? BoeDocumentId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("declared_goods_description")] public string? DeclaredGoodsDescription { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("clearance_type")] public string? ClearanceType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("country_of_origin")] public string? CountryOfOrigin { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("declared_hs_codes_json")] public string? DeclaredHsCodesJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("declared_values_json")] public string? DeclaredValuesJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("total_declared_fob")] public decimal? TotalDeclaredFob { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("source")] public string? Source { get; set; }
    }

    public class CocoAnnotation
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("image_id")] public int ImageId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("category_id")] public int CategoryId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("bbox")] public List<double> Bbox { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("area")] public double Area { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("iscrowd")] public int IsCrowd { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("score")] public double Score { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("source")] public string Source { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("supercategory")] public string? Supercategory { get; set; }
    }
}
