using System;
using System.Threading;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Singleton in-memory progress tracker for the FS6000 raw-channel
    /// backfill job. Only one backfill can run per API process at a time; a
    /// second POST while a job is active returns 409 Conflict via the
    /// <see cref="TryStart"/> gate.
    ///
    /// State does NOT persist across API restarts — the tracker resets to
    /// idle on process start. If a job is interrupted mid-run, the operator
    /// simply re-POSTs; the ingester is idempotent so no rows are duplicated.
    /// </summary>
    public class FS6000BackfillJobTracker
    {
        private readonly object _lock = new();
        private int _inProgress; // 0 = idle, 1 = running

        public string? JobId { get; private set; }
        public DateTime? StartedAtUtc { get; private set; }
        public DateTime? FinishedAtUtc { get; private set; }
        public DateTime? FromDate { get; private set; }
        public DateTime? ToDate { get; private set; }
        public int TotalScans { get; private set; }
        public int Processed { get; private set; }
        public int ScansWithNewChannels { get; private set; }
        public int ScansAlreadyComplete { get; private set; }
        public int ScansFailed { get; private set; }
        public int ChannelsIngested { get; private set; }
        public long BytesIngested { get; private set; }
        public string? LastError { get; private set; }

        public bool IsRunning => Volatile.Read(ref _inProgress) == 1;

        /// <summary>
        /// Atomically transition from idle → running, clearing previous run's
        /// state. Returns false (and does not mutate) if a job is already
        /// running.
        /// </summary>
        public bool TryStart(DateTime fromDate, DateTime toDate, int totalScans, out string jobId)
        {
            lock (_lock)
            {
                if (_inProgress == 1)
                {
                    jobId = JobId ?? string.Empty;
                    return false;
                }

                _inProgress = 1;
                JobId = "fs6000-raw-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                StartedAtUtc = DateTime.UtcNow;
                FinishedAtUtc = null;
                FromDate = fromDate;
                ToDate = toDate;
                TotalScans = totalScans;
                Processed = 0;
                ScansWithNewChannels = 0;
                ScansAlreadyComplete = 0;
                ScansFailed = 0;
                ChannelsIngested = 0;
                BytesIngested = 0;
                LastError = null;

                jobId = JobId;
                return true;
            }
        }

        public void RecordScan(RawChannelIngestionResult r)
        {
            lock (_lock)
            {
                Processed++;
                ChannelsIngested += r.IngestedChannels;
                BytesIngested += r.IngestedBytes;
                if (r.IngestedChannels > 0)
                {
                    ScansWithNewChannels++;
                }
                else if (r.FailedChannels > 0 || r.ErrorMessage != null)
                {
                    ScansFailed++;
                    LastError = r.LastError ?? r.ErrorMessage ?? LastError;
                }
                else
                {
                    ScansAlreadyComplete++;
                }
            }
        }

        public void RecordScanException(string message)
        {
            lock (_lock)
            {
                Processed++;
                ScansFailed++;
                LastError = message;
            }
        }

        public void Finish()
        {
            lock (_lock)
            {
                FinishedAtUtc = DateTime.UtcNow;
                _inProgress = 0;
            }
        }
    }
}
