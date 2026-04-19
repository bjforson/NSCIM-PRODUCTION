using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Npgsql;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FS6000ParityTest;

/// <summary>
/// Pixel-parity test: run the native C# FS6000 decoder+compositor and compare
/// the output PNG bytes against the Python inspector's output for the same
/// scan. Declares success if the per-channel mean absolute difference is
/// below a small tolerance (matches the float32 / percentile-interpolation
/// slop we documented in FS6000Compositor).
/// </summary>
internal static class Program
{
    private const string DefaultScanId = "b24bc648-0290-499f-b2fc-53ae16090616";  // FCIU5297925
    private const string PythonBase = "http://localhost:5320";

    private static async Task<int> Main(string[] args)
    {
        string scanId = args.Length > 0 ? args[0] : DefaultScanId;
        string outDir = args.Length > 1 ? args[1] : @"C:\temp\fs6000-parity";
        Directory.CreateDirectory(outDir);

        string connStr = BuildConnectionString();
        Console.WriteLine($"[parity] scan id: {scanId}");
        Console.WriteLine($"[parity] output dir: {outDir}");

        // ── 1. Fetch all three channel blobs from DB ──────────────────────
        Console.WriteLine("[parity] fetching channel blobs from DB...");
        var blobs = await FetchChannelBlobsAsync(connStr, scanId);
        Console.WriteLine($"[parity]   HighEnergy bytes = {blobs.High.Length:N0}");
        Console.WriteLine($"[parity]   LowEnergy  bytes = {blobs.Low.Length:N0}");
        Console.WriteLine($"[parity]   Material   bytes = {blobs.Material.Length:N0}");

        // ── 2. Native C# decode + composite ───────────────────────────────
        Console.WriteLine("[parity] running native C# decode + composite...");
        var t0 = DateTime.UtcNow;
        var decoded = FS6000FormatDecoder.Decode(blobs.High, blobs.Low, blobs.Material);
        var t1 = DateTime.UtcNow;
        byte[] nativePngBytes = FS6000Compositor.CompositeRgbPng(decoded);
        var t2 = DateTime.UtcNow;
        Console.WriteLine($"[parity]   decoded {decoded.Width}x{decoded.Height} in {(t1 - t0).TotalMilliseconds:F0}ms");
        Console.WriteLine($"[parity]   composite+encode in {(t2 - t1).TotalMilliseconds:F0}ms");
        Console.WriteLine($"[parity]   native PNG size = {nativePngBytes.Length:N0} bytes");

        var nativePath = Path.Combine(outDir, $"{scanId}.native.png");
        File.WriteAllBytes(nativePath, nativePngBytes);
        Console.WriteLine($"[parity]   wrote {nativePath}");

        // ── 3. Python inspector output ────────────────────────────────────
        Console.WriteLine("[parity] fetching Python inspector composite...");
        byte[] pythonPngBytes;
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        {
            var url = $"{PythonBase}/inspector/composite/fs6000/{scanId}";
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            pythonPngBytes = await resp.Content.ReadAsByteArrayAsync();
        }
        Console.WriteLine($"[parity]   python PNG size = {pythonPngBytes.Length:N0} bytes");

        var pythonPath = Path.Combine(outDir, $"{scanId}.python.png");
        File.WriteAllBytes(pythonPath, pythonPngBytes);
        Console.WriteLine($"[parity]   wrote {pythonPath}");

        // ── 4. Decode both PNGs and diff the pixels ───────────────────────
        Console.WriteLine("[parity] decoding both PNGs for pixel compare...");
        using var nativeImg = Image.Load<Rgb24>(nativePngBytes);
        using var pythonImg = Image.Load<Rgb24>(pythonPngBytes);

        if (nativeImg.Width != pythonImg.Width || nativeImg.Height != pythonImg.Height)
        {
            Console.Error.WriteLine(
                $"[parity] FAIL: dimension mismatch: native={nativeImg.Width}x{nativeImg.Height} python={pythonImg.Width}x{pythonImg.Height}");
            return 2;
        }
        Console.WriteLine($"[parity]   dims OK: {nativeImg.Width}x{nativeImg.Height}");

        // Walk every pixel; accumulate stats. ImageSharp 3.x uses ProcessPixelRows
        // to expose the backing buffers; we copy both images to flat byte arrays
        // first so the diff loop stays simple and allocation-free.
        int W = nativeImg.Width, H = nativeImg.Height;
        long totalPixels = (long)W * H;
        var natBuf = new byte[(int)totalPixels * 3];
        var pyBuf = new byte[(int)totalPixels * 3];
        nativeImg.CopyPixelDataTo(natBuf);
        pythonImg.CopyPixelDataTo(pyBuf);

        long sumAbsR = 0, sumAbsG = 0, sumAbsB = 0;
        long maxAbsR = 0, maxAbsG = 0, maxAbsB = 0;
        long identicalCount = 0;
        long within1Count = 0;
        long within2Count = 0;

        for (long i = 0; i < totalPixels; i++)
        {
            int off = (int)(i * 3);
            int dr = Math.Abs(natBuf[off] - pyBuf[off]);
            int dg = Math.Abs(natBuf[off + 1] - pyBuf[off + 1]);
            int db = Math.Abs(natBuf[off + 2] - pyBuf[off + 2]);
            sumAbsR += dr; sumAbsG += dg; sumAbsB += db;
            if (dr > maxAbsR) maxAbsR = dr;
            if (dg > maxAbsG) maxAbsG = dg;
            if (db > maxAbsB) maxAbsB = db;
            int maxD = Math.Max(dr, Math.Max(dg, db));
            if (maxD == 0) identicalCount++;
            if (maxD <= 1) within1Count++;
            if (maxD <= 2) within2Count++;
        }

        double meanR = (double)sumAbsR / totalPixels;
        double meanG = (double)sumAbsG / totalPixels;
        double meanB = (double)sumAbsB / totalPixels;
        double pctIdent = 100.0 * identicalCount / totalPixels;
        double pctWithin1 = 100.0 * within1Count / totalPixels;
        double pctWithin2 = 100.0 * within2Count / totalPixels;

        Console.WriteLine();
        Console.WriteLine("== Pixel parity summary ==");
        Console.WriteLine($"  total pixels    : {totalPixels:N0}");
        Console.WriteLine($"  identical       : {identicalCount:N0}  ({pctIdent:F3}%)");
        Console.WriteLine($"  within ±1/chan  : {within1Count:N0}  ({pctWithin1:F3}%)");
        Console.WriteLine($"  within ±2/chan  : {within2Count:N0}  ({pctWithin2:F3}%)");
        Console.WriteLine($"  mean |Δ|  R={meanR:F4}  G={meanG:F4}  B={meanB:F4}");
        Console.WriteLine($"  max  |Δ|  R={maxAbsR}  G={maxAbsG}  B={maxAbsB}");

        // Pass criteria: ≥99% within ±2 AND mean |Δ| < 1.0 per channel.
        // Per-channel max of a few units is expected due to:
        //   - float32 order-of-operations differences in blend/gamma
        //   - histogram "lower-rank" percentile vs numpy linear (±1 bin = ±1/65536)
        //   - PNG encoder chunk differences (PNG file bytes differ, pixels match)
        bool passed = pctWithin2 >= 99.0 && meanR < 1.0 && meanG < 1.0 && meanB < 1.0;

        Console.WriteLine();
        if (passed)
        {
            Console.WriteLine("[parity] PASS");
            return 0;
        }
        Console.Error.WriteLine("[parity] FAIL: pixel divergence exceeds tolerance");
        return 1;
    }

