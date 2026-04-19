using System.Text.Json;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public interface IImageAnnotationRenderer
    {
        Task<byte[]> RenderAnnotationsAsync(byte[] imageBytes, string? suspiciousAreasJson, string? tags);
    }

    public class ImageAnnotationRenderer : IImageAnnotationRenderer
    {
        private readonly ILogger<ImageAnnotationRenderer> _logger;
        private static readonly Rgba32 RedColor = new(220, 0, 0, 220);
        private static readonly Rgba32 RedFill = new(255, 0, 0, 20);
        private static readonly Rgba32 TagBg = new(200, 0, 0, 220);
        private static readonly Rgba32 White = new(255, 255, 255, 255);

        public ImageAnnotationRenderer(ILogger<ImageAnnotationRenderer> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> RenderAnnotationsAsync(byte[] imageBytes, string? suspiciousAreasJson, string? tags)
        {
            if (string.IsNullOrWhiteSpace(suspiciousAreasJson) && string.IsNullOrWhiteSpace(tags))
            {
                return imageBytes;
            }

            var rects = new List<AnnotationRect>();
            if (!string.IsNullOrWhiteSpace(suspiciousAreasJson))
            {
                try
                {
                    rects = JsonSerializer.Deserialize<List<AnnotationRect>>(suspiciousAreasJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to parse SuspiciousAreas JSON: {Error}", ex.Message);
                }
            }

            var tagList = new List<string>();
            if (!string.IsNullOrWhiteSpace(tags))
            {
                tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (!rects.Any() && !tagList.Any())
            {
                return imageBytes;
            }

            try
            {
                using var image = Image.Load<Rgba32>(imageBytes);

                foreach (var rect in rects)
                {
                    DrawRectangle(image, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, 3);
                }

                // Draw tag labels at top-left corner
                if (tagList.Any())
                {
                    var tagText = string.Join(" | ", tagList);
                    DrawTagBanner(image, tagText);
                }

                using var ms = new MemoryStream();
                await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 90 });
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render annotations onto image");
                return imageBytes;
            }
        }

        private static void DrawRectangle(Image<Rgba32> image, int x, int y, int w, int h, int thickness)
        {
            // Clamp to image bounds
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            w = Math.Min(w, image.Width - x);
            h = Math.Min(h, image.Height - y);
            if (w <= 0 || h <= 0) return;

            // Fill with semi-transparent red (using indexer — ImageSharp v3 compatible)
            for (int py = y; py < y + h && py < image.Height; py++)
            {
                for (int px = x; px < x + w && px < image.Width; px++)
                {
                    image[px, py] = BlendPixel(image[px, py], RedFill);
                }
            }

            // Draw border (top, bottom, left, right edges)
            for (int t = 0; t < thickness; t++)
            {
                // Top edge
                if (y + t < image.Height)
                    for (int px = x; px < x + w && px < image.Width; px++)
                        image[px, y + t] = RedColor;
                // Bottom edge
                if (y + h - 1 - t >= 0 && y + h - 1 - t < image.Height)
                    for (int px = x; px < x + w && px < image.Width; px++)
                        image[px, y + h - 1 - t] = RedColor;
                // Left edge
                if (x + t < image.Width)
                    for (int py = y; py < y + h && py < image.Height; py++)
                        image[x + t, py] = RedColor;
                // Right edge
                if (x + w - 1 - t >= 0 && x + w - 1 - t < image.Width)
                    for (int py = y; py < y + h && py < image.Height; py++)
                        image[x + w - 1 - t, py] = RedColor;
            }
        }

        private static void DrawTagBanner(Image<Rgba32> image, string text)
        {
            // Draw a tag banner at top-left: red background with white text area
            int bannerHeight = 28;
            int bannerWidth = Math.Min(text.Length * 9 + 20, image.Width);

            for (int py = 0; py < bannerHeight && py < image.Height; py++)
            {
                for (int px = 0; px < bannerWidth && px < image.Width; px++)
                {
                    image[px, py] = TagBg;
                }
            }

            // Note: ImageSharp without the Drawing package can't render text.
            // The banner serves as a visual indicator. For text, we'd need SixLabors.ImageSharp.Drawing.
            // The rectangles themselves are the primary visual annotation.
        }

        private static Rgba32 BlendPixel(Rgba32 background, Rgba32 overlay)
        {
            float alpha = overlay.A / 255f;
            return new Rgba32(
                (byte)(overlay.R * alpha + background.R * (1 - alpha)),
                (byte)(overlay.G * alpha + background.G * (1 - alpha)),
                (byte)(overlay.B * alpha + background.B * (1 - alpha)),
                255);
        }

        private class AnnotationRect
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string? Tag { get; set; }
            public string? CreatedBy { get; set; }
        }
    }
}
