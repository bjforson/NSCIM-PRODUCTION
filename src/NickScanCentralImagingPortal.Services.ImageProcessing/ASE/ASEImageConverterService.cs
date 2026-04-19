using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.ASE
{
    public interface IASEImageConverterService
    {
        Task<AseImageConversionResult> ConvertAseImageToJpegAsync(byte[] proprietaryImageData);
    }

    /// <summary>
    /// Decodes ASE proprietary image blobs using the pure-C# decoder
    /// (<see cref="AseFormatDecoder"/> + <see cref="AsePercentileRenderer"/>).
    ///
    /// Historical note: the vendor <c>Ase.Image.dll</c> path was removed in 2.2.0
    /// after FallbackOnly ran successfully in production. The <c>AseDecoderMode</c>
    /// enum and its surrounding options class are retained only for appsettings.json
    /// backward compatibility — the service now always uses the pure-C# path.
    /// </summary>
    public class ASEImageConverterService : IASEImageConverterService
    {
        private readonly ILogger<ASEImageConverterService> _logger;

        // Decoder provenance string value — kept for ImageCache.ProcessingPipeline continuity.
        public const string DecoderFallback = "Fallback";

        public ASEImageConverterService(ILogger<ASEImageConverterService> logger)
        {
            _logger = logger;
        }

        public Task<AseImageConversionResult> ConvertAseImageToJpegAsync(byte[] proprietaryImageData)
        {
            if (proprietaryImageData == null || proprietaryImageData.Length == 0)
            {
                return Task.FromResult(new AseImageConversionResult
                {
                    Success = false,
                    ErrorMessage = "No image data provided"
                });
            }

            return Task.FromResult(RunFallback(proprietaryImageData));
        }

        private AseImageConversionResult RunFallback(byte[] proprietaryImageData)
        {
            try
            {
                var decoded = AseFormatDecoder.Decode(proprietaryImageData);
                using var bmp = AsePercentileRenderer.BuildBitmap(decoded);
                var jpegBytes = EncodeBitmapToJpeg(bmp, out int width, out int height);

                _logger.LogInformation(
                    "ASE decoded {Bytes} bytes -> {W}x{H} (ldt={Ldt})",
                    proprietaryImageData.Length, width, height, decoded.LineDataType);

                return new AseImageConversionResult
                {
                    Success = true,
                    ImageData = jpegBytes,
                    Width = width,
                    Height = height,
                    DecoderUsed = DecoderFallback,
                    Metadata = new ImageMetadata
                    {
                        Width = width,
                        Height = height,
                        FileSizeBytes = jpegBytes.Length,
                        ImageFormat = "JPEG",
                        ProcessingPipeline = "ASE-Proprietary-to-JPEG-Fallback",
                        Quality = "High"
                    }
                };
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "ASE decoder rejected blob (invalid format): {Err}", ex.Message);
                return new AseImageConversionResult
                {
                    Success = false,
                    ErrorMessage = $"ASE decode failed: {ex.Message}",
                    DecoderUsed = DecoderFallback
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ASE decoder unexpected exception");
                return new AseImageConversionResult
                {
                    Success = false,
                    ErrorMessage = $"ASE decode exception: {ex.Message}",
                    DecoderUsed = DecoderFallback
                };
            }
        }

        /// <summary>
        /// Encode an in-memory Bitmap as JPEG quality 95, returning the byte array.
        /// </summary>
        private byte[] EncodeBitmapToJpeg(Bitmap source, out int width, out int height)
        {
            width = source.Width;
            height = source.Height;

            using var jpegStream = new MemoryStream();
            var jpegEncoder = GetJpegEncoder();
            var encoderParams = GetHighQualityEncoderParams();
            source.Save(jpegStream, jpegEncoder, encoderParams);
            return jpegStream.ToArray();
        }

        private ImageCodecInfo GetJpegEncoder()
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            return Array.Find(codecs, codec => codec.FormatID == ImageFormat.Jpeg.Guid)
                ?? throw new InvalidOperationException("JPEG encoder not found");
        }

        private EncoderParameters GetHighQualityEncoderParams()
        {
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
            return encoderParams;
        }
    }

    public class AseImageConversionResult
    {
        public bool Success { get; set; }
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public string ErrorMessage { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public ImageMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Which decoder produced this result. Always "Fallback" since 2.2.0
        /// (vendor DLL path removed). Propagated into <c>ImageCache.ProcessingPipeline</c>
        /// by <see cref="ASEImagePipeline"/>.
        /// </summary>
        public string DecoderUsed { get; set; } = string.Empty;
    }
}
