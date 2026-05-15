using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.ImageProcessing.EagleA25;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters
{
    public sealed class EagleA25FormatAdapter : IScanFormatAdapter
    {
        public const string FormatTag = "eagle-a25-cargoimage-v1";

        private readonly ILogger<EagleA25FormatAdapter> _logger;

        public EagleA25FormatAdapter(ILogger<EagleA25FormatAdapter> logger)
        {
            _logger = logger;
        }

        public string SourceFormatTag => FormatTag;

        public Task<DecodedScan?> DecodeAsync(ScanSourceBytes bytes, CancellationToken ct = default)
        {
            if (!bytes.Blobs.TryGetValue("XRAY", out var blob) || blob == null || blob.Length < 40)
            {
                _logger.LogDebug("[EagleA25Adapter] {Container} has no usable XRAY cargoimage blob", bytes.ContainerNumber);
                return Task.FromResult<DecodedScan?>(null);
            }

            EagleA25CargoImageDecoder.DecodedCargoImage cargoImage;
            try
            {
                cargoImage = EagleA25CargoImageDecoder.Decode(blob);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EagleA25Adapter] Decode failed for {Container} (scan {ScanId})", bytes.ContainerNumber, bytes.ScanId);
                return Task.FromResult<DecodedScan?>(null);
            }

            DateTime? scanTime = null;
            if (bytes.Metadata.TryGetValue("ScanTime", out var ts) &&
                DateTime.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTs))
            {
                scanTime = parsedTs;
            }

            var sourceMetadata = new Dictionary<string, string>(bytes.Metadata, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in cargoImage.Metadata)
            {
                sourceMetadata[pair.Key] = pair.Value;
            }

            var channels = new List<EnergyChannel>
            {
                new() { Kind = EnergyKind.High, BitDepth = 16, Pixels = cargoImage.HighEnergy },
                new() { Kind = EnergyKind.Low, BitDepth = 16, Pixels = cargoImage.LowEnergy },
            };

            return Task.FromResult<DecodedScan?>(new DecodedScan
            {
                ScanId = bytes.ScanId,
                ContainerNumber = bytes.ContainerNumber,
                SourceFormatTag = FormatTag,
                ScanTime = scanTime,
                Width = cargoImage.Width,
                Height = cargoImage.Height,
                Orientation = ScanOrientation.LeftToRight,
                Channels = channels,
                Material = null,
                SourceMetadata = sourceMetadata,
            });
        }
    }
}
