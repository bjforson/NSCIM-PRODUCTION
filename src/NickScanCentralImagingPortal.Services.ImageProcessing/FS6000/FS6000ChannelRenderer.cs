using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Renders a single FS6000 raw-channel <c>.img</c> blob as a displayable
    /// JPEG. Separate from <see cref="FS6000Compositor"/> which combines all
    /// three channels — this is for the case where a caller (UI image-tab
    /// switcher, engineering export) wants to look at <b>one</b> channel in
    /// isolation.
    ///
    /// Up until v2.9.10 the unified image endpoint just handed the raw 16-bit
    /// (or 8-bit) <c>.img</c> bytes to the browser tagged as
    /// <c>image/jpeg</c>. Browsers can't decode raw IM headers + big-endian
    /// pixel data, so the image card silently stayed blank. This class closes
    /// that gap.
    ///
    /// Output format:
    /// <list type="bullet">
    ///   <item><description><b>HighEnergy / LowEnergy</b> (16-bit):
    ///     percentile-clipped, inverted (so dense=dark, matching the vendor
    ///     convention), rendered as 8-bit grayscale JPEG.</description></item>
    ///   <item><description><b>Material</b> (8-bit class indices): colorized
    ///     via <see cref="FS6000Compositor.DefaultMaterialLut"/> and
    ///     rendered as an RGB24 JPEG — so the UI shows the same
    ///     organic/inorganic/metal palette that the composite view uses.</description></item>
    /// </list>
    /// </summary>
    public static class FS6000ChannelRenderer
    {
        // Quality 88 is the same JPEG setting the composite pipeline uses —
        // visually-lossless for x-ray content, reasonable file size.
        private const int JpegQuality = 88;

        /// <summary>
        /// Channel identifiers understood by <see cref="RenderChannelJpeg"/>.
        /// Match the strings stored in <c>fs6000images.imagetype</c>.
        /// </summary>
        public const string ChannelHighEnergy = "HighEnergy";
        public const string ChannelLowEnergy = "LowEnergy";
        public const string ChannelMaterial = "Material";

        /// <summary>
        /// Render the given raw <c>.img</c> blob as a JPEG suitable for
        /// direct <c>&lt;img src&gt;</c> consumption.
        /// </summary>
        /// <param name="rawImgBytes">Raw <c>.img</c> file bytes — 36-byte header + pixel data.</param>
        /// <param name="channelType">One of <see cref="ChannelHighEnergy"/>, <see cref="ChannelLowEnergy"/>, <see cref="ChannelMaterial"/>.</param>
        /// <exception cref="ArgumentException">Unrecognised channel identifier.</exception>
        /// <exception cref="InvalidDataException">Header parse failed (propagated from <see cref="FS6000FormatDecoder"/>).</exception>
        public static byte[] RenderChannelJpeg(byte[] rawImgBytes, string channelType)
        {
            if (rawImgBytes == null || rawImgBytes.Length < FS6000FormatDecoder.HeaderSize)
                throw new InvalidDataException("raw .img blob is empty or shorter than the 36-byte header");

            var header = FS6000FormatDecoder.Fs6000Header.Parse(rawImgBytes);
            int w = header.Width;
            int h = header.Height;

            return channelType switch
            {
                ChannelHighEnergy or ChannelLowEnergy => RenderEnergyChannelJpeg(rawImgBytes, w, h, header.BitDepth),
                ChannelMaterial => RenderMaterialChannelJpeg(rawImgBytes, w, h, header.BitDepth),
                _ => throw new ArgumentException(
                    $"Unknown FS6000 channel type: '{channelType}'. " +
                    $"Expected one of: {ChannelHighEnergy}, {ChannelLowEnergy}, {ChannelMaterial}.",
                    nameof(channelType)),
            };
        }

        private static byte[] RenderEnergyChannelJpeg(byte[] raw, int width, int height, int bitDepth)
        {
            if (bitDepth != 16)
                throw new InvalidDataException(
                    $"HighEnergy/LowEnergy expected 16-bit data, got {bitDepth}-bit header");

            // Decode the raw channel to native-endian ushort[], already vertically flipped.
            var channel = DecodeSingleChannel16(raw, width, height);

            // Reuse the compositor's percentile normalizer + invert-for-vendor-look so
            // HighEnergy/LowEnergy thumbnails look consistent with the color composite.
            byte[] lum8 = FS6000Compositor.NormalizeEnergyChannel(channel);
            for (int i = 0; i < lum8.Length; i++)
                lum8[i] = (byte)(255 - lum8[i]);

            using var img = Image.LoadPixelData<L8>(lum8, width, height);
            using var ms = new MemoryStream(capacity: width * height / 4);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
            return ms.ToArray();
        }

        private static byte[] RenderMaterialChannelJpeg(byte[] raw, int width, int height, int bitDepth)
        {
            if (bitDepth != 8)
                throw new InvalidDataException(
                    $"Material expected 8-bit data, got {bitDepth}-bit header");

            var material = DecodeSingleChannel8(raw, width, height);

            // Colorize via the same LUT the composite view uses → organic = orange,
            // inorganic = green, metal = blue (against near-white background).
            var lut = FS6000Compositor.DefaultMaterialLut;
            var rgb = new byte[width * height * 3];
            for (int i = 0; i < material.Length; i++)
            {
                byte cls = material[i];
                int off = i * 3;
                rgb[off + 0] = lut[cls, 0]; // R
                rgb[off + 1] = lut[cls, 1]; // G
                rgb[off + 2] = lut[cls, 2]; // B
            }

            using var img = Image.LoadPixelData<Rgb24>(rgb, width, height);
            using var ms = new MemoryStream(capacity: width * height / 4);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
            return ms.ToArray();
        }

        // Single-channel decode helpers — thin wrappers around the full decoder's
        // header-validated payload extraction. The full Decode() path requires all
        // three channels together so we inline the single-channel version here.

        private static ushort[] DecodeSingleChannel16(byte[] data, int width, int height)
        {
            long pixelCount = (long)width * height;
            long required = FS6000FormatDecoder.HeaderSize + pixelCount * 2;
            if (data.Length < required)
                throw new InvalidDataException(
                    $"FS6000 16-bit channel truncated: {data.Length} bytes, need {required} for {width}x{height}");

            var pixels = new ushort[pixelCount];
            var srcBytes = data.AsSpan(FS6000FormatDecoder.HeaderSize, (int)(pixelCount * 2));
            var srcU16 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(srcBytes);
            var dstU16 = pixels.AsSpan();
            // Copy row-by-row with vertical flip to match vendor orientation.
            for (int r = 0; r < height; r++)
            {
                int srcRowStart = r * width;
                int dstRowStart = (height - 1 - r) * width;
                srcU16.Slice(srcRowStart, width).CopyTo(dstU16.Slice(dstRowStart, width));
            }
            // BE → native LE byte-swap (vectorized on modern x64).
            System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(pixels.AsSpan(), pixels.AsSpan());
            return pixels;
        }

        private static byte[] DecodeSingleChannel8(byte[] data, int width, int height)
        {
            long pixelCount = (long)width * height;
            long required = FS6000FormatDecoder.HeaderSize + pixelCount;
            if (data.Length < required)
                throw new InvalidDataException(
                    $"FS6000 8-bit channel truncated: {data.Length} bytes, need {required} for {width}x{height}");

            var pixels = new byte[pixelCount];
            for (int r = 0; r < height; r++)
            {
                int srcRowStart = FS6000FormatDecoder.HeaderSize + r * width;
                int dstRowStart = (height - 1 - r) * width;
                Buffer.BlockCopy(data, srcRowStart, pixels, dstRowStart, width);
            }
            return pixels;
        }
    }
}
