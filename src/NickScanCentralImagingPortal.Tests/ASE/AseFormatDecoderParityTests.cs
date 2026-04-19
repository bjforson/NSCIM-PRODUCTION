using System;
using System.IO;
using System.Text.Json;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.ASE
{
    /// <summary>
    /// One-time parity check between the C# <see cref="AseFormatDecoder"/>
    /// and the Python reference implementation at
    /// <c>services/image-splitter/inspector/decoders/ase.py</c>.
    ///
    /// Reads the same 10 production samples that the Python characterization
    /// script produced `.report.json` sidecars for, runs the C# decoder on
    /// them, and compares (width, height, line_data_type, pixel min, max,
    /// mean, stddev) to the values recorded in the Python-generated JSON.
    ///
    /// These tests are GATED by environment variable
    /// <c>ASE_PARITY_SAMPLES_DIR</c>. If the env var is not set (or points
    /// to a nonexistent directory), the test is skipped. This keeps the
    /// 82 MB sample corpus out of the unit test binary while still letting
    /// anyone reproduce the check locally with:
    ///
    /// <code>
    /// $env:ASE_PARITY_SAMPLES_DIR = "C:\Shared\NSCIM_PRODUCTION\services\image-splitter\tools\ase_format_research\samples"
    /// dotnet test --filter "FullyQualifiedName~AseFormatDecoderParity"
    /// </code>
    ///
    /// The single committed test fixture
    /// (<c>TestData/Ase/sample_single_view.ase</c>) still gets exercised by
    /// <see cref="AseFormatDecoderTests"/> on every CI run.
    /// </summary>
    public class AseFormatDecoderParityTests
    {
        private const string EnvVar = "ASE_PARITY_SAMPLES_DIR";

        // Sample IDs to cross-check. Picked deliberately to cover:
        //   - the single-view sample already in the unit test fixture (regression anchor)
        //   - a different single-view sample (line_data_type=2, 544-wide)
        //   - a tri-panel sample (line_data_type=3, 1632-wide) — this is the
        //     biggest coverage gap in the small committed fixture
        private static readonly string[] SampleIds =
        {
            "fec5b6f9-9329-49d8-9d5c-4575bc1664fb", // single-view 544x1554 — same as committed fixture
            "0183f02c-a16f-4d97-93b7-54a4a8c40d7c", // single-view 544x1579
            "80a43828-1a8b-40eb-9ce5-0f1dc408bf00", // TRI-PANEL 1632x1571 line_data_type=3
        };

        public static bool SamplesAvailable()
        {
            var dir = Environment.GetEnvironmentVariable(EnvVar);
            return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Decode_Sample_MatchesPythonReference(int sampleIndex)
        {
            // xUnit doesn't have a first-class "skip at runtime" primitive
            // without the SkippableFact package, so we instead early-return
            // with a clear assertion-or-message when the env var is unset.
            // CI runs without the env var and these tests no-op successfully;
            // a developer wanting to verify parity sets the env var locally.
            if (!SamplesAvailable())
            {
                // Treat as "not applicable" — successful no-op.
                // The committed unit test in AseFormatDecoderTests still
                // covers the single-sample happy path on every CI run.
                return;
            }

            var dir = Environment.GetEnvironmentVariable(EnvVar)!;
            var id = SampleIds[sampleIndex];
            var asePath = Path.Combine(dir, $"{id}.ase");
            var reportPath = Path.Combine(dir, $"{id}.report.json");

            Assert.True(File.Exists(asePath), $"Sample missing: {asePath}");
            Assert.True(File.Exists(reportPath), $"Report JSON missing: {reportPath}");

            // Load Python reference stats
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var header = reportDoc.RootElement.GetProperty("header");
            int expectedWidth = header.GetProperty("width").GetInt32();
            int expectedHeight = header.GetProperty("height").GetInt32();
            // Python calls line_data_type "channels" in the report JSON (legacy naming)
            int expectedLineDataType = header.GetProperty("channels").GetInt32();

            var pythonStats = reportDoc.RootElement.GetProperty("pixel_stats");
            int expectedMin = pythonStats.GetProperty("min").GetInt32();
            int expectedMax = pythonStats.GetProperty("max").GetInt32();
            double expectedMean = pythonStats.GetProperty("mean").GetDouble();
            double expectedStd = pythonStats.GetProperty("std").GetDouble();

            // Run the C# decoder
            var blob = File.ReadAllBytes(asePath);
            var decoded = AseFormatDecoder.Decode(blob);
            var actualStats = AsePercentileRenderer.ComputePixelStats(decoded.Pixels);

            // Dimensions and format fields must match exactly
            Assert.Equal(expectedWidth, decoded.Width);
            Assert.Equal(expectedHeight, decoded.Height);
            Assert.Equal(expectedLineDataType, decoded.LineDataType);

            // Pixel stats must match exactly on integer fields and to 2
            // decimal places on the floating-point ones. Python uses float64
            // with the same algorithm, so they should agree to at least
            // 5 decimal places — 0.01 gives a generous margin.
            Assert.Equal(expectedMin, actualStats.Min);
            Assert.Equal(expectedMax, actualStats.Max);
            Assert.InRange(actualStats.Mean, expectedMean - 0.01, expectedMean + 0.01);
            Assert.InRange(actualStats.StdDev, expectedStd - 0.01, expectedStd + 0.01);
        }
    }
}
