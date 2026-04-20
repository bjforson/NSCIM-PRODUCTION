using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions
{
    /// <summary>
    /// v2.11.0 — the ONLY place in the codebase where scanner-specific code
    /// lives for the render/analysis lifecycle. Takes raw vendor bytes and
    /// produces a <see cref="DecodedScan"/> — the IR the kernel operates on.
    ///
    /// Implementations:
    /// <list type="bullet">
    ///   <item><see cref="FS6000"/> — parses HE/LE/Material .img blobs</item>
    ///   <item><see cref="ASE"/> — parses the single ASE blob (both tri-panel and single-view)</item>
    ///   <item>(future) HeimannAdapter, NuctechMxAdapter, etc. — only things a new scanner needs.</item>
    /// </list>
    ///
    /// Adapters are stateless / pure: same bytes in → same IR out. They don't
    /// touch the database, don't cache, don't call external services. That
    /// makes them trivial to unit-test.
    /// </summary>
    public interface IScanFormatAdapter
    {
        /// <summary>Short tag naming the wire format this adapter handles.
        /// Must match <see cref="ScanSourceBytes.SourceFormatTag"/> on the
        /// input bundle. See <see cref="DecodedScan.SourceFormatTag"/>.</summary>
        string SourceFormatTag { get; }

        /// <summary>Parse the raw bytes into the IR. Returns null on
        /// unrecoverable parse failure (adapter logs; caller treats as "no
        /// renderable scan").</summary>
        Task<DecodedScan?> DecodeAsync(ScanSourceBytes bytes, CancellationToken ct = default);
    }

    /// <summary>
    /// Bundle of bytes that <see cref="IScanFormatAdapter.DecodeAsync"/>
    /// parses. Multi-blob scanners (FS6000 has HE+LE+Material) populate
    /// several named entries; single-blob scanners (ASE) populate one.
    /// </summary>
    public sealed class ScanSourceBytes
    {
        public required string ScanId { get; init; }
        public required string ContainerNumber { get; init; }

        /// <summary>Must match an adapter's <see cref="IScanFormatAdapter.SourceFormatTag"/>.</summary>
        public required string SourceFormatTag { get; init; }

        /// <summary>Named blobs. Keys are adapter-defined: FS6000 uses
        /// <c>"HighEnergy"</c> / <c>"LowEnergy"</c> / <c>"Material"</c> (and
        /// optionally <c>"Main"</c> for the vendor reference JPEG); ASE uses
        /// <c>"ScanImage"</c>.</summary>
        public required IReadOnlyDictionary<string, byte[]> Blobs { get; init; }

        /// <summary>Scanner-provided metadata (timestamp, scanner ID, firmware
        /// version, etc.). Passed through to <see cref="DecodedScan.SourceMetadata"/>.</summary>
        public IReadOnlyDictionary<string, string> Metadata { get; init; }
            = new Dictionary<string, string>();
    }
}
