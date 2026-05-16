using System;
using System.Collections.Generic;
using System.IO;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.EagleA25
{
    /// <summary>
    /// Decoder for Eagle A25 RAP cargoimage files.
    /// Observed files are chunked: 8-byte RAP magic, DIMS chunk, XRAY chunk.
    /// XRAY is two little-endian 16-bit planes interleaved per pixel.
    /// </summary>
    public static class EagleA25CargoImageDecoder
    {
        private static readonly byte[] Magic = { 0xA9, 0x52, 0x41, 0x50, 0x0D, 0x0A, 0x1A, 0x0A };

        public readonly record struct DecodedCargoImage(
            int Width,
            int Height,
            ushort[] HighEnergy,
            ushort[] LowEnergy,
            IReadOnlyDictionary<string, string> Metadata);

        public static DecodedCargoImage Decode(byte[] blob)
        {
            if (blob == null)
                throw new InvalidDataException("Eagle A25 cargoimage blob is null");

            if (blob.Length < 40)
                throw new InvalidDataException($"Eagle A25 cargoimage is too small: {blob.Length} bytes");

            for (var i = 0; i < Magic.Length; i++)
            {
                if (blob[i] != Magic[i])
                {
                    throw new InvalidDataException(
                        $"Eagle A25 RAP magic mismatch: expected A9-52-41-50-0D-0A-1A-0A, got {Convert.ToHexString(blob.AsSpan(0, Math.Min(8, blob.Length)))}");
                }
            }

            int? rawWidth = null;
            int? rawHeight = null;
            int? bytesPerPixel = null;
            int? planeCount = null;
            int? nominalBitDepth = null;
            ReadOnlySpan<byte> xray = default;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ContainerFormat"] = "RAP",
            };

            var offset = Magic.Length;
            while (offset + 8 <= blob.Length)
            {
                var chunkType = System.Text.Encoding.ASCII.GetString(blob, offset, 4);
                var chunkLength = BitConverter.ToInt32(blob, offset + 4);
                if (chunkLength < 0 || offset + 8L + chunkLength > blob.Length)
                {
                    throw new InvalidDataException($"Eagle A25 chunk '{chunkType}' has invalid length {chunkLength} at offset {offset}");
                }

                var dataOffset = offset + 8;
                var data = blob.AsSpan(dataOffset, chunkLength);
                metadata[$"Chunk:{chunkType}"] = chunkLength.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (chunkType == "DIMS")
                {
                    if (chunkLength < 24)
                        throw new InvalidDataException($"Eagle A25 DIMS chunk too small: {chunkLength} bytes");

                    bytesPerPixel = BitConverter.ToInt32(blob, dataOffset);
                    rawWidth = BitConverter.ToInt32(blob, dataOffset + 4);
                    rawHeight = BitConverter.ToInt32(blob, dataOffset + 8);
                    planeCount = BitConverter.ToInt32(blob, dataOffset + 12);
                    nominalBitDepth = BitConverter.ToInt32(blob, dataOffset + 16);

                    metadata["RawWidth"] = rawWidth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    metadata["RawHeight"] = rawHeight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    metadata["BytesPerPixel"] = bytesPerPixel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    metadata["PlaneCount"] = planeCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    metadata["NominalBitDepth"] = nominalBitDepth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (chunkType == "XRAY")
                {
                    xray = data;
                }

                offset = dataOffset + chunkLength;
            }

            if (rawWidth is null || rawHeight is null)
                throw new InvalidDataException("Eagle A25 cargoimage is missing DIMS chunk");

            if (xray.IsEmpty)
                throw new InvalidDataException("Eagle A25 cargoimage is missing XRAY chunk");

            if (rawWidth <= 0 || rawHeight <= 0 || rawWidth > 16384 || rawHeight > 16384)
                throw new InvalidDataException($"Eagle A25 DIMS declares unsupported dimensions {rawWidth}x{rawHeight}");

            if (bytesPerPixel != 4)
                throw new InvalidDataException($"Eagle A25 XRAY bytes-per-pixel {bytesPerPixel} is unsupported; expected 4");

            var pixelCount = checked(rawWidth.Value * rawHeight.Value);
            var expectedXrayBytes = pixelCount * 4;
            if (xray.Length < expectedXrayBytes)
            {
                throw new InvalidDataException(
                    $"Eagle A25 XRAY payload truncated: DIMS declares {rawWidth}x{rawHeight}x4={expectedXrayBytes} bytes, chunk has {xray.Length}");
            }

            var rawHigh = new ushort[pixelCount];
            var rawLow = new ushort[pixelCount];
            for (var i = 0; i < pixelCount; i++)
            {
                var src = i * 4;
                rawHigh[i] = (ushort)(xray[src] | (xray[src + 1] << 8));
                rawLow[i] = (ushort)(xray[src + 2] | (xray[src + 3] << 8));
            }

            var displayWidth = rawHeight.Value;
            var displayHeight = rawWidth.Value;
            var high = TransposeToLandscape(rawHigh, rawWidth.Value, rawHeight.Value);
            var low = TransposeToLandscape(rawLow, rawWidth.Value, rawHeight.Value);

            return new DecodedCargoImage(displayWidth, displayHeight, high, low, metadata);
        }

        private static ushort[] TransposeToLandscape(ushort[] src, int rawWidth, int rawHeight)
        {
            var dst = new ushort[src.Length];
            for (var y = 0; y < rawHeight; y++)
            {
                var srcRow = y * rawWidth;
                for (var x = 0; x < rawWidth; x++)
                {
                    dst[x * rawHeight + y] = src[srcRow + x];
                }
            }

            return dst;
        }
    }
}
