using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.ASE
{
    /// <summary>
    /// Unit tests for the pure-C# ASE fallback decoder, mirroring the
    /// validation the Python reference (services/image-splitter/inspector/
    /// decoders/ase.py) already passes on 10 production samples.
    ///
    /// Fixture: TestData/Ase/sample_single_view.ase is copy of
    /// services/image-splitter/tools/ase_format_research/samples/
    /// fec5b6f9-9329-49d8-9d5c-4575bc1664fb.ase — a 544x1554 single-view
    /// DualEnergyBitmap (line_data_type=2) from container MRSU6538751,
    /// scan 2026-03-31. Expected decoded values lifted from its
    /// corresponding .report.json.
    /// </summary>
    public class AseFormatDecoderTests
    {
        private const string SampleSingleView = "TestData/Ase/sample_single_view.ase";
        // Values from the Python-decoded .report.json sidecar
        private const int ExpectedWidth = 544;
        private const int ExpectedHeight = 1554;
        private const int ExpectedLineDataType = 2;
        private const long ExpectedSizeBytes = 1691484;

        private static byte[] LoadSample()
        {
            var path = Path.Combine(AppContext.BaseDirectory, SampleSingleView);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}. " +
                "Check that <None Update=\"TestData\\Ase\\**\\*.ase\" CopyToOutputDirectory=\"PreserveNewest\" /> " +
                "is in NickScanCentralImagingPortal.Tests.csproj.");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(ExpectedSizeBytes, bytes.Length);
            return bytes;
        }

        [Fact]
        public void Decode_KnownSample_ReturnsExpectedDimensions()
        {
            var blob = LoadSample();

            var decoded = AseFormatDecoder.Decode(blob);

            Assert.Equal(ExpectedWidth, decoded.Width);
            Assert.Equal(ExpectedHeight, decoded.Height);
            Assert.Equal(ExpectedLineDataType, decoded.LineDataType);
            Assert.Equal((long)ExpectedWidth * ExpectedHeight, decoded.Pixels.Length);
            Assert.False(decoded.IsMultiPanel);
        }

        [Fact]
        public void Decode_KnownSample_PixelStatsMatchPythonReference()
        {
            // Values from the .report.json sidecar ("pixel_stats") — the Python
            // reference decoder reported exactly these numbers, so if our C#
            // decoder agrees we know byte-for-byte parity on pixel layout
            // (including endianness).
            //
            //   "pixel_stats": { "min": 0, "max": 65535, "mean": 30089.91, "std": 22888.29 }
            var blob = LoadSample();
            var decoded = AseFormatDecoder.Decode(blob);
            var stats = AsePercentileRenderer.ComputePixelStats(decoded.Pixels);

            Assert.Equal(0, stats.Min);
            Assert.Equal(65535, stats.Max);
            // Allow tiny numerical tolerance; Python uses float64 on a buffer
            // of the same size, so they should match to 3 decimal places.
            Assert.InRange(stats.Mean, 30089.90, 30089.92);
            Assert.InRange(stats.StdDev, 22888.28, 22888.30);
        }

        [Fact]
        public void Decode_InvalidMagic_ThrowsInvalidDataException()
        {
            var bogus = new byte[64]; // all zeros — no 'IM\0\0' magic

            var ex = Assert.Throws<InvalidDataException>(() => AseFormatDecoder.Decode(bogus));
            Assert.Contains("ASE magic mismatch", ex.Message);
        }

        [Fact]
        public void Decode_TooSmallBlob_ThrowsInvalidDataException()
        {
            var tiny = new byte[8];

            var ex = Assert.Throws<InvalidDataException>(() => AseFormatDecoder.Decode(tiny));
            Assert.Contains("too small", ex.Message);
        }

        [Fact]
        public void Decode_TruncatedPayload_ThrowsWithCompressionHint()
        {
            // Fabricate a valid header declaring 1000x1000 but supply only 100 pixel bytes.
            var buf = new byte[16 + 100];
            // magic "IM\0\0"
            buf[0] = 0x49; buf[1] = 0x4D; buf[2] = 0x00; buf[3] = 0x00;
            // width=1000 LE
            buf[4] = 0xE8; buf[5] = 0x03;
            // height=1000 LE
            buf[6] = 0xE8; buf[7] = 0x03;
            // line_data_type=2 LE
            buf[8] = 0x02; buf[9] = 0x00;

            var ex = Assert.Throws<InvalidDataException>(() => AseFormatDecoder.Decode(buf));
            Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CompEncryptHeader", ex.Message);
        }

        [Fact]
        public void Decode_ZeroDimensions_ThrowsInvalidDataException()
        {
            var buf = new byte[32];
            buf[0] = 0x49; buf[1] = 0x4D; buf[2] = 0x00; buf[3] = 0x00;
            // width=0, height=0, line_data_type=2
            buf[8] = 0x02;

            var ex = Assert.Throws<InvalidDataException>(() => AseFormatDecoder.Decode(buf));
            Assert.Contains("zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Decode_FakeTriPanelHeader_ReturnsLineDataType3AndIsMultiPanelTrue()
        {
            // We don't ship a 5 MB tri-panel sample in the test project, so
            // we fabricate a tiny but valid tri-panel blob to cover the
            // line_data_type=3 branch of the decoder. width must be divisible
            // by 3 for IsMultiPanel to be useful.
            int w = 9, h = 4;
            var buf = new byte[16 + w * h * 2];
            buf[0] = 0x49; buf[1] = 0x4D; buf[2] = 0x00; buf[3] = 0x00;
            buf[4] = (byte)(w & 0xFF); buf[5] = (byte)(w >> 8);
            buf[6] = (byte)(h & 0xFF); buf[7] = (byte)(h >> 8);
            buf[8] = 0x03; buf[9] = 0x00; // line_data_type=3

            var decoded = AseFormatDecoder.Decode(buf);
            Assert.Equal(w, decoded.Width);
            Assert.Equal(h, decoded.Height);
            Assert.Equal(3, decoded.LineDataType);
            Assert.True(decoded.IsMultiPanel);
            Assert.Equal(w * h, decoded.Pixels.Length);
        }

        [Fact]
        public void Render_KnownSamplePercentileJpeg_ProducesValidJpegWithExpectedDimensions()
        {
            var blob = LoadSample();
            var decoded = AseFormatDecoder.Decode(blob);

            using var bmp = AsePercentileRenderer.BuildBitmap(decoded);
            Assert.Equal(ExpectedWidth, bmp.Width);
            Assert.Equal(ExpectedHeight, bmp.Height);
            Assert.Equal(PixelFormat.Format8bppIndexed, bmp.PixelFormat);

            // Round-trip through JPEG and re-open to confirm it's a valid image
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            var jpegBytes = ms.ToArray();

            Assert.True(jpegBytes.Length > 1000, $"JPEG output suspiciously small: {jpegBytes.Length} bytes");
            // JPEG magic: FF D8
            Assert.Equal(0xFF, jpegBytes[0]);
            Assert.Equal(0xD8, jpegBytes[1]);

            using var readBack = Image.FromStream(new MemoryStream(jpegBytes));
            Assert.Equal(ExpectedWidth, readBack.Width);
            Assert.Equal(ExpectedHeight, readBack.Height);
        }

        [Fact]
        public void Render_AllZeroPixels_DoesNotDivideByZero()
        {
            // Degenerate edge case: all pixels identical. The percentile
            // computation must return a width of at least 1 so the linear
            // map doesn't divide by zero downstream.
            int w = 16, h = 16;
            var buf = new byte[16 + w * h * 2];
            buf[0] = 0x49; buf[1] = 0x4D; buf[2] = 0x00; buf[3] = 0x00;
            buf[4] = (byte)(w & 0xFF); buf[5] = (byte)(w >> 8);
            buf[6] = (byte)(h & 0xFF); buf[7] = (byte)(h >> 8);
            buf[8] = 0x02; buf[9] = 0x00;
            // pixel payload stays all-zero

            var decoded = AseFormatDecoder.Decode(buf);
            using var bmp = AsePercentileRenderer.BuildBitmap(decoded);

            Assert.Equal(w, bmp.Width);
            Assert.Equal(h, bmp.Height);
            // Successfully building the bitmap is the assertion — no NaN,
            // no DivideByZero, no exception.
        }
    }
}
