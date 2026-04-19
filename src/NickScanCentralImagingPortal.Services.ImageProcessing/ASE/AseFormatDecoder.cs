using System;
using System.IO;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.ASE
{
    /// <summary>
    /// Pure-C# parser for ASE scanner transmission-image blobs.
    /// No dependency on the vendor <c>Ase.Image.dll</c>.
    ///
    /// Format (reverse-engineered from 10 production samples; see
    /// <c>services/image-splitter/tools/ase_format_research/FINDINGS.md</c>
    /// and the reference Python implementation at
    /// <c>services/image-splitter/inspector/decoders/ase.py</c>):
    ///
    /// <code>
    ///   offset  size  field
    ///   0       4     magic "IM\0\0"
    ///   4       2     width          u16 LE
    ///   6       2     height         u16 LE
    ///   8       2     lineDataType   u16 LE  (2 = DualEnergyBitmap, 3 = ParcelDualEnergyBitmap tri-panel)
    ///   10      6     reserved / bit-depth enum (observed 0x0000 0x0002 0x0000)
    ///   16      ...   width * height * 2 bytes of raw 16-bit grayscale pixels, little-endian
    ///   +~48          opaque trailer (usually zeros, not critical to decode)
    ///   +668          UTF-16 LE XML &lt;Metadata&gt;...&lt;/Metadata&gt;
    /// </code>
    ///
    /// The format supports compression/encryption via <c>CompEncryptHeader</c> on
    /// the vendor DLL side, but zero production samples use either. This decoder
    /// does NOT support compressed/encrypted blobs — it throws
    /// <see cref="InvalidDataException"/> with a message instructing the caller
    /// to keep the DLL path enabled for affected files.
    ///
    /// Endianness note: this implementation targets x64 Windows Server 2022
    /// (NSCIM's only host platform). <see cref="Buffer.BlockCopy"/> preserves
    /// the little-endian layout in the <see cref="ushort"/>[] output.
    /// </summary>
    public static class AseFormatDecoder
    {
        private const int HeaderSize = 16;
        private const int MagicLength = 4;

        // 'I' 'M' 0x00 0x00
        private static readonly byte[] Magic = { 0x49, 0x4D, 0x00, 0x00 };

        public const int LineDataTypeDualEnergyBitmap = 2;
        public const int LineDataTypeParcelDualEnergyBitmap = 3;

        /// <summary>
        /// Result of a successful decode — the raw pixel buffer in native
        /// <see cref="ushort"/>[] form plus the header dimensions.
        /// </summary>
        public readonly struct DecodedAse
        {
            public readonly ushort Width;
            public readonly ushort Height;
            public readonly ushort LineDataType;
            public readonly ushort[] Pixels;    // row-major, length = Width*Height

            public DecodedAse(ushort width, ushort height, ushort lineDataType, ushort[] pixels)
            {
                Width = width;
                Height = height;
                LineDataType = lineDataType;
                Pixels = pixels;
            }

            public bool IsMultiPanel => LineDataType == LineDataTypeParcelDualEnergyBitmap;
        }

        /// <summary>
        /// Parse an ASE blob into a <see cref="DecodedAse"/> value.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Thrown when the blob is too small, has the wrong magic, has out-of-range
        /// dimensions, or is truncated (possibly because the file uses the unsupported
        /// <c>CompEncryptHeader</c> compression/encryption variant).
        /// </exception>
        public static DecodedAse Decode(byte[] blob)
        {
            if (blob == null)
                throw new InvalidDataException("ASE blob is null");
            if (blob.Length < HeaderSize)
                throw new InvalidDataException($"ASE blob too small for header: {blob.Length} bytes (need at least {HeaderSize})");

            for (int i = 0; i < MagicLength; i++)
            {
                if (blob[i] != Magic[i])
                {
                    throw new InvalidDataException(
                        $"ASE magic mismatch: expected 'IM\\0\\0' (0x494D0000), got 0x{blob[0]:X2}{blob[1]:X2}{blob[2]:X2}{blob[3]:X2}. " +
                        "This is not a valid ASE transmission image blob.");
                }
            }

            ushort width = BitConverter.ToUInt16(blob, 4);
            ushort height = BitConverter.ToUInt16(blob, 6);
            ushort lineDataType = BitConverter.ToUInt16(blob, 8);

            if (width == 0 || height == 0)
                throw new InvalidDataException($"ASE dimensions are zero: {width}x{height}");
            if (width > 8192 || height > 16384)
                throw new InvalidDataException($"ASE dimensions out of supported range: {width}x{height} (max 8192x16384)");

            long pixelCount = (long)width * height;
            long pixelBytes = pixelCount * 2;
            long required = HeaderSize + pixelBytes;
            if (required > blob.Length)
            {
                throw new InvalidDataException(
                    $"ASE payload truncated: header declares {width}x{height} ({pixelBytes} pixel bytes) " +
                    $"but blob is only {blob.Length} bytes. " +
                    "If this file uses the vendor CompEncryptHeader compression/encryption variant, the pure-C# " +
                    "decoder does not support it — keep the vendor Ase.Image.dll path enabled for affected files.");
            }

            // Buffer.BlockCopy preserves the little-endian byte order into the
            // ushort[] target on x64 Windows. On x64 machines (our target),
            // a ushort[] IS a packed little-endian uint16 buffer in memory.
            var pixels = new ushort[pixelCount];
            Buffer.BlockCopy(blob, HeaderSize, pixels, 0, (int)pixelBytes);

            return new DecodedAse(width, height, lineDataType, pixels);
        }
    }
}
