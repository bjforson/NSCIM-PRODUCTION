using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;
using NickScanCentralImagingPortal.Core.Entities.CameraEvidence;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed class CameraEvidenceService : ICameraEvidenceService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUniFiProtectClient _protectClient;
        private readonly ICameraEvidenceOcrService _ocrService;
        private readonly ICameraEvidenceSecretResolver _secretResolver;
        private readonly CameraEvidenceOptions _options;
        private readonly UniFiProtectOptions _protectOptions;
        private readonly ILogger<CameraEvidenceService> _logger;

        public CameraEvidenceService(
            ApplicationDbContext db,
            IUniFiProtectClient protectClient,
            ICameraEvidenceOcrService ocrService,
            ICameraEvidenceSecretResolver secretResolver,
            IOptions<CameraEvidenceOptions> options,
            IOptions<UniFiProtectOptions> protectOptions,
            ILogger<CameraEvidenceService> logger)
        {
            _db = db;
            _protectClient = protectClient;
            _ocrService = ocrService;
            _secretResolver = secretResolver;
            _options = options.Value;
            _protectOptions = protectOptions.Value;
            _logger = logger;
        }

        public async Task<CameraEvidenceHealthDto> GetHealthAsync(CancellationToken cancellationToken)
        {
            var sites = await _db.CameraEvidenceSites
                .AsNoTracking()
                .Select(site => new CameraEvidenceSiteHealthDto
                {
                    SiteId = site.Id,
                    SiteKey = site.SiteKey,
                    DisplayName = site.DisplayName,
                    LocationName = site.LocationName,
                    IsEnabled = site.IsEnabled,
                    SourceCount = _db.CameraEvidenceSources.Count(source => source.SiteId == site.Id),
                    PendingQueueCount = _db.CameraEvidenceQueueItems.Count(item => item.SiteId == site.Id && item.Status == "Pending"),
                    Status = site.IsEnabled ? "Configured" : "Disabled"
                })
                .OrderBy(site => site.SiteKey)
                .ToListAsync(cancellationToken);

            foreach (var configuredSite in _protectOptions.Sites.Where(s => !string.IsNullOrWhiteSpace(s.SiteKey)))
            {
                if (sites.Any(s => string.Equals(s.SiteKey, configuredSite.SiteKey, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                sites.Add(new CameraEvidenceSiteHealthDto
                {
                    SiteKey = configuredSite.SiteKey,
                    DisplayName = string.IsNullOrWhiteSpace(configuredSite.DisplayName) ? configuredSite.SiteKey : configuredSite.DisplayName,
                    LocationName = configuredSite.LocationName,
                    IsEnabled = configuredSite.IsEnabled,
                    Status = "ConfiguredInOptions",
                    Message = "Configured in UniFiProtect:Sites but not yet persisted as a CameraEvidenceSite row."
                });
            }

            return new CameraEvidenceHealthDto
            {
                Enabled = _options.Enabled,
                WebhookIngestionEnabled = _options.WebhookIngestionEnabled,
                MediaFetchEnabled = _options.MediaFetchEnabled,
                OcrEnabled = _options.OcrEnabled,
                ExternalVisionFallbackEnabled = _options.ExternalVisionFallbackEnabled,
                CoreReadOnlyLookupEnabled = _options.CoreReadOnlyLookupEnabled,
                CoreDisplayPromotionEnabled = _options.CoreDisplayPromotionEnabled,
                CoreDecisionSupportEnabled = _options.CoreDecisionSupportEnabled,
                CoreAutomationEnabled = _options.CoreAutomationEnabled,
                SiteCount = await _db.CameraEvidenceSites.CountAsync(cancellationToken),
                SourceCount = await _db.CameraEvidenceSources.CountAsync(cancellationToken),
                PendingQueueCount = await _db.CameraEvidenceQueueItems.CountAsync(item => item.Status == "Pending", cancellationToken),
                ReviewBacklogCount = await _db.CameraEvidenceOcrResults.CountAsync(result => result.ReviewStatus == "Pending", cancellationToken),
                Sites = sites
            };
        }

        public async Task<IReadOnlyList<CameraEvidenceSiteDto>> GetSitesAsync(CancellationToken cancellationToken)
        {
            return await _db.CameraEvidenceSites
                .AsNoTracking()
                .OrderBy(site => site.SiteKey)
                .Select(site => new CameraEvidenceSiteDto
                {
                    Id = site.Id,
                    SiteKey = site.SiteKey,
                    DisplayName = site.DisplayName,
                    LocationName = site.LocationName,
                    BaseUrl = site.BaseUrl,
                    ApiKeySecretName = site.ApiKeySecretName,
                    WebhookSecretName = site.WebhookSecretName,
                    AllowedWebhookSourceCidrsJson = site.AllowedWebhookSourceCidrsJson,
                    VerifySsl = site.VerifySsl,
                    RequestTimeoutSeconds = site.RequestTimeoutSeconds,
                    IsEnabled = site.IsEnabled,
                    CreatedAtUtc = site.CreatedAtUtc,
                    UpdatedAtUtc = site.UpdatedAtUtc,
                    SourceCount = _db.CameraEvidenceSources.Count(source => source.SiteId == site.Id)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<CameraEvidenceSiteDto> UpsertSiteAsync(CameraEvidenceSiteUpsertRequest request, string actorUserId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var siteKey = NormalizeSiteKey(request.SiteKey);
            if (string.IsNullOrWhiteSpace(siteKey))
            {
                throw new InvalidOperationException("SiteKey is required.");
            }

            var site = request.Id.HasValue
                ? await _db.CameraEvidenceSites.AsTracking().FirstOrDefaultAsync(s => s.Id == request.Id.Value, cancellationToken)
                : await _db.CameraEvidenceSites.AsTracking().FirstOrDefaultAsync(s => s.SiteKey == siteKey, cancellationToken);

            if (site == null)
            {
                site = new CameraEvidenceSite
                {
                    SiteKey = siteKey,
                    CreatedAtUtc = now
                };
                _db.CameraEvidenceSites.Add(site);
            }

            site.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? siteKey : request.DisplayName.Trim();
            site.LocationName = TrimToNull(request.LocationName);
            site.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
            site.ApiKeySecretName = TrimToNull(request.ApiKeySecretName);
            site.WebhookSecretName = TrimToNull(request.WebhookSecretName);
            site.AllowedWebhookSourceCidrsJson = TrimToNull(request.AllowedWebhookSourceCidrsJson);
            site.VerifySsl = request.VerifySsl;
            site.RequestTimeoutSeconds = Math.Clamp(request.RequestTimeoutSeconds, 1, 120);
            site.IsEnabled = request.IsEnabled;
            site.UpdatedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await AddAuditAsync(site.Id, null, site.Id, "CameraEvidenceSite", request.Id.HasValue ? "Upsert" : "Create", actorUserId, null, cancellationToken);
            return (await GetSitesAsync(cancellationToken)).First(s => s.Id == site.Id);
        }

        public async Task<IReadOnlyList<CameraEvidenceSourceDto>> GetSourcesAsync(Guid? siteId, CancellationToken cancellationToken)
        {
            var query = _db.CameraEvidenceSources.AsNoTracking().Include(source => source.Site).AsQueryable();
            if (siteId.HasValue)
            {
                query = query.Where(source => source.SiteId == siteId.Value);
            }

            var sources = await query
                .OrderBy(source => source.Site.SiteKey)
                .ThenBy(source => source.DisplayName)
                .ToListAsync(cancellationToken);

            return sources.Select(ToSourceDto).ToList();
        }

        public async Task<CameraEvidenceSourceDto> UpsertSourceAsync(CameraEvidenceSourceUpsertRequest request, string actorUserId, CancellationToken cancellationToken)
        {
            var siteExists = await _db.CameraEvidenceSites.AnyAsync(site => site.Id == request.SiteId, cancellationToken);
            if (!siteExists)
            {
                throw new InvalidOperationException("Camera evidence site was not found.");
            }

            var source = request.Id.HasValue
                ? await _db.CameraEvidenceSources.AsTracking().FirstOrDefaultAsync(s => s.Id == request.Id.Value, cancellationToken)
                : null;

            if (source == null)
            {
                source = new CameraEvidenceSource
                {
                    SiteId = request.SiteId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.CameraEvidenceSources.Add(source);
            }

            source.ProtectCameraId = TrimToNull(request.ProtectCameraId);
            source.ProtectDeviceKey = TrimToNull(request.ProtectDeviceKey);
            source.MacAddress = TrimToNull(request.MacAddress);
            source.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.ProtectCameraId ?? "Protect Camera"
                : request.DisplayName.Trim();
            source.LocationName = TrimToNull(request.LocationName);
            source.OperationalZone = TrimToNull(request.OperationalZone);
            source.ExpectedTextType = NormalizeOption(request.ExpectedTextType, "unknown");
            source.CaptureMode = NormalizeOption(request.CaptureMode, "snapshot");
            source.OcrProfile = NormalizeOption(request.OcrProfile, "default");
            source.IsEnabled = request.IsEnabled;
            source.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            await AddAuditAsync(source.SiteId, null, source.Id, "CameraEvidenceSource", request.Id.HasValue ? "Upsert" : "Create", actorUserId, null, cancellationToken);

            return (await GetSourcesAsync(source.SiteId, cancellationToken)).First(s => s.Id == source.Id);
        }

        public async Task<IReadOnlyList<ProtectCameraDto>> GetProtectCamerasAsync(Guid siteId, CancellationToken cancellationToken)
        {
            var runtime = await ResolveRuntimeSiteAsync(siteId, cancellationToken);
            return await _protectClient.GetCamerasAsync(runtime, cancellationToken);
        }

        public async Task<CameraEvidenceSnapshotTestResultDto> TestSnapshotAsync(Guid sourceId, string actorUserId, CancellationToken cancellationToken)
        {
            var source = await _db.CameraEvidenceSources
                .AsNoTracking()
                .Include(s => s.Site)
                .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);

            if (source == null)
            {
                return new CameraEvidenceSnapshotTestResultDto { Success = false, Error = "Camera evidence source was not found." };
            }

            if (string.IsNullOrWhiteSpace(source.ProtectCameraId))
            {
                return new CameraEvidenceSnapshotTestResultDto { Success = false, Error = "Source has no ProtectCameraId." };
            }

            var runtime = await ResolveRuntimeSiteAsync(source.SiteId, cancellationToken);
            var now = DateTime.UtcNow;
            var evidenceEvent = new CameraEvidenceEvent
            {
                SiteId = source.SiteId,
                SourceId = source.Id,
                AlarmName = "Manual snapshot test",
                TriggerType = "ManualTest",
                ProtectDeviceKey = source.ProtectDeviceKey ?? source.ProtectCameraId,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                IdempotencyKey = $"manual-test:{source.Id}:{Guid.NewGuid():N}",
                RawPayloadJson = JsonSerializer.Serialize(new { manualTest = true, sourceId, actorUserId, createdAtUtc = now }),
                ProcessingStatus = "ManualTest"
            };
            _db.CameraEvidenceEvents.Add(evidenceEvent);

            try
            {
                var snapshot = await FetchSnapshotWithFallbackAsync(runtime, source.ProtectCameraId, cancellationToken);
                var frame = await StoreFrameAsync(evidenceEvent, source, snapshot, now, cancellationToken);
                evidenceEvent.ProcessingStatus = "FrameCaptured";
                await _db.SaveChangesAsync(cancellationToken);
                await AddAuditAsync(source.SiteId, evidenceEvent.Id, frame.Id, "CameraEvidenceFrame", "ManualSnapshotTest", actorUserId, null, cancellationToken);

                return new CameraEvidenceSnapshotTestResultDto
                {
                    Success = true,
                    FrameId = frame.Id,
                    ContentType = frame.ContentType,
                    ByteCount = snapshot.Content.LongLength,
                    Sha256 = frame.Sha256
                };
            }
            catch (Exception ex)
            {
                evidenceEvent.ProcessingStatus = "FrameCaptureFailed";
                evidenceEvent.ProcessingError = ex.Message;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "Manual camera evidence snapshot test failed for source {SourceId}", sourceId);
                return new CameraEvidenceSnapshotTestResultDto { Success = false, Error = ex.Message };
            }
        }

        public async Task<CameraEvidenceWebhookAcceptedDto> IngestWebhookAsync(
            string siteKey,
            IReadOnlyDictionary<string, string?> headers,
            string rawBody,
            IPAddress? remoteIpAddress,
            CancellationToken cancellationToken)
        {
            if (!_options.Enabled || !_options.WebhookIngestionEnabled)
            {
                return new CameraEvidenceWebhookAcceptedDto
                {
                    Accepted = false,
                    SiteKey = siteKey,
                    ProcessingStatus = "Disabled",
                    Message = "Camera evidence webhook ingestion is disabled."
                };
            }

            var site = await ResolveSiteForKeyAsync(siteKey, createFromOptions: true, cancellationToken);
            if (site == null || !site.IsEnabled)
            {
                return new CameraEvidenceWebhookAcceptedDto
                {
                    Accepted = false,
                    SiteKey = siteKey,
                    ProcessingStatus = "SiteDisabled",
                    Message = "Camera evidence site is not configured or is disabled."
                };
            }

            var runtime = await ResolveRuntimeSiteAsync(site.Id, requireApiKey: _options.MediaFetchEnabled, cancellationToken);
            var auth = AuthenticateWebhook(runtime, headers, remoteIpAddress);
            if (!auth.Success)
            {
                return new CameraEvidenceWebhookAcceptedDto
                {
                    Accepted = false,
                    SiteKey = site.SiteKey,
                    ProcessingStatus = "Rejected",
                    Message = auth.Message
                };
            }

            var payloadJson = EnsureJsonObject(rawBody);
            var fields = ExtractWebhookFields(payloadJson);
            var idempotencyKey = BuildWebhookIdempotencyKey(site.SiteKey, fields.ProviderEventId, fields.ProtectDeviceKey, fields.EventTimestampUtc, payloadJson);

            var existing = await _db.CameraEvidenceEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.SiteId == site.Id && e.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing != null)
            {
                return new CameraEvidenceWebhookAcceptedDto
                {
                    Accepted = true,
                    Duplicate = true,
                    EventId = existing.Id,
                    SiteKey = site.SiteKey,
                    ProcessingStatus = existing.ProcessingStatus,
                    Message = "Duplicate webhook was accepted but not re-queued."
                };
            }

            var source = await FindSourceForWebhookAsync(site.Id, fields.ProtectDeviceKey, cancellationToken);
            var evidenceEvent = new CameraEvidenceEvent
            {
                SiteId = site.Id,
                SourceId = source?.Id,
                ProviderEventId = fields.ProviderEventId,
                IdempotencyKey = idempotencyKey,
                AlarmName = fields.AlarmName,
                TriggerKey = fields.TriggerKey,
                TriggerType = fields.TriggerType,
                ProtectDeviceKey = fields.ProtectDeviceKey,
                EventTimestampUtc = fields.EventTimestampUtc,
                ReceivedAtUtc = DateTime.UtcNow,
                RawPayloadJson = payloadJson,
                ProcessingStatus = source == null ? "UnmappedSource" : "Received",
                ProcessingError = source == null ? "Webhook did not match an enabled camera evidence source." : null
            };

            _db.CameraEvidenceEvents.Add(evidenceEvent);
            var mediaFetchQueued = false;
            if (source != null)
            {
                _db.CameraEvidenceQueueItems.Add(new CameraEvidenceQueueItem
                {
                    SiteId = site.Id,
                    EventId = evidenceEvent.Id,
                    WorkType = "MediaFetch",
                    Status = "Pending",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                mediaFetchQueued = true;
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new CameraEvidenceWebhookAcceptedDto
            {
                Accepted = true,
                EventId = evidenceEvent.Id,
                SiteKey = site.SiteKey,
                Duplicate = false,
                MediaFetchQueued = mediaFetchQueued,
                ProcessingStatus = evidenceEvent.ProcessingStatus,
                Message = mediaFetchQueued ? "Webhook accepted and media fetch queued." : "Webhook accepted but no mapped source was found."
            };
        }

        public async Task<CameraEvidenceEventPageDto> GetEventsAsync(
            string? siteKey,
            Guid? sourceId,
            string? reviewStatus,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _db.CameraEvidenceEvents
                .AsNoTracking()
                .Include(e => e.Site)
                .Include(e => e.Source)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(siteKey))
            {
                var key = NormalizeSiteKey(siteKey);
                query = query.Where(e => e.Site.SiteKey == key);
            }

            if (sourceId.HasValue)
            {
                query = query.Where(e => e.SourceId == sourceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(reviewStatus))
            {
                query = query.Where(e => e.Frames.Any(f => f.OcrResults.Any(o => o.ReviewStatus == reviewStatus)));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var events = await query
                .OrderByDescending(e => e.ReceivedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new CameraEvidenceEventListItemDto
                {
                    Id = e.Id,
                    SiteId = e.SiteId,
                    SiteKey = e.Site.SiteKey,
                    SiteName = e.Site.DisplayName,
                    SourceId = e.SourceId,
                    SourceName = e.Source != null ? e.Source.DisplayName : null,
                    AlarmName = e.AlarmName,
                    TriggerType = e.TriggerType,
                    ProtectDeviceKey = e.ProtectDeviceKey,
                    EventTimestampUtc = e.EventTimestampUtc,
                    ReceivedAtUtc = e.ReceivedAtUtc,
                    ProcessingStatus = e.ProcessingStatus,
                    ProcessingError = e.ProcessingError,
                    FrameCount = e.Frames.Count,
                    OcrResultCount = e.Frames.SelectMany(f => f.OcrResults).Count()
                })
                .ToListAsync(cancellationToken);

            return new CameraEvidenceEventPageDto
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                Data = events
            };
        }

        public async Task<CameraEvidenceEventDetailDto?> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
        {
            var evidenceEvent = await _db.CameraEvidenceEvents
                .AsNoTracking()
                .Include(e => e.Site)
                .Include(e => e.Source)
                .Include(e => e.Frames)
                    .ThenInclude(f => f.OcrResults)
                        .ThenInclude(o => o.ReviewDecisions)
                .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

            if (evidenceEvent == null)
            {
                return null;
            }

            return ToEventDetailDto(evidenceEvent);
        }

        public async Task<CameraEvidenceFrameFile?> GetFrameFileAsync(Guid frameId, CancellationToken cancellationToken)
        {
            var frame = await _db.CameraEvidenceFrames
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == frameId, cancellationToken);

            if (frame == null || string.IsNullOrWhiteSpace(frame.StoragePath))
            {
                return null;
            }

            var root = Path.GetFullPath(GetStorageRoot());
            var path = Path.GetFullPath(frame.StoragePath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                _logger.LogWarning("Blocked or missing camera evidence frame path {Path}", path);
                return null;
            }

            return new CameraEvidenceFrameFile(path, frame.ContentType, Path.GetFileName(path));
        }

        public async Task<CameraEvidenceReviewDecisionDto> ReviewOcrResultAsync(
            Guid ocrResultId,
            CameraEvidenceReviewRequest request,
            string reviewerUserId,
            CancellationToken cancellationToken)
        {
            var allowed = new[] { "Accepted", "Rejected", "Corrected" };
            var decision = NormalizeDecision(request.Decision);
            if (!allowed.Contains(decision, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Decision must be Accepted, Rejected, or Corrected.");
            }

            var ocr = await _db.CameraEvidenceOcrResults.AsTracking().FirstOrDefaultAsync(o => o.Id == ocrResultId, cancellationToken);
            if (ocr == null)
            {
                throw new InvalidOperationException("OCR result was not found.");
            }

            var review = new CameraEvidenceReviewDecision
            {
                OcrResultId = ocr.Id,
                ReviewerUserId = string.IsNullOrWhiteSpace(reviewerUserId) ? "unknown" : reviewerUserId,
                Decision = decision,
                CorrectedText = TrimToNull(request.CorrectedText),
                CorrectedCandidateType = TrimToNull(request.CorrectedCandidateType),
                Notes = TrimToNull(request.Notes),
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.CameraEvidenceReviewDecisions.Add(review);
            ocr.ReviewStatus = decision;

            await _db.SaveChangesAsync(cancellationToken);
            await AddAuditAsync(ocr.SiteId, null, ocr.Id, "CameraEvidenceOcrResult", $"Review:{decision}", reviewerUserId, null, cancellationToken);

            return new CameraEvidenceReviewDecisionDto
            {
                Id = review.Id,
                OcrResultId = review.OcrResultId,
                ReviewerUserId = review.ReviewerUserId,
                Decision = review.Decision,
                CorrectedText = review.CorrectedText,
                CorrectedCandidateType = review.CorrectedCandidateType,
                Notes = review.Notes,
                CreatedAtUtc = review.CreatedAtUtc
            };
        }

        public async Task<int> ProcessPendingWorkAsync(CancellationToken cancellationToken)
        {
            if (!_options.Enabled)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            var workItems = await _db.CameraEvidenceQueueItems
                .AsTracking()
                .Where(item => item.Status == "Pending" && (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
                .OrderBy(item => item.CreatedAtUtc)
                .Take(Math.Clamp(_options.MaxWorkItemsPerPoll, 1, 100))
                .ToListAsync(cancellationToken);

            var processed = 0;
            foreach (var item in workItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = "Processing";
                item.LockedUntilUtc = DateTime.UtcNow.AddMinutes(5);
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                try
                {
                    switch (item.WorkType)
                    {
                        case "MediaFetch":
                            if (!_options.MediaFetchEnabled)
                            {
                                item.Status = "Pending";
                                item.LockedUntilUtc = null;
                                item.UpdatedAtUtc = DateTime.UtcNow;
                                break;
                            }

                            await ProcessMediaFetchAsync(item, cancellationToken);
                            break;
                        case "Ocr":
                            if (!_options.OcrEnabled)
                            {
                                item.Status = "Pending";
                                item.LockedUntilUtc = null;
                                item.UpdatedAtUtc = DateTime.UtcNow;
                                break;
                            }

                            await ProcessOcrAsync(item, cancellationToken);
                            break;
                        default:
                            item.Status = "Failed";
                            item.LastError = $"Unknown camera evidence work type: {item.WorkType}";
                            item.UpdatedAtUtc = DateTime.UtcNow;
                            break;
                    }

                    processed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var maxAttempts = item.WorkType == "Ocr" ? _options.MaxOcrAttempts : _options.MaxMediaFetchAttempts;
                    item.AttemptCount++;
                    item.LastError = ex.Message;
                    item.Status = item.AttemptCount >= maxAttempts ? "Failed" : "Pending";
                    item.NextAttemptAtUtc = item.Status == "Pending"
                        ? DateTime.UtcNow.AddSeconds(Math.Min(300, 15 * Math.Pow(2, item.AttemptCount)))
                        : null;
                    item.LockedUntilUtc = null;
                    item.UpdatedAtUtc = DateTime.UtcNow;
                    _logger.LogWarning(ex, "Camera evidence work item {WorkItemId} failed", item.Id);
                }
                finally
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            return processed;
        }

        private async Task ProcessMediaFetchAsync(CameraEvidenceQueueItem item, CancellationToken cancellationToken)
        {
            var evidenceEvent = await _db.CameraEvidenceEvents
                .AsTracking()
                .Include(e => e.Site)
                .Include(e => e.Source)
                .FirstOrDefaultAsync(e => e.Id == item.EventId, cancellationToken);

            if (evidenceEvent?.Source == null)
            {
                throw new InvalidOperationException("Camera evidence event has no mapped source.");
            }

            if (string.IsNullOrWhiteSpace(evidenceEvent.Source.ProtectCameraId))
            {
                throw new InvalidOperationException("Mapped source has no ProtectCameraId.");
            }

            var runtime = await ResolveRuntimeSiteAsync(evidenceEvent.SiteId, cancellationToken);
            var snapshot = await FetchSnapshotWithFallbackAsync(runtime, evidenceEvent.Source.ProtectCameraId, cancellationToken);
            var frame = await StoreFrameAsync(evidenceEvent, evidenceEvent.Source, snapshot, evidenceEvent.EventTimestampUtc ?? DateTime.UtcNow, cancellationToken);

            evidenceEvent.ProcessingStatus = "FrameCaptured";
            evidenceEvent.ProcessingError = null;
            item.Status = "Completed";
            item.LockedUntilUtc = null;
            item.UpdatedAtUtc = DateTime.UtcNow;

            _db.CameraEvidenceQueueItems.Add(new CameraEvidenceQueueItem
            {
                SiteId = evidenceEvent.SiteId,
                EventId = evidenceEvent.Id,
                FrameId = frame.Id,
                WorkType = "Ocr",
                Status = "Pending",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        private async Task ProcessOcrAsync(CameraEvidenceQueueItem item, CancellationToken cancellationToken)
        {
            if (item.FrameId == null)
            {
                throw new InvalidOperationException("OCR queue item has no FrameId.");
            }

            var frame = await _db.CameraEvidenceFrames
                .AsNoTracking()
                .Include(f => f.Source)
                .FirstOrDefaultAsync(f => f.Id == item.FrameId.Value, cancellationToken);
            if (frame == null)
            {
                throw new InvalidOperationException("Camera evidence frame was not found.");
            }

            var extraction = await _ocrService.ExtractAsync(frame, frame.Source, cancellationToken);
            _db.CameraEvidenceOcrResults.Add(new CameraEvidenceOcrResult
            {
                FrameId = frame.Id,
                SiteId = frame.SiteId,
                SourceId = frame.SourceId,
                Engine = extraction.Engine,
                EngineVersion = extraction.EngineVersion,
                RawText = extraction.RawText,
                NormalizedText = extraction.NormalizedText,
                CandidateType = extraction.CandidateType,
                Confidence = extraction.Confidence,
                ValidationStatus = extraction.ValidationStatus,
                ValidationReasonsJson = extraction.ValidationReasonsJson,
                BoundingBoxesJson = extraction.BoundingBoxesJson,
                ReviewStatus = "Pending",
                CreatedAtUtc = DateTime.UtcNow
            });

            var evidenceEvent = await _db.CameraEvidenceEvents.AsTracking().FirstOrDefaultAsync(e => e.Id == item.EventId, cancellationToken);
            if (evidenceEvent != null)
            {
                evidenceEvent.ProcessingStatus = "OcrCompleted";
                evidenceEvent.ProcessingError = null;
            }

            item.Status = "Completed";
            item.LockedUntilUtc = null;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }

        private async Task<UniFiProtectSnapshotResult> FetchSnapshotWithFallbackAsync(CameraEvidenceRuntimeSite runtime, string cameraId, CancellationToken cancellationToken)
        {
            var channel = string.IsNullOrWhiteSpace(_options.DefaultSnapshotChannel) ? "main" : _options.DefaultSnapshotChannel;
            if (_options.SnapshotHighQualityDefault)
            {
                try
                {
                    return await _protectClient.GetSnapshotAsync(runtime, cameraId, channel, highQuality: true, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "High quality Protect snapshot failed for site {SiteKey}, camera {CameraId}; trying standard quality.", runtime.Site.SiteKey, cameraId);
                }
            }

            return await _protectClient.GetSnapshotAsync(runtime, cameraId, channel, highQuality: false, cancellationToken);
        }

        private async Task<CameraEvidenceFrame> StoreFrameAsync(
            CameraEvidenceEvent evidenceEvent,
            CameraEvidenceSource source,
            UniFiProtectSnapshotResult snapshot,
            DateTime frameTimestampUtc,
            CancellationToken cancellationToken)
        {
            var frameId = Guid.NewGuid();
            var siteKey = evidenceEvent.Site?.SiteKey ?? (await _db.CameraEvidenceSites.AsNoTracking().Where(s => s.Id == evidenceEvent.SiteId).Select(s => s.SiteKey).FirstAsync(cancellationToken));
            var extension = ContentTypeToExtension(snapshot.ContentType);
            var root = GetStorageRoot();
            var relative = Path.Combine(
                NormalizeFileSegment(siteKey),
                source.Id.ToString("N"),
                frameTimestampUtc.ToString("yyyy"),
                frameTimestampUtc.ToString("MM"),
                frameTimestampUtc.ToString("dd"),
                evidenceEvent.Id.ToString("N"),
                $"{frameId:N}{extension}");
            var fullPath = Path.GetFullPath(Path.Combine(root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, snapshot.Content, cancellationToken);

            var frame = new CameraEvidenceFrame
            {
                Id = frameId,
                EventId = evidenceEvent.Id,
                SiteId = evidenceEvent.SiteId,
                SourceId = source.Id,
                CaptureMode = source.CaptureMode,
                FrameTimestampUtc = DateTime.SpecifyKind(frameTimestampUtc, DateTimeKind.Utc),
                RelativeOffsetMs = 0,
                StoragePath = fullPath,
                ContentType = snapshot.ContentType,
                Sha256 = ComputeSha256(snapshot.Content),
                IsHighQuality = snapshot.IsHighQuality,
                ProtectSnapshotParametersJson = snapshot.SnapshotParametersJson,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.CameraEvidenceFrames.Add(frame);
            return frame;
        }

        private WebhookAuthenticationResult AuthenticateWebhook(
            CameraEvidenceRuntimeSite runtime,
            IReadOnlyDictionary<string, string?> headers,
            IPAddress? remoteIpAddress)
        {
            if (!string.IsNullOrWhiteSpace(runtime.WebhookSecret))
            {
                var supplied = ReadHeader(headers, _protectOptions.WebhookSecretHeader);
                if (!FixedTimeEquals(supplied, runtime.WebhookSecret))
                {
                    return new WebhookAuthenticationResult(false, HttpStatusCode.Unauthorized, "Invalid or missing webhook secret.");
                }
            }

            if (runtime.AllowedWebhookSourceCidrs.Count > 0)
            {
                if (remoteIpAddress == null)
                {
                    return new WebhookAuthenticationResult(false, HttpStatusCode.Forbidden, "Webhook source address could not be verified.");
                }

                var allowed = runtime.AllowedWebhookSourceCidrs.Any(cidr => AddressMatches(remoteIpAddress, cidr));
                if (!allowed)
                {
                    return new WebhookAuthenticationResult(false, HttpStatusCode.Forbidden, "Webhook source address is not allowed.");
                }
            }

            return new WebhookAuthenticationResult(true, HttpStatusCode.Accepted, null);
        }

        private async Task<CameraEvidenceRuntimeSite> ResolveRuntimeSiteAsync(Guid siteId, CancellationToken cancellationToken)
        {
            return await ResolveRuntimeSiteAsync(siteId, requireApiKey: true, cancellationToken);
        }

        private async Task<CameraEvidenceRuntimeSite> ResolveRuntimeSiteAsync(Guid siteId, bool requireApiKey, CancellationToken cancellationToken)
        {
            var site = await _db.CameraEvidenceSites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken)
                ?? throw new InvalidOperationException("Camera evidence site was not found.");
            return BuildRuntimeSite(site, requireApiKey);
        }

        private async Task<CameraEvidenceSite?> ResolveSiteForKeyAsync(string siteKey, bool createFromOptions, CancellationToken cancellationToken)
        {
            var normalized = NormalizeSiteKey(siteKey);
            var site = await _db.CameraEvidenceSites.AsTracking().FirstOrDefaultAsync(s => s.SiteKey == normalized, cancellationToken);
            if (site != null || !createFromOptions)
            {
                return site;
            }

            var configured = _protectOptions.Sites.FirstOrDefault(s => string.Equals(s.SiteKey, normalized, StringComparison.OrdinalIgnoreCase));
            if (configured == null)
            {
                return null;
            }

            site = new CameraEvidenceSite
            {
                SiteKey = normalized,
                DisplayName = string.IsNullOrWhiteSpace(configured.DisplayName) ? normalized : configured.DisplayName,
                LocationName = TrimToNull(configured.LocationName),
                BaseUrl = configured.BaseUrl.Trim().TrimEnd('/'),
                ApiKeySecretName = TrimToNull(configured.ApiKeySecretName),
                WebhookSecretName = TrimToNull(configured.WebhookSecretName),
                AllowedWebhookSourceCidrsJson = configured.AllowedWebhookSourceCidrs.Count == 0 ? null : JsonSerializer.Serialize(configured.AllowedWebhookSourceCidrs),
                VerifySsl = configured.VerifySsl,
                RequestTimeoutSeconds = Math.Clamp(configured.RequestTimeoutSeconds, 1, 120),
                IsEnabled = configured.IsEnabled,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.CameraEvidenceSites.Add(site);
            await _db.SaveChangesAsync(cancellationToken);
            return site;
        }

        private CameraEvidenceRuntimeSite BuildRuntimeSite(CameraEvidenceSite site, bool requireApiKey)
        {
            var configured = _protectOptions.Sites.FirstOrDefault(s => string.Equals(s.SiteKey, site.SiteKey, StringComparison.OrdinalIgnoreCase));
            var apiKey = _secretResolver.Resolve(site.ApiKeySecretName, configured?.ApiKey)
                ?? _secretResolver.Resolve(configured?.ApiKeySecretName, configured?.ApiKey)
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey) && requireApiKey)
            {
                throw new InvalidOperationException($"UniFi Protect API key is not configured for site '{site.SiteKey}'.");
            }

            var webhookSecret = _secretResolver.Resolve(site.WebhookSecretName, configured?.WebhookSecret)
                ?? _secretResolver.Resolve(configured?.WebhookSecretName, configured?.WebhookSecret);
            var cidrs = ParseStringList(site.AllowedWebhookSourceCidrsJson);
            if (cidrs.Count == 0 && configured?.AllowedWebhookSourceCidrs.Count > 0)
            {
                cidrs = configured.AllowedWebhookSourceCidrs;
            }

            return new CameraEvidenceRuntimeSite(site, apiKey, webhookSecret, cidrs);
        }

        private async Task<CameraEvidenceSource?> FindSourceForWebhookAsync(Guid siteId, string? deviceKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                return null;
            }

            var normalized = deviceKey.Trim();
            return await _db.CameraEvidenceSources
                .AsNoTracking()
                .Where(source => source.SiteId == siteId && source.IsEnabled)
                .FirstOrDefaultAsync(source =>
                    source.ProtectCameraId == normalized ||
                    source.ProtectDeviceKey == normalized ||
                    source.MacAddress == normalized,
                    cancellationToken);
        }

        private static string EnsureJsonObject(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return "{}";
            }

            try
            {
                using var document = JsonDocument.Parse(rawBody);
                return document.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                return JsonSerializer.Serialize(new { raw = rawBody });
            }
        }

        private static WebhookFields ExtractWebhookFields(string payloadJson)
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var deviceKey = FindString(root, "cameraId", "deviceId", "deviceKey", "device", "camera", "mac", "macAddress");
            return new WebhookFields(
                FindString(root, "eventId", "id", "alarmId"),
                FindString(root, "alarmName", "name", "alarm"),
                FindString(root, "triggerKey", "trigger", "key"),
                FindString(root, "triggerType", "type", "eventType"),
                deviceKey,
                FindDateTime(root, "timestamp", "eventTime", "eventTimestamp", "createdAt", "start"));
        }

        private static string? FindString(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString(),
                            JsonValueKind.Number => property.Value.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => property.Value.ValueKind == JsonValueKind.Object
                                ? FindString(property.Value, "id", "key", "mac", "macAddress")
                                : null
                        };
                    }

                    var nested = FindString(property.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    var nested = FindString(child, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static DateTime? FindDateTime(JsonElement element, params string[] names)
        {
            var text = FindString(element, names);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (long.TryParse(text, out var epoch))
            {
                return epoch > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            }

            return DateTimeOffset.TryParse(text, out var parsed) ? parsed.UtcDateTime : null;
        }

        private static string BuildWebhookIdempotencyKey(string siteKey, string? providerEventId, string? deviceKey, DateTime? eventTimestampUtc, string payloadJson)
        {
            if (!string.IsNullOrWhiteSpace(providerEventId))
            {
                return $"{siteKey}:{providerEventId.Trim()}";
            }

            var fingerprint = ComputeSha256(Encoding.UTF8.GetBytes(payloadJson));
            return $"{siteKey}:{deviceKey ?? "unknown"}:{eventTimestampUtc?.ToString("O") ?? "no-time"}:{fingerprint}";
        }

        private async Task AddAuditAsync(
            Guid? siteId,
            Guid? eventId,
            Guid? entityId,
            string entityType,
            string action,
            string? actor,
            object? details,
            CancellationToken cancellationToken)
        {
            _db.CameraEvidenceAuditLogs.Add(new CameraEvidenceAuditLog
            {
                SiteId = siteId,
                EventId = eventId,
                EntityId = entityId,
                EntityType = entityType,
                Action = action,
                ActorUserId = TrimToNull(actor),
                DetailsJson = details == null ? null : JsonSerializer.Serialize(details),
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        private string GetStorageRoot()
        {
            var root = string.IsNullOrWhiteSpace(_options.StorageRoot) ? "Data/CameraEvidence" : _options.StorageRoot;
            if (!Path.IsPathRooted(root))
            {
                root = Path.Combine(AppContext.BaseDirectory, root);
            }

            Directory.CreateDirectory(root);
            return Path.GetFullPath(root);
        }

        private static CameraEvidenceSourceDto ToSourceDto(CameraEvidenceSource source)
        {
            return new CameraEvidenceSourceDto
            {
                Id = source.Id,
                SiteId = source.SiteId,
                SiteKey = source.Site.SiteKey,
                Provider = source.Provider,
                ProtectCameraId = source.ProtectCameraId,
                ProtectDeviceKey = source.ProtectDeviceKey,
                MacAddress = source.MacAddress,
                DisplayName = source.DisplayName,
                LocationName = source.LocationName,
                OperationalZone = source.OperationalZone,
                ExpectedTextType = source.ExpectedTextType,
                CaptureMode = source.CaptureMode,
                OcrProfile = source.OcrProfile,
                IsEnabled = source.IsEnabled,
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = source.UpdatedAtUtc
            };
        }

        private static CameraEvidenceEventDetailDto ToEventDetailDto(CameraEvidenceEvent e)
        {
            return new CameraEvidenceEventDetailDto
            {
                Id = e.Id,
                SiteId = e.SiteId,
                SiteKey = e.Site.SiteKey,
                SiteName = e.Site.DisplayName,
                SourceId = e.SourceId,
                SourceName = e.Source?.DisplayName,
                ProviderEventId = e.ProviderEventId,
                IdempotencyKey = e.IdempotencyKey,
                AlarmName = e.AlarmName,
                TriggerKey = e.TriggerKey,
                TriggerType = e.TriggerType,
                ProtectDeviceKey = e.ProtectDeviceKey,
                EventTimestampUtc = e.EventTimestampUtc,
                ReceivedAtUtc = e.ReceivedAtUtc,
                RawPayloadJson = e.RawPayloadJson,
                ProcessingStatus = e.ProcessingStatus,
                ProcessingError = e.ProcessingError,
                FrameCount = e.Frames.Count,
                OcrResultCount = e.Frames.SelectMany(f => f.OcrResults).Count(),
                Frames = e.Frames.OrderBy(f => f.FrameTimestampUtc).Select(ToFrameDto).ToList()
            };
        }

        private static CameraEvidenceFrameDto ToFrameDto(CameraEvidenceFrame frame)
        {
            return new CameraEvidenceFrameDto
            {
                Id = frame.Id,
                EventId = frame.EventId,
                SiteId = frame.SiteId,
                SourceId = frame.SourceId,
                CaptureMode = frame.CaptureMode,
                FrameTimestampUtc = frame.FrameTimestampUtc,
                RelativeOffsetMs = frame.RelativeOffsetMs,
                ContentType = frame.ContentType,
                Sha256 = frame.Sha256,
                Width = frame.Width,
                Height = frame.Height,
                IsHighQuality = frame.IsHighQuality,
                CreatedAtUtc = frame.CreatedAtUtc,
                ImagePath = $"/api/camera-evidence/frames/{frame.Id}/image",
                OcrResults = frame.OcrResults.OrderByDescending(o => o.CreatedAtUtc).Select(ToOcrDto).ToList()
            };
        }

        private static CameraEvidenceOcrResultDto ToOcrDto(CameraEvidenceOcrResult result)
        {
            return new CameraEvidenceOcrResultDto
            {
                Id = result.Id,
                FrameId = result.FrameId,
                SiteId = result.SiteId,
                SourceId = result.SourceId,
                Engine = result.Engine,
                EngineVersion = result.EngineVersion,
                RawText = result.RawText,
                NormalizedText = result.NormalizedText,
                CandidateType = result.CandidateType,
                Confidence = result.Confidence,
                ValidationStatus = result.ValidationStatus,
                ValidationReasonsJson = result.ValidationReasonsJson,
                BoundingBoxesJson = result.BoundingBoxesJson,
                ReviewStatus = result.ReviewStatus,
                CreatedAtUtc = result.CreatedAtUtc,
                ReviewDecisions = result.ReviewDecisions.OrderByDescending(r => r.CreatedAtUtc).Select(r => new CameraEvidenceReviewDecisionDto
                {
                    Id = r.Id,
                    OcrResultId = r.OcrResultId,
                    ReviewerUserId = r.ReviewerUserId,
                    Decision = r.Decision,
                    CorrectedText = r.CorrectedText,
                    CorrectedCandidateType = r.CorrectedCandidateType,
                    Notes = r.Notes,
                    CreatedAtUtc = r.CreatedAtUtc
                }).ToList()
            };
        }

        private static IReadOnlyList<string> ParseStringList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        private static string NormalizeSiteKey(string siteKey)
        {
            return (siteKey ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeOption(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeDecision(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
        }

        private static string? TrimToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ComputeSha256(byte[] content)
        {
            var hash = SHA256.HashData(content);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ContentTypeToExtension(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                _ => ".jpg"
            };
        }

        private static string NormalizeFileSegment(string value)
        {
            var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray();
            return new string(chars);
        }

        private static string? ReadHeader(IReadOnlyDictionary<string, string?> headers, string headerName)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }

        private static bool FixedTimeEquals(string? supplied, string expected)
        {
            if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected))
            {
                return false;
            }

            var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            return suppliedBytes.Length == expectedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }

        private static bool AddressMatches(IPAddress address, string cidrOrAddress)
        {
            if (string.IsNullOrWhiteSpace(cidrOrAddress))
            {
                return false;
            }

            if (!cidrOrAddress.Contains('/'))
            {
                return IPAddress.TryParse(cidrOrAddress, out var exact) && exact.Equals(address);
            }

            var parts = cidrOrAddress.Split('/', 2);
            if (!IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefix))
            {
                return false;
            }

            var addressBytes = address.MapToIPv4().GetAddressBytes();
            var networkBytes = network.MapToIPv4().GetAddressBytes();
            if (addressBytes.Length != 4 || networkBytes.Length != 4 || prefix < 0 || prefix > 32)
            {
                return false;
            }

            var addressInt = BitConverter.ToUInt32(addressBytes.Reverse().ToArray(), 0);
            var networkInt = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0);
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            return (addressInt & mask) == (networkInt & mask);
        }

        private sealed record WebhookFields(
            string? ProviderEventId,
            string? AlarmName,
            string? TriggerKey,
            string? TriggerType,
            string? ProtectDeviceKey,
            DateTime? EventTimestampUtc);
    }
}
