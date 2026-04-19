using System.Diagnostics;
using System.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [HasPermission(Permissions.SystemPerformanceView)]
    public class MemoryDiagnosticsController : ControllerBase
    {
        private readonly ILogger<MemoryDiagnosticsController> _logger;

        public MemoryDiagnosticsController(ILogger<MemoryDiagnosticsController> _logger)
        {
            this._logger = _logger;
        }

        /// <summary>
        /// Get current memory usage statistics
        /// </summary>
        [HttpGet("status")]
        public ActionResult<MemoryStatus> GetMemoryStatus()
        {
            var process = Process.GetCurrentProcess();
            var gcMemInfo = GC.GetGCMemoryInfo();

            var status = new MemoryStatus
            {
                // Process Memory
                WorkingSetMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
                PrivateMemoryMB = Math.Round(process.PrivateMemorySize64 / 1024.0 / 1024.0, 2),
                VirtualMemoryMB = Math.Round(process.VirtualMemorySize64 / 1024.0 / 1024.0, 2),

                // GC Memory
                TotalAllocatedMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2),
                HeapSizeMB = Math.Round(gcMemInfo.HeapSizeBytes / 1024.0 / 1024.0, 2),
                FragmentedMB = Math.Round(gcMemInfo.FragmentedBytes / 1024.0 / 1024.0, 2),

                // GC Statistics
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),

                // GC Info
                IsServerGC = GCSettings.IsServerGC,
                LatencyMode = GCSettings.LatencyMode.ToString(),
                TotalAvailableMemoryMB = Math.Round(gcMemInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2),
                HighMemoryLoadThresholdMB = Math.Round(gcMemInfo.HighMemoryLoadThresholdBytes / 1024.0 / 1024.0, 2),
                MemoryLoadPercentage = Math.Round((double)gcMemInfo.MemoryLoadBytes / gcMemInfo.TotalAvailableMemoryBytes * 100, 2),

                // Process Info
                ProcessUptime = DateTime.Now - Process.GetCurrentProcess().StartTime,
                ThreadCount = process.Threads.Count
            };

            return Ok(status);
        }

        /// <summary>
        /// Force garbage collection (use with caution)
        /// </summary>
        [HttpPost("gc/collect")]
        public ActionResult ForceGC([FromQuery] int generation = 2)
        {
            try
            {
                var beforeMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2);

                GC.Collect(generation, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();

                var afterMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2);
                var freedMB = Math.Round(beforeMB - afterMB, 2);

                _logger.LogWarning("🧹 Manual GC collection triggered | Generation: {Gen} | Before: {Before}MB | After: {After}MB | Freed: {Freed}MB",
                    generation, beforeMB, afterMB, freedMB);

                return Ok(new
                {
                    generation,
                    beforeMB,
                    afterMB,
                    freedMB,
                    message = $"GC Gen{generation} collection completed. Freed {freedMB}MB"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forcing GC collection");
                return StatusCode(500, new { error = "Failed to force GC collection", details = ex.Message });
            }
        }

        /// <summary>
        /// Get detailed GC statistics
        /// </summary>
        [HttpGet("gc/stats")]
        public ActionResult<GCStats> GetGCStats()
        {
            var gcMemInfo = GC.GetGCMemoryInfo();

            var stats = new GCStats
            {
                // Generation Collections
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),

                // Memory Info
                TotalAllocatedMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2),
                HeapSizeMB = Math.Round(gcMemInfo.HeapSizeBytes / 1024.0 / 1024.0, 2),
                FragmentedMB = Math.Round(gcMemInfo.FragmentedBytes / 1024.0 / 1024.0, 2),
                PromotedMB = Math.Round(gcMemInfo.PromotedBytes / 1024.0 / 1024.0, 2),

                // Heap Generation Sizes
                Gen0SizeMB = gcMemInfo.GenerationInfo.Length > 0 ? Math.Round(gcMemInfo.GenerationInfo[0].SizeAfterBytes / 1024.0 / 1024.0, 2) : 0,
                Gen1SizeMB = gcMemInfo.GenerationInfo.Length > 1 ? Math.Round(gcMemInfo.GenerationInfo[1].SizeAfterBytes / 1024.0 / 1024.0, 2) : 0,
                Gen2SizeMB = gcMemInfo.GenerationInfo.Length > 2 ? Math.Round(gcMemInfo.GenerationInfo[2].SizeAfterBytes / 1024.0 / 1024.0, 2) : 0,

                // GC Settings
                IsServerGC = GCSettings.IsServerGC,
                LatencyMode = GCSettings.LatencyMode.ToString(),
                Concurrent = gcMemInfo.Concurrent,
                Compacted = gcMemInfo.Compacted,

                // Memory Pressure
                TotalAvailableMemoryMB = Math.Round(gcMemInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2),
                HighMemoryLoadThresholdMB = Math.Round(gcMemInfo.HighMemoryLoadThresholdBytes / 1024.0 / 1024.0, 2),
                MemoryLoadMB = Math.Round(gcMemInfo.MemoryLoadBytes / 1024.0 / 1024.0, 2),
                MemoryLoadPercentage = Math.Round((double)gcMemInfo.MemoryLoadBytes / gcMemInfo.TotalAvailableMemoryBytes * 100, 2)
            };

            return Ok(stats);
        }

        /// <summary>
        /// Get memory trend over last N seconds
        /// </summary>
        [HttpGet("trend")]
        public async Task<ActionResult<List<MemorySnapshot>>> GetMemoryTrend([FromQuery] int seconds = 60, [FromQuery] int intervalMs = 5000)
        {
            if (seconds > 300) // Max 5 minutes
            {
                return BadRequest("Maximum trend duration is 300 seconds");
            }

            var snapshots = new List<MemorySnapshot>();
            var iterations = seconds * 1000 / intervalMs;

            for (int i = 0; i < iterations; i++)
            {
                var process = Process.GetCurrentProcess();
                snapshots.Add(new MemorySnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    WorkingSetMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
                    ManagedMemoryMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2)
                });

                if (i < iterations - 1)
                {
                    await Task.Delay(intervalMs);
                }
            }

            return Ok(snapshots);
        }
    }

    public class MemoryStatus
    {
        // Process Memory
        public double WorkingSetMB { get; set; }
        public double PrivateMemoryMB { get; set; }
        public double VirtualMemoryMB { get; set; }

        // GC Memory
        public double TotalAllocatedMB { get; set; }
        public double HeapSizeMB { get; set; }
        public double FragmentedMB { get; set; }

        // GC Statistics
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }

        // GC Info
        public bool IsServerGC { get; set; }
        public string LatencyMode { get; set; } = string.Empty;
        public double TotalAvailableMemoryMB { get; set; }
        public double HighMemoryLoadThresholdMB { get; set; }
        public double MemoryLoadPercentage { get; set; }

        // Process Info
        public TimeSpan ProcessUptime { get; set; }
        public int ThreadCount { get; set; }
    }

    public class GCStats
    {
        // Generation Collections
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }

        // Memory Info
        public double TotalAllocatedMB { get; set; }
        public double HeapSizeMB { get; set; }
        public double FragmentedMB { get; set; }
        public double PromotedMB { get; set; }

        // Heap Generation Sizes
        public double Gen0SizeMB { get; set; }
        public double Gen1SizeMB { get; set; }
        public double Gen2SizeMB { get; set; }

        // GC Settings
        public bool IsServerGC { get; set; }
        public string LatencyMode { get; set; } = string.Empty;
        public bool Concurrent { get; set; }
        public bool Compacted { get; set; }

        // Memory Pressure
        public double TotalAvailableMemoryMB { get; set; }
        public double HighMemoryLoadThresholdMB { get; set; }
        public double MemoryLoadMB { get; set; }
        public double MemoryLoadPercentage { get; set; }
    }

    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public double WorkingSetMB { get; set; }
        public double ManagedMemoryMB { get; set; }
    }
}

