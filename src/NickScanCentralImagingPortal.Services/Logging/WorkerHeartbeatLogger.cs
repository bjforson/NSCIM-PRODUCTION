using System;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
    /// <summary>
    /// Audit 8.13 (2026-05-05, Sprint 5G2): per-iteration heartbeat log helper
    /// for <c>BackgroundService</c> classes.
    ///
    /// Background: NSCIM_API hosts 35+ <c>BackgroundService</c> classes. Most
    /// either log nothing per iteration (when there is no work) or log results
    /// unconditionally without a structured "iteration_summary" event. The
    /// audit confirms operators cannot distinguish alive-and-idle from
    /// alive-and-buggy from logs alone.
    ///
    /// Fix: every wired BackgroundService logs one summary line per iteration
    /// of the form
    /// <c>"[ServiceId] iteration=N elapsed_ms=X processed=Y skipped=Z failed=W"</c>.
    /// The structured-template properties (<c>{ServiceId}</c>,
    /// <c>{Iteration}</c>, etc.) make the log searchable / Splunkable; the
    /// rendered string is operator-readable in the file sink.
    ///
    /// Wired services (Sprint 5G2 initial rollout):
    /// <list type="bullet">
    ///   <item><c>ImageAnalysisOrchestratorService</c> (cycle counter already present, reused)</item>
    ///   <item><c>ContainerCompletenessOrchestratorService</c> (cycleCount already present)</item>
    ///   <item><c>IcumPipelineOrchestratorService</c> (added local counter)</item>
    ///   <item><c>ContainerCompletenessService</c> (queue-draining BackgroundService, added local counter)</item>
    ///   <item><c>ZombieAnalysisGroupSweeperService</c> (added local counter)</item>
    /// </list>
    ///
    /// The remaining 30+ BackgroundServices should be wired incrementally as
    /// each is touched for unrelated work — the fan-out across all of them in
    /// a single sprint was rejected as too many touches at once. Add a call to
    /// <see cref="LogIterationSummary"/> at the end of any newly-wired
    /// service's iteration body.
    ///
    /// The cycle correlation property pushed by
    /// <c>BackgroundLogScopeExtensions.BeginCycle</c> attaches to this log
    /// line automatically when the call site is inside a <c>using</c> scope.
    /// That makes a heartbeat line per cycle the natural anchor an operator
    /// uses when tracing one cycle of one worker.
    /// </summary>
    public static class WorkerHeartbeatLogger
    {
        /// <summary>
        /// Emit the standard end-of-iteration summary line.
        /// </summary>
        /// <param name="logger">The worker's <see cref="ILogger"/>.</param>
        /// <param name="serviceId">
        /// Stable identifier for the service; should match the value passed to
        /// <c>BeginCycle</c> earlier in the iteration.
        /// </param>
        /// <param name="iteration">
        /// Monotonic iteration counter for this service instance. Tracked by
        /// the worker (instance field, <c>Interlocked.Increment</c> on a
        /// static, etc.). Used for "I just ran" telemetry and to spot
        /// stuck/wedged iterations.
        /// </param>
        /// <param name="elapsed">
        /// Wall-clock duration of the iteration body. Truncated to
        /// milliseconds at log time.
        /// </param>
        /// <param name="itemsProcessed">
        /// Items successfully handled this iteration. Use 0 for an idle pass
        /// (alive-and-idle is a useful distinction from no-line-at-all).
        /// </param>
        /// <param name="itemsSkipped">
        /// Items examined but deliberately not handled (gated, throttled,
        /// already-done, etc.). Optional.
        /// </param>
        /// <param name="itemsFailed">
        /// Items attempted but failed (caught exceptions, validation rejects).
        /// Optional.
        /// </param>
        public static void LogIterationSummary(
            this ILogger logger,
            string serviceId,
            int iteration,
            TimeSpan elapsed,
            int itemsProcessed,
            int itemsSkipped = 0,
            int itemsFailed = 0)
        {
            if (logger is null)
            {
                return;
            }

            logger.LogInformation(
                "[{ServiceId}] iteration={Iteration} elapsed_ms={ElapsedMs} processed={ItemsProcessed} skipped={ItemsSkipped} failed={ItemsFailed}",
                serviceId,
                iteration,
                (int)elapsed.TotalMilliseconds,
                itemsProcessed,
                itemsSkipped,
                itemsFailed);
        }
    }
}