    private record ChannelBlobs(byte[] High, byte[] Low, byte[] Material);

    private static async Task<ChannelBlobs> FetchChannelBlobsAsync(string connStr, string scanId)
    {
        byte[]? high = null, low = null, mat = null;
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT imagetype, imagedata FROM fs6000images " +
            "WHERE scanid = @scanid::uuid " +
            "AND imagetype IN ('HighEnergy', 'LowEnergy', 'Material') " +
            "AND imagedata IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("scanid", scanId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            var type = rdr.GetString(0);
            var bytes = (byte[])rdr["imagedata"];
            switch (type)
            {
                case "HighEnergy": high = bytes; break;
                case "LowEnergy": low = bytes; break;
                case "Material": mat = bytes; break;
            }
        }
        if (high == null || low == null || mat == null)
        {
            throw new InvalidOperationException(
                $"scan {scanId}: missing channels " +
                $"(high={(high != null)} low={(low != null)} material={(mat != null)})");
        }
        return new ChannelBlobs(high, low, mat);
    }

    private static string BuildConnectionString()
    {
        // Same env var the Python inspector reads in config.py.
        string? password = Environment.GetEnvironmentVariable("NICKSCAN_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "DB password not found: set the NICKSCAN_DB_PASSWORD machine env var " +
                "(the Python inspector uses the same one).");
        }
        return $"Host=localhost;Port=5432;Database=nickscan_production;Username=postgres;Password={password};";
    }
}
