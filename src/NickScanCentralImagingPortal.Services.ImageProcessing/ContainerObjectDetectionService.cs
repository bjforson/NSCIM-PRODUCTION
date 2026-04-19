using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;
using OpenCvSharp;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// Object detection service for identifying containers, vehicles, and anomalies in scan images
    /// Uses OpenCvSharp for computer vision operations
    /// </summary>
    public class ContainerObjectDetectionService : IContainerObjectDetectionService
    {
        private readonly ILogger<ContainerObjectDetectionService> _logger;
        private readonly IImageProcessingService _imageProcessingService;

        public ContainerObjectDetectionService(
            ILogger<ContainerObjectDetectionService> logger,
            IImageProcessingService imageProcessingService)
        {
            _logger = logger;
            _imageProcessingService = imageProcessingService;
        }

        /// <summary>
        /// Detect objects in container scan image
        /// </summary>
        public async Task<ObjectDetectionResult> DetectObjectsAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Detecting objects in image for container: {ContainerNumber}", containerNumber);

                // Get image as base64
                var base64Image = await _imageProcessingService.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                    return new ObjectDetectionResult
                    {
                        Success = false,
                        ErrorMessage = "No image available for object detection"
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

                // Decode image
                using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                if (src.Empty())
                {
                    _logger.LogWarning("Failed to decode image for object detection");
                    return new ObjectDetectionResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to decode image"
                    };
                }

                // Detect containers
                var containerRegions = DetectContainerRegions(src);

                // Detect vehicles (with improved multi-pass detection)
                var vehicleRegions = DetectVehicleRegions(src);

                // Detect anomalies
                var anomalies = DetectAnomalies(src);

                _logger.LogInformation("Object detection completed for {ContainerNumber}. Containers: {Containers}, Vehicles: {Vehicles}, Anomalies: {Anomalies}",
                    containerNumber, containerRegions.Count, vehicleRegions.Count, anomalies.Count);

                return new ObjectDetectionResult
                {
                    Success = true,
                    ContainerRegions = containerRegions,
                    VehicleRegions = vehicleRegions,
                    Anomalies = anomalies,
                    HasAnomalies = anomalies.Any()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting objects for container: {ContainerNumber}", containerNumber);
                return new ObjectDetectionResult
                {
                    Success = false,
                    ErrorMessage = $"Object detection failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Detect container regions using edge detection and contour analysis
        /// </summary>
        private List<BoundingBox> DetectContainerRegions(Mat image)
        {
            var containerBoxes = new List<BoundingBox>();

            try
            {
                // Convert to grayscale
                using var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                // Apply Gaussian blur to reduce noise
                using var blurred = new Mat();
                Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

                // Edge detection using Canny
                using var edges = new Mat();
                Cv2.Canny(blurred, edges, 50, 150);

                // ✅ PHASE 1 IMPROVEMENT: Use Tree mode to detect nested/overlapping objects
                Cv2.FindContours(edges, out var contours, out var hierarchy,
                    RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                // Filter contours for container-like shapes
                foreach (var contour in contours)
                {
                    var rect = Cv2.BoundingRect(contour);
                    var area = Cv2.ContourArea(contour);
                    var aspectRatio = (double)rect.Width / rect.Height;

                    // Filter by size and aspect ratio (containers are typically rectangular)
                    // Minimum area: 10000 pixels (adjust based on image resolution)
                    // Aspect ratio: between 0.5 and 2.0 (rectangular shapes)
                    if (area > 10000 && rect.Width > 200 && rect.Height > 200 &&
                        aspectRatio >= 0.5 && aspectRatio <= 2.0)
                    {
                        containerBoxes.Add(new BoundingBox
                        {
                            X = rect.X,
                            Y = rect.Y,
                            Width = rect.Width,
                            Height = rect.Height,
                            Type = "Container",
                            Confidence = CalculateConfidence(area, aspectRatio)
                        });
                    }
                }

                _logger.LogDebug("Detected {Count} container regions", containerBoxes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting container regions");
            }

            return containerBoxes;
        }

        /// <summary>
        /// Detect vehicle regions using improved multi-pass detection with better preprocessing
        /// Phase 1 improvements: Multi-pass detection, Tree mode, CLAHE, adaptive thresholds, NMS
        /// </summary>
        private List<BoundingBox> DetectVehicleRegions(Mat image)
        {
            var containerBoxes = DetectContainerRegions(image); // Get containers first
            var allVehicles = new List<BoundingBox>();

            try
            {
                // ✅ PHASE 1 IMPROVEMENT: Better preprocessing
                using var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                // ✅ IMPROVEMENT: CLAHE (Contrast Limited Adaptive Histogram Equalization) for better contrast
                using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
                using var enhanced = new Mat();
                clahe.Apply(gray, enhanced);

                // ✅ IMPROVEMENT: Bilateral filter (preserves edges while reducing noise)
                using var filtered = new Mat();
                Cv2.BilateralFilter(enhanced, filtered, 9, 75, 75);

                // ✅ IMPROVEMENT: Adaptive Canny thresholds (instead of fixed 50, 150)
                using var mean = new Mat();
                using var stddev = new Mat();
                Cv2.MeanStdDev(filtered, mean, stddev);
                var meanValue = mean.Get<double>(0);
                var stdValue = stddev.Get<double>(0);
                var lowThreshold = Math.Max(30, meanValue - stdValue);
                var highThreshold = Math.Min(200, meanValue + stdValue);

                using var edges = new Mat();
                Cv2.Canny(filtered, edges, lowThreshold, highThreshold);

                // ✅ IMPROVEMENT: Use Tree mode to detect nested/overlapping objects
                Cv2.FindContours(edges, out var contours, out var hierarchy,
                    RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                // ✅ PHASE 1 IMPROVEMENT: Multi-pass detection with different thresholds
                // Pass 1: Large vehicles (original thresholds)
                var largeVehicles = DetectVehiclesWithParams(contours, containerBoxes,
                    minArea: 5000, minWidth: 150, minHeight: 150);

                // Pass 2: Medium vehicles (lowered thresholds)
                var mediumVehicles = DetectVehiclesWithParams(contours, containerBoxes,
                    minArea: 3000, minWidth: 100, minHeight: 100);

                // Pass 3: Small vehicles (even lower thresholds)
                var smallVehicles = DetectVehiclesWithParams(contours, containerBoxes,
                    minArea: 1500, minWidth: 75, minHeight: 75);

                // Combine all detections
                allVehicles.AddRange(largeVehicles);
                allVehicles.AddRange(mediumVehicles);
                allVehicles.AddRange(smallVehicles);

                // ✅ PHASE 1 IMPROVEMENT: Non-Maximum Suppression to remove duplicates/overlaps
                var filteredVehicles = NonMaximumSuppression(allVehicles, overlapThreshold: 0.3f);

                _logger.LogDebug("Detected {Count} vehicle regions (before NMS: {BeforeCount})",
                    filteredVehicles.Count, allVehicles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting vehicle regions");
            }

            return allVehicles.Count > 0 ? NonMaximumSuppression(allVehicles, 0.3f) : new List<BoundingBox>();
        }

        /// <summary>
        /// Detect vehicles with specific parameters (used in multi-pass detection)
        /// </summary>
        private List<BoundingBox> DetectVehiclesWithParams(
            OpenCvSharp.Point[][] contours,
            List<BoundingBox> containerBoxes,
            int minArea,
            int minWidth,
            int minHeight)
        {
            var vehicles = new List<BoundingBox>();

            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                var area = Cv2.ContourArea(contour);
                var aspectRatio = (double)rect.Width / rect.Height;

                // Filter by size and aspect ratio
                if (area > minArea && rect.Width > minWidth && rect.Height > minHeight &&
                    aspectRatio >= 0.3 && aspectRatio <= 3.0)
                {
                    // ✅ IMPROVEMENT: Better container overlap check (using IoU instead of simple distance)
                    var isContainer = containerBoxes.Any(c =>
                        CalculateIoU(rect.X, rect.Y, rect.Width, rect.Height,
                                    c.X, c.Y, c.Width, c.Height) > 0.1f);

                    if (!isContainer)
                    {
                        vehicles.Add(new BoundingBox
                        {
                            X = rect.X,
                            Y = rect.Y,
                            Width = rect.Width,
                            Height = rect.Height,
                            Type = "Vehicle",
                            Confidence = CalculateConfidence(area, aspectRatio)
                        });
                    }
                }
            }

            return vehicles;
        }

        /// <summary>
        /// Non-Maximum Suppression: Remove overlapping detections, keeping the one with highest confidence
        /// </summary>
        private List<BoundingBox> NonMaximumSuppression(List<BoundingBox> boxes, float overlapThreshold = 0.3f)
        {
            if (boxes == null || boxes.Count == 0)
                return new List<BoundingBox>();

            // Sort by confidence (or area if confidence is 0)
            var sortedBoxes = boxes.OrderByDescending(b =>
                b.Confidence > 0 ? b.Confidence : (b.Width * b.Height)).ToList();

            var filtered = new List<BoundingBox>();
            var suppressed = new bool[sortedBoxes.Count];

            for (int i = 0; i < sortedBoxes.Count; i++)
            {
                if (suppressed[i]) continue;

                filtered.Add(sortedBoxes[i]);

                // Suppress overlapping boxes
                for (int j = i + 1; j < sortedBoxes.Count; j++)
                {
                    if (suppressed[j]) continue;

                    var iou = CalculateIoU(
                        sortedBoxes[i].X, sortedBoxes[i].Y, sortedBoxes[i].Width, sortedBoxes[i].Height,
                        sortedBoxes[j].X, sortedBoxes[j].Y, sortedBoxes[j].Width, sortedBoxes[j].Height);

                    if (iou > overlapThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            return filtered;
        }

        /// <summary>
        /// Calculate Intersection over Union (IoU) between two bounding boxes
        /// </summary>
        private float CalculateIoU(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2)
        {
            // Calculate intersection rectangle
            var xLeft = Math.Max(x1, x2);
            var yTop = Math.Max(y1, y2);
            var xRight = Math.Min(x1 + w1, x2 + w2);
            var yBottom = Math.Min(y1 + h1, y2 + h2);

            if (xRight < xLeft || yBottom < yTop)
                return 0f;

            var intersection = (xRight - xLeft) * (yBottom - yTop);
            var area1 = w1 * h1;
            var area2 = w2 * h2;
            var union = area1 + area2 - intersection;

            return union > 0 ? intersection / union : 0f;
        }

        /// <summary>
        /// Detect anomalies (unusual shapes, densities, patterns)
        /// </summary>
        private List<BoundingBox> DetectAnomalies(Mat image)
        {
            var anomalies = new List<BoundingBox>();

            try
            {
                // Convert to grayscale
                using var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                // Calculate local variance (anomalies have high variance)
                using var mean = new Mat();
                using var stddev = new Mat();
                Cv2.MeanStdDev(gray, mean, stddev);

                var meanValue = mean.Get<double>(0);
                var stdValue = stddev.Get<double>(0);

                // Threshold for anomaly detection (high standard deviation = anomaly)
                var anomalyThreshold = meanValue + (2 * stdValue);

                // Create binary mask for high-variance regions
                using var threshold = new Mat();
                Cv2.Threshold(gray, threshold, anomalyThreshold, 255, ThresholdTypes.Binary);

                // ✅ PHASE 1 IMPROVEMENT: Use Tree mode to detect nested/overlapping objects
                Cv2.FindContours(threshold, out var contours, out var hierarchy,
                    RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                foreach (var contour in contours)
                {
                    var rect = Cv2.BoundingRect(contour);
                    var area = Cv2.ContourArea(contour);

                    // Anomalies are typically smaller, irregular shapes
                    if (area > 500 && area < 50000 && rect.Width > 50 && rect.Height > 50)
                    {
                        anomalies.Add(new BoundingBox
                        {
                            X = rect.X,
                            Y = rect.Y,
                            Width = rect.Width,
                            Height = rect.Height,
                            Type = "Anomaly",
                            Confidence = 0.7f // Lower confidence for anomalies
                        });
                    }
                }

                _logger.LogDebug("Detected {Count} anomaly regions", anomalies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting anomalies");
            }

            return anomalies;
        }

        /// <summary>
        /// Calculate confidence score based on area and aspect ratio
        /// </summary>
        private float CalculateConfidence(double area, double aspectRatio)
        {
            // Normalize area (assuming max area of 1000000 pixels)
            var areaScore = Math.Min(1.0, area / 1000000.0);

            // Aspect ratio score (prefer values close to 1.0 for containers)
            var aspectScore = 1.0 - Math.Abs(1.0 - aspectRatio) * 0.5;
            aspectScore = Math.Max(0.0, aspectScore);

            // Combined confidence
            return (float)((areaScore * 0.6) + (aspectScore * 0.4));
        }
    }

    /// <summary>
    /// Object detection result model
    /// </summary>
}

