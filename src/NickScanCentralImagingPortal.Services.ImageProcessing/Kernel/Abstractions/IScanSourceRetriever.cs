using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions
{
    /// <summary>
    /// v2.11.0 — reads the stored blobs for a container from whichever
    /// storage backend a given scanner's ingest pipeline writes to.
    /// Separate from <see cref="IScanFormatAdapter"/> because decoding is
    /// pure/stateless while retrieval has to talk to the database.
    ///
    /// Implementations:
    /// <list type="bullet">
    ///   <item><c>FS6000SourceRetriever</c> — reads <c>fs6000images</c></item>
    ///   <item><c>ASESourceRetriever</c> — reads <c>asescans</c></item>
    ///   <item>(future) queue-based / object-store retrievers</item>
    /// </list>
    /// </summary>
    public interface IScanSourceRetriever
    {
        /// <summary>Which scanner family this retriever knows how to read.
        /// <see cref="ScanRouter"/> picks retrievers by this.</summary>
        ScannerType ScannerType { get; }

        /// <summary>Load raw bytes for a container. Returns null when no
        /// scan exists at all. Returns a non-null <see cref="ScanSourceBytes"/>
        /// with possibly-partial <c>Blobs</c> when the scan exists but some
        /// channels are missing — the adapter decides what can still be
        /// decoded from a partial bundle.</summary>
        Task<ScanSourceBytes?> LoadAsync(string containerNumber, CancellationToken ct = default);

        /// <summary>Cheaply report which named blobs exist for this container,
        /// without actually loading them. Used by the capabilities endpoint to
        /// tell the UI "scan exists but missing Material" vs "no scan".
        /// Returns null when no scan at all.</summary>
        Task<BlobInventory?> InventoryAsync(string containerNumber, CancellationToken ct = default);
    }

    /// <summary>
    /// Result of a cheap blob-existence check. Used for capability hints
    /// without paying the full blob-load cost.
    /// </summary>
    public sealed class BlobInventory
    {
        public required string ScanId { get; init; }
        public required string SourceFormatTag { get; init; }
        public required System.Collections.Generic.IReadOnlyCollection<string> PresentBlobNames { get; init; }
        public required System.Collections.Generic.IReadOnlyCollection<string> MissingBlobNames { get; init; }

        public bool IsComplete => MissingBlobNames.Count == 0;
    }
}
