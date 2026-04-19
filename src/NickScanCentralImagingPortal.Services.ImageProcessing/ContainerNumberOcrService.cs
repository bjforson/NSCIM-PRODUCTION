using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;
using OpenCvSharp;
using Tesseract;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// OCR service for extracting container numbers from images
    /// Uses Tesseract.NET with OpenCvSharp preprocessing for better accuracy
    /// </summary>
    public class ContainerNumberOcrService : IContainerNumberOcrService
    {
        private readonly ILogger<ContainerNumberOcrService> _logger;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly string _tessdataPath;
        private const string CONTAINER_NUMBER_PATTERN = @"([A-Z]{4})(\d{7})"; // 4 letters + 7 digits

        public ContainerNumberOcrService(
            ILogger<ContainerNumberOcrService> logger,
            IImageProcessingService imageProcessingService)
        {
            _logger = logger;
            _imageProcessingService = imageProcessingService;

            // Set tessdata path (default to current directory or configurable)
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _tessdataPath = Path.Combine(baseDirectory, "tessdata");

            // Create tessdata directory if it doesn't exist
            if (!Directory.Exists(_tessdataPath))
            {
                Directory.CreateDirectory(_tessdataPath);
                _logger.LogWarning("Tessdata directory does not exist: {TessdataPath}. Please ensure Tesseract language data files are available.", _tessdataPath);
            }
        }

        /// <summary>
        /// Extract container number from image
        /// </summary>
        public async Task<OcrResult> ExtractContainerNumberAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Extracting container number via OCR for: {ContainerNumber}", containerNumber);

                // Get image as base64
                var base64Image = await _imageProcessingService.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                    return new OcrResult
                    {
                        Success = false,
                        ErrorMessage = "No image available for OCR processing"
                    };
                }

                // ✅ FIX: Strip data URI prefix if present (e.g., "data:image/jpeg;base64,")
                var base64Data = base64Image;
                if (base64Image.Contains(","))
                {
                    base64Data = base64Image.Substring(base64Image.IndexOf(",") + 1);
                }

                // Convert base64 to bytes
                var imageBytes = Convert.FromBase64String(base64Data);

                // Preprocess image for better OCR
                var preprocessedImage = PreprocessImageForOcr(imageBytes);

                // Run OCR
                var ocrText = await RunOcrAsync(preprocessedImage);

                // Extract container number pattern
                var extractedContainerNumber = ExtractContainerNumberFromText(ocrText);

                // Validate against expected container number
                var matchesExpected = !string.IsNullOrEmpty(extractedContainerNumber) &&
                                     extractedContainerNumber.Equals(containerNumber, StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("OCR completed for {ContainerNumber}. Detected: {Detected}, Matches: {Matches}",
                    containerNumber, extractedContainerNumber ?? "None", matchesExpected);

                return new OcrResult
                {
                    Success = true,
                    DetectedText = ocrText,
                    ContainerNumber = extractedContainerNumber,
                    MatchesExpected = matchesExpected,
                    ExpectedContainerNumber = containerNumber,
                    Confidence = 0.85f // Placeholder - Tesseract provides confidence per word
                };
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError(fnfEx, "Tesseract language data file missing for container: {ContainerNumber}", containerNumber);
                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = $"OCR not available: Tesseract language data file (eng.traineddata) not found. Please download from https://github.com/tesseract-ocr/tessdata and place in {_tessdataPath}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting container number via OCR for: {ContainerNumber}", containerNumber);
                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = $"OCR processing failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Preprocess image for better OCR accuracy
        /// </summary>
        private byte[] PreprocessImageForOcr(byte[] imageBytes)
        {
            try
            {
                // Decode image
                using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                if (src.Empty())
                {
                    _logger.LogWarning("Failed to decode image for OCR preprocessing");
                    return imageBytes; // Return original if decode fails
                }

                // Convert to grayscale
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

                // Apply Gaussian blur to reduce noise
                using var blurred = new Mat();
                Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);

                // Apply threshold (binary) for better text contrast
                using var threshold = new Mat();
                Cv2.Threshold(blurred, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                // Enhance contrast with CLAHE (Contrast Limited Adaptive Histogram Equalization)
                using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
                using var enhanced = new Mat();
                clahe.Apply(threshold, enhanced);

                // Encode back to JPEG
                var encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, 95 };
                Cv2.ImEncode(".jpg", enhanced, out var result, encodeParams);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error preprocessing image for OCR, using original image");
                return imageBytes; // Return original if preprocessing fails
            }
        }

        /// <summary>
        /// Run OCR on preprocessed image
        /// </summary>
        private async Task<string> RunOcrAsync(byte[] imageBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // ✅ FIX: Check if tessdata file exists before initializing TesseractEngine
                    var tessdataFile = Path.Combine(_tessdataPath, "eng.traineddata");
                    if (!File.Exists(tessdataFile))
                    {
                        _logger.LogError("Tesseract language data file not found: {TessdataFile}. Please download eng.traineddata from https://github.com/tesseract-ocr/tessdata and place it in {TessdataPath}",
                            tessdataFile, _tessdataPath);
                        throw new FileNotFoundException($"Tesseract language data file not found: {tessdataFile}. Please download eng.traineddata from https://github.com/tesseract-ocr/tessdata", tessdataFile);
                    }

                    using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                    using var pix = Pix.LoadFromMemory(imageBytes);
                    using var page = engine.Process(pix);

                    var text = page.GetText();
                    return text?.Trim() ?? string.Empty;
                }
                catch (FileNotFoundException)
                {
                    // Re-throw file not found exceptions with clear message
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error running OCR. Tessdata path: {TessdataPath}", _tessdataPath);
                    throw;
                }
            });
        }

        /// <summary>
        /// Extract container number pattern from OCR text
        /// Pattern: 4 uppercase letters followed by 7 digits (e.g., ABCD1234567)
        /// </summary>
        private string? ExtractContainerNumberFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Try to find container number pattern
            var match = Regex.Match(text, CONTAINER_NUMBER_PATTERN, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Format as uppercase: 4 letters + 7 digits
                return $"{match.Groups[1].Value.ToUpperInvariant()}{match.Groups[2].Value}";
            }

            // Try alternative patterns (with spaces, dashes, etc.)
            var patterns = new[]
            {
                @"([A-Z]{4})\s*(\d{7})",      // With space
                @"([A-Z]{4})-(\d{7})",        // With dash
                @"([A-Z]{4})\.(\d{7})",       // With dot
            };

            foreach (var pattern in patterns)
            {
                match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return $"{match.Groups[1].Value.ToUpperInvariant()}{match.Groups[2].Value}";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// OCR result model
    /// </summary>
}


