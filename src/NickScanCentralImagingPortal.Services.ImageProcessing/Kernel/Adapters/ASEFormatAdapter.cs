using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters
{
    /// <summary>
    /// v2.11.0 — ASE wire-format → IR adapter. Handles both ASE variants the
    /// system encounters:
    /// <list type="bullet">
    ///   <item><c>lineDataType == 3</c> — tri-panel dual-energy. Splits the
    ///   single blob into three panels via <see cref="AseTriPanelDecoder"/>,
    ///   emits 2 channels + material. Same structural shape as FS6000.</item>
    ///   <item><c>lineDataType == 2</c> — single-view. One channel only.
    ///   No material layer. The IR's <see cref="DecodedScan.Channels"/>
    ///   has exactly one entry; <see cref="DecodedScan.Material"/> is null.</item>
    /// </list>
    ///
    /// The kernel doesn't care which variant the ASE scan is — it dispatches
    /// on <see cref="DecodedScan.IsDualEnergy"/> / channel count, and every
    /// operation works structurally.
    /// </summary>
    public sealed class ASEFormatAdapter : IScanFormatAdapter
    {
        public const string FormatTag = "ase-v1";

        private readonly ILogger<ASEFormatAdapter> _logger;

        public ASEFormatAdapter(ILogger<ASEFormatAdapter> logger)
        {
            _logger = logger;
        }

        public string SourceFormatTag => FormatTag;

        public Task<DecodedScan?> DecodeAsync(ScanSourceBytes bytes, CancellationToken ct = default)
        {
            if (!bytes.Blobs.TryGetValue("ScanImage", out var blob) || blob == null || blob.Length < 16)
            {
                _logger.LogDebug("[ASEAdapter] {Container} has no usable ScanImage blob", bytes.ContainerNumber);
                return Task.FromResult<DecodedScan?>(null);
            }

            AseFormatDecoder.DecodedAse ase;
            try
            {
                ase = AseFormatDecoder.Decode(blob);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASEAdapter] Decode failed for {Container}", bytes.ContainerNumber);
                return Task.FromResult<DecodedScan?>(null);
            }

            DateTime? scanTime = null;
            if (bytes.Metadata.TryGetValue("ScanTime", out var ts) &&
                DateTime.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTs))
            {
                scanTime = parsedTs;
            }

            if (ase.IsMultiPanel)
            {
                FS6000.FS6000FormatDecoder.DecodedFs6000 tri;
                try
                {
                    tri = AseTriPanelDecoder.SplitToDualEnergyShape(ase, scanTime);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ASEAdapter] Tri-panel split failed for {Container}", bytes.ContainerNumber);
                    return Task.FromResult<DecodedScan?>(null);
                }

                var channels = new List<EnergyChannel>
                {
                    new EnergyChannel { Kind = EnergyKind.High, BitDepth = 16, Pixels = tri.High },
                    new EnergyChannel { Kind = EnergyKind.Low,  BitDepth = 16, Pixels = tri.Low  },
                };
                var material = new MaterialClassification
                {
                    Classes  = tri.Material,
                    // ASE tri-panel scans carry a post-SplitToDualEnergyShape
                    // material byte that's been re-scaled to 0..255 from a
                    // 16-bit sparse source. The FS6000 taxonomy's class-band
                    // boundaries (the 41/121 cutoffs for organic/metal) work
                    // here because the re-scale preserves relative ordering.
                    Taxonomy = FS6000FormatAdapter.Fs6000Taxonomy,
                };
                return Task.FromResult<DecodedScan?>(new DecodedScan
                {
                    ScanId          = bytes.ScanId,
                    ContainerNumber = bytes.ContainerNumber,
                    SourceFormatTag = "ase-tri-panel",
                    ScanTime        = scanTime,
                    Width           = tri.Width,
                    Height          = tri.Height,
                    Orientation     = ScanOrientation.LeftToRight,
                    Channels        = channels,
                    Material        = material,
                    SourceMetadata  = bytes.Metadata,
                });
            }
            else
            {
                // Single-view: one channel only.
                var channels = new List<EnergyChannel>
                {
                    new EnergyChannel { Kind = EnergyKind.Single, BitDepth = 16, Pixels = ase.Pixels },
                };
                return Task.FromResult<DecodedScan?>(new DecodedScan
                {
                    ScanId          = bytes.ScanId,
                    ContainerNumber = bytes.ContainerNumber,
                    SourceFormatTag = "ase-single-view",
                    ScanTime        = scanTime,
                    Width           = ase.Width,
                    Height          = ase.Height,
                    Orientation     = ScanOrientation.LeftToRight,
                    Channels        = channels,
                    Material        = null,
                    SourceMetadata  = bytes.Metadata,
                });
            }
        }
    }
}
