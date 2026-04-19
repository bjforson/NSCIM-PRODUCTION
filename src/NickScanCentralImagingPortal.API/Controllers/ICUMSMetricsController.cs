using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Services.IcumApi;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for ICUMS metrics
    /// Phase 3.1: Exposes performance metrics for monitoring and dashboards
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ICUMSMetricsController : ControllerBase
    {
        private readonly ICUMSMetrics _metrics;
        private readonly ILogger<ICUMSMetricsController> _logger;

        public ICUMSMetricsController(
            ICUMSMetrics metrics,
            ILogger<ICUMSMetricsController> logger)
        {
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        /// Get complete metrics snapshot
        /// </summary>
        [HttpGet("snapshot")]
        public ActionResult<ICUMSMetricsSnapshot> GetSnapshot()
        {
            try
            {
                var snapshot = _metrics.GetSnapshot();
                return Ok(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS metrics snapshot");
                return StatusCode(500, new { error = "Failed to retrieve metrics" });
            }
        }

        /// <summary>
        /// Get counter values
        /// </summary>
        [HttpGet("counters")]
        public ActionResult<Dictionary<string, long>> GetCounters()
        {
            try
            {
                var snapshot = _metrics.GetSnapshot();
                return Ok(snapshot.Counters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS counters");
                return StatusCode(500, new { error = "Failed to retrieve counters" });
            }
        }

        /// <summary>
        /// Get gauge values
        /// </summary>
        [HttpGet("gauges")]
        public ActionResult<Dictionary<string, double>> GetGauges()
        {
            try
            {
                var snapshot = _metrics.GetSnapshot();
                return Ok(snapshot.Gauges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS gauges");
                return StatusCode(500, new { error = "Failed to retrieve gauges" });
            }
        }

        /// <summary>
        /// Get histogram statistics
        /// </summary>
        [HttpGet("histograms")]
        public ActionResult<Dictionary<string, HistogramStats>> GetHistograms()
        {
            try
            {
                var snapshot = _metrics.GetSnapshot();
                return Ok(snapshot.Histograms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS histograms");
                return StatusCode(500, new { error = "Failed to retrieve histograms" });
            }
        }

        /// <summary>
        /// Get specific counter value
        /// </summary>
        [HttpGet("counters/{counterName}")]
        public ActionResult<long> GetCounter(string counterName)
        {
            try
            {
                var value = _metrics.GetCounter(counterName);
                return Ok(new { counter = counterName, value = value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting counter {CounterName}", counterName);
                return StatusCode(500, new { error = $"Failed to retrieve counter: {counterName}" });
            }
        }

        /// <summary>
        /// Get specific gauge value
        /// </summary>
        [HttpGet("gauges/{gaugeName}")]
        public ActionResult<double> GetGauge(string gaugeName)
        {
            try
            {
                var value = _metrics.GetGauge(gaugeName);
                return Ok(new { gauge = gaugeName, value = value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting gauge {GaugeName}", gaugeName);
                return StatusCode(500, new { error = $"Failed to retrieve gauge: {gaugeName}" });
            }
        }

        /// <summary>
        /// Get specific histogram statistics
        /// </summary>
        [HttpGet("histograms/{histogramName}")]
        public ActionResult<HistogramStats> GetHistogram(string histogramName)
        {
            try
            {
                var stats = _metrics.GetHistogramStats(histogramName);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting histogram {HistogramName}", histogramName);
                return StatusCode(500, new { error = $"Failed to retrieve histogram: {histogramName}" });
            }
        }
    }
}

