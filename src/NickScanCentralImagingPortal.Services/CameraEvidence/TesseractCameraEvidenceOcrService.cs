using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.CameraEvidence;
using NickScanCentralImagingPortal.Core.Utilities;
using Tesseract;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed class TesseractCameraEvidenceOcrService : ICameraEvidenceOcrService
    {
        private static readonly Regex ContainerPattern = new(@"[A-Z]{4}\s*[-.]?\s*\d{7}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
        private readonly ILogger<TesseractCameraEvidenceOcrService> _logger;
        private readonly string _tessdataPath;

        public TesseractCameraEvidenceOcrService(ILogger<TesseractCameraEvidenceOcrService> logger)
        {
            _logger = logger;
            _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        }

        public async Task<CameraEvidenceOcrExtraction> ExtractAsync(
            CameraEvidenceFrame frame,
            CameraEvidenceSource source,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(frame.StoragePath))
                {
                    return Failure("FrameMissing", $"Frame file does not exist: {frame.StoragePath}");
                }

                var tessdataFile = Path.Combine(_tessdataPath, "eng.traineddata");
                if (!File.Exists(tessdataFile))
                {
                    return Failure("OcrUnavailable", $"Tesseract language data file is missing: {tessdataFile}");
                }

                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                    using var pix = Pix.LoadFromFile(frame.StoragePath);
                    using var page = engine.Process(pix);

                    var rawText = page.GetText()?.Trim() ?? string.Empty;
                    var confidence = Math.Clamp(page.GetMeanConfidence(), 0, 1);
                    var normalized = NormalizeCandidate(rawText, source.ExpectedTextType, out var candidateType, out var validationStatus, out var validationReasons);

                    return new CameraEvidenceOcrExtraction(
                        "local-tesseract",
                        typeof(TesseractEngine).Assembly.GetName().Version?.ToString() ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                        rawText,
                        normalized,
                        candidateType,
                        confidence,
                        validationStatus,
                        JsonSerializer.Serialize(validationReasons),
                        null);
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Camera evidence OCR failed for frame {FrameId}", frame.Id);
                return Failure("OcrFailed", ex.Message);
            }
        }

        private static CameraEvidenceOcrExtraction Failure(string status, string reason)
        {
            return new CameraEvidenceOcrExtraction(
                "local-tesseract",
                typeof(TesseractEngine).Assembly.GetName().Version?.ToString(),
                string.Empty,
                null,
                "unknown",
                0,
                status,
                JsonSerializer.Serialize(new[] { reason }),
                null);
        }

        private static string? NormalizeCandidate(
            string rawText,
            string expectedTextType,
            out string candidateType,
            out string validationStatus,
            out List<string> validationReasons)
        {
            validationReasons = new List<string>();
            candidateType = string.IsNullOrWhiteSpace(expectedTextType) || expectedTextType == "unknown"
                ? "unknown"
                : expectedTextType.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(rawText))
            {
                validationStatus = "NoText";
                validationReasons.Add("No OCR text was detected.");
                return null;
            }

            var containerMatch = ContainerPattern.Match(rawText);
            if (containerMatch.Success)
            {
                candidateType = "container";
                var normalized = Regex.Replace(containerMatch.Value, @"[^A-Z0-9]", string.Empty, RegexOptions.IgnoreCase).ToUpperInvariant();
                if (ContainerNumberValidator.IsValidContainerNumber(normalized))
                {
                    validationStatus = "ValidContainerFormat";
                    validationReasons.Add("Candidate matches ISO-style container number format.");
                }
                else
                {
                    validationStatus = "ContainerFormatWarning";
                    validationReasons.Add("Candidate looks container-like but failed local format validation.");
                }

                return normalized;
            }

            var compact = WhitespacePattern.Replace(rawText.Trim(), " ");
            if (compact.Length > 500)
            {
                compact = compact[..500];
            }

            validationStatus = "Unvalidated";
            validationReasons.Add("No container-like text candidate was detected.");
            return compact.ToUpperInvariant();
        }
    }
}
