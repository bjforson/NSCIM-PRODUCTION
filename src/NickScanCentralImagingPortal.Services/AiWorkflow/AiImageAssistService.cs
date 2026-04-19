using System.Text.Json;
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
    /// Generates AI-assisted image analysis suggestions for a group.
    /// Uses <see cref="IAiModelProvider"/> for actual inference (Claude Vision, stub, etc.).
    /// Results are stored as suggestions for analyst review — never as autonomous decisions.
    /// </summary>
    public class AiImageAssistService : IAiImageAssistService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOptions<AiWorkflowOptions> _options;
        private readonly IAiModelProvider _modelProvider;
        private readonly HttpClient _http;
        private readonly ILogger<AiImageAssistService> _logger;

        public AiImageAssistService(
            ApplicationDbContext db,
            IOptions<AiWorkflowOptions> options,
            IAiModelProvider modelProvider,
            HttpClient http,
            ILogger<AiImageAssistService> logger)
        {
            _db = db;
            _options = options;
            _modelProvider = modelProvider;
            _http = http;
            _logger = logger;
        }

        /// <summary>
        /// Generate AI suggestions for all containers in an analysis group.
        /// Each container's image is analyzed and a suggestion record is created.
        /// </summary>
        public async Task<IReadOnlyList<AiImageAnalysisSuggestion>> GenerateSuggestionsForGroupAsync(
            Guid groupId, CancellationToken cancellationToken = default)
        {
            var opts = _options.Value;
            if (!opts.Enabled || !opts.ImageAssistEnabled)
                return Array.Empty<AiImageAnalysisSuggestion>();

            var group = await _db.AnalysisGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
                throw new InvalidOperationException($"Analysis group {groupId} not found.");

            var records = await _db.AnalysisRecords
                .AsNoTracking()
                .Where(r => r.GroupId == groupId)
                .ToListAsync(cancellationToken);

            if (records.Count == 0)
            {
                _logger.LogWarning("No analysis records found for group {GroupId}", groupId);
                return Array.Empty<AiImageAnalysisSuggestion>();
            }

            var available = await _modelProvider.IsAvailableAsync(cancellationToken);
            if (!available)
            {
                _logger.LogWarning("AI model provider '{Provider}' is not available, skipping group {GroupId}",
                    _modelProvider.ProviderId, groupId);
                return Array.Empty<AiImageAnalysisSuggestion>();
            }

            _logger.LogInformation(
                "Starting AI analysis for group {GroupId} ({RecordCount} containers) using provider '{Provider}'",
                groupId, records.Count, _modelProvider.ProviderId);

            var created = new List<AiImageAnalysisSuggestion>();
            var semaphore = new SemaphoreSlim(opts.MaxConcurrentInferences);

            var tasks = records.Select(async record =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var suggestion = await AnalyzeRecordAsync(group, record, cancellationToken);
                    if (suggestion != null)
                    {
                        lock (created) { created.Add(suggestion); }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (created.Count > 0)
            {
                _db.AiImageAnalysisSuggestions.AddRange(created);
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "AI assist: created {Count}/{Total} suggestion(s) for group {GroupId} via '{Provider}'",
                created.Count, records.Count, groupId, _modelProvider.ProviderId);

            return created;
        }

        /// <summary>Keep backward compatibility with existing controller endpoint.</summary>
        public Task<IReadOnlyList<AiImageAnalysisSuggestion>> GenerateStubSuggestionsForGroupAsync(
            Guid groupId, CancellationToken cancellationToken = default)
            => GenerateSuggestionsForGroupAsync(groupId, cancellationToken);

        private async Task<AiImageAnalysisSuggestion?> AnalyzeRecordAsync(
            Core.Entities.Analysis.AnalysisGroup group,
            Core.Entities.Analysis.AnalysisRecord record,
            CancellationToken cancellationToken)
        {
            try
            {
                var request = new AiImageAnalysisRequest
                {
                    ContainerNumber = record.ContainerNumber ?? "UNKNOWN",
                    ScannerType = record.ScannerType ?? group.ScannerType ?? "FS6000",
                    GroupIdentifier = group.GroupIdentifier
                };

                // Attempt to fetch the image for the model
                await TryLoadImageAsync(request, record, cancellationToken);

                // Run inference
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_options.Value.InferenceTimeoutSeconds));

                var result = await _modelProvider.AnalyzeImageAsync(request, cts.Token);

                // Build suggestion record
                var payloadJson = result.Payload != null
                    ? JsonSerializer.Serialize(result.Payload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    })
                    : null;

                return new AiImageAnalysisSuggestion
                {
                    AnalysisGroupId = group.Id,
                    ContainerNumber = request.ContainerNumber,
                    ScannerType = request.ScannerType,
                    GroupIdentifier = group.GroupIdentifier ?? string.Empty,
                    ModelId = result.ModelId,
                    ModelVersion = result.ModelVersion,
                    FeatureVersion = _options.Value.FeatureVersion,
                    SuggestionPayloadJson = payloadJson,
                    SuggestedDecision = result.SuggestedDecision,
                    Confidence = result.Confidence,
                    Tier = 2,
                    ShadowMode = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    EligibleForTrainingExport = result.Success,
                    DatasetOptIn = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI analysis failed for container {Container} in group {GroupId}",
                    record.ContainerNumber, group.Id);
                return null;
            }
        }

        private async Task TryLoadImageAsync(
            AiImageAnalysisRequest request,
            Core.Entities.Analysis.AnalysisRecord record,
            CancellationToken cancellationToken)
        {
            // If the record has an image URL, try to fetch it
            if (string.IsNullOrWhiteSpace(record.ImageUrl))
                return;

            var baseUrl = _options.Value.InternalApiBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return;

            try
            {
                var imageUrl = record.ImageUrl.StartsWith("http")
                    ? record.ImageUrl
                    : $"{baseUrl.TrimEnd('/')}/{record.ImageUrl.TrimStart('/')}";

                // Append size=full if not already specified
                if (!imageUrl.Contains("size="))
                    imageUrl += (imageUrl.Contains('?') ? "&" : "?") + "size=full";

                var imageBytes = await _http.GetByteArrayAsync(imageUrl, cancellationToken);

                if (imageBytes.Length > 0)
                {
                    request.ImageBase64 = Convert.ToBase64String(imageBytes);
                    request.MediaType = DetectMediaType(imageUrl, imageBytes);

                    _logger.LogDebug("Loaded image for {Container}: {Size} bytes",
                        request.ContainerNumber, imageBytes.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch image for {Container} from {Url}",
                    request.ContainerNumber, record.ImageUrl);
                // Continue without image — model will get text-only context
            }
        }

        private static string DetectMediaType(string url, byte[] bytes)
        {
            // Check magic bytes
            if (bytes.Length >= 4)
            {
                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                    return "image/png";
                if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                    return "image/jpeg";
                if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                    return "image/gif";
            }

            // Fall back to URL extension
            if (url.Contains(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
            if (url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
                url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)) return "image/jpeg";

            return "image/png";
        }
    }
}
