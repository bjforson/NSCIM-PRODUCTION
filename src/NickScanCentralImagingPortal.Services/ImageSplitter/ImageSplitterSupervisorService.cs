using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    /// <summary>
    /// Spawns and supervises the Python image-splitter subprocess (FastAPI/Uvicorn
    /// on port 5320) as a child of NSCIM_API. Replaces the prior NSSM-managed
    /// <c>NSCIM_ImageSplitter</c> / <c>NSCIM_RawImageEngine</c> Windows service so
    /// that the splitter's lifecycle is tied to NSCIM_API's: one service to start,
    /// stop, and monitor.
    ///
    /// What this supervisor does:
    /// 1. After a short startup delay (let NSCIM_API get healthy first), launches
    ///    <c>python.exe -m uvicorn main:app --host 0.0.0.0 --port 5320</c> from the
    ///    image-splitter working directory using its venv.
    /// 2. Streams child stdout/stderr into NSCIM_API's Serilog pipeline so all
    ///    splitter logs flow into <c>Data\Logs\nickscan-*.txt</c> — no separate
    ///    log file to babysit.
    /// 3. Polls <c>IImageSplitterService.IsHealthyAsync</c> periodically to catch
    ///    hangs (process alive but unresponsive), in addition to process-exit
    ///    detection.
    /// 4. On child exit, applies a linear-with-cap backoff (3s → 6s → 12s → 24s,
    ///    capped at 60s). Backoff resets after a stable 5-minute run.
    /// 5. On NSCIM_API shutdown, terminates the child process tree. Uvicorn
    ///    doesn't have a clean Windows-signal path, so <c>Process.Kill(true)</c>
    ///    is the pragmatic choice; SQLAlchemy writes its transactions atomically
    ///    so abrupt termination does not corrupt split jobs.
    ///
    /// Configurable via <c>ImageSplitter:Supervisor:*</c> in appsettings.json:
    /// <list type="bullet">
    ///   <item><c>Enabled</c> (bool, default <c>true</c>) — master switch; set to
    ///     <c>false</c> to have NSCIM_API leave the Python process alone (e.g.
    ///     during a rewrite window where you're managing it by hand).</item>
    ///   <item><c>PythonExecutable</c> (string) — default resolves to
    ///     <c>&lt;WorkingDirectory&gt;\venv\Scripts\python.exe</c>.</item>
    ///   <item><c>WorkingDirectory</c> (string) — default
    ///     <c>C:\Shared\NSCIM_PRODUCTION\services\image-splitter</c>.</item>
    ///   <item><c>Port</c> (int, default <c>5320</c>) — passed to uvicorn.</item>
    ///   <item><c>StartupDelaySeconds</c> (int, default <c>10</c>).</item>
    ///   <item><c>HealthCheckIntervalSeconds</c> (int, default <c>60</c>).</item>
    ///   <item><c>ShutdownTimeoutSeconds</c> (int, default <c>15</c>) — how long
    ///     to wait for a graceful exit before Kill(true).</item>
    /// </list>
    ///
    /// Deprecation note: this service replaces
    /// <c>ImageSplitterHealthMonitorService</c> (removed same release). The
    /// NSSM-registered <c>NSCIM_ImageSplitter</c> / <c>NSCIM_RawImageEngine</c>
    /// Windows service must be stopped + removed (<c>nssm remove ... confirm</c>)
    /// to avoid two processes racing for port 5320. See CHANGELOG for the
    /// accompanying ops step.
    /// </summary>
    public class ImageSplitterSupervisorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ImageSplitterSupervisorService> _logger;

        private readonly bool _enabled;
        private readonly string _pythonExe;
        private readonly string _workingDir;
        private readonly int _port;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _healthCheckInterval;
        private readonly TimeSpan _shutdownTimeout;

        private Process? _child;
        private DateTime _childStartedAtUtc = DateTime.MinValue;
        private int _consecutiveCrashes;

        public ImageSplitterSupervisorService(
            IServiceProvider serviceProvider,
            ILogger<ImageSplitterSupervisorService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            _enabled = configuration.GetValue("ImageSplitter:Supervisor:Enabled", true);

            _workingDir = configuration.GetValue<string>("ImageSplitter:Supervisor:WorkingDirectory",
                "C:\\Shared\\NSCIM_PRODUCTION\\services\\image-splitter") ?? "";

            var defaultPython = Path.Combine(_workingDir, "venv", "Scripts", "python.exe");
            _pythonExe = configuration.GetValue<string>("ImageSplitter:Supervisor:PythonExecutable", defaultPython) ?? defaultPython;

            _port = configuration.GetValue("ImageSplitter:Supervisor:Port", 5320);
            _startupDelay = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("ImageSplitter:Supervisor:StartupDelaySeconds", 10)));
            _healthCheckInterval = TimeSpan.FromSeconds(Math.Max(10, configuration.GetValue("ImageSplitter:Supervisor:HealthCheckIntervalSeconds", 60)));
            _shutdownTimeout = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("ImageSplitter:Supervisor:ShutdownTimeoutSeconds", 15)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("[SPLITTER-SUPERVISOR] Disabled via configuration (ImageSplitter:Supervisor:Enabled=false). Not spawning the Python subprocess.");
                return;
            }

            if (!File.Exists(_pythonExe))
            {
                _logger.LogError(
                    "[SPLITTER-SUPERVISOR] Python executable not found at '{PythonExe}'. Supervisor cannot start; splitter will be unavailable until path is fixed or NSSM service is restored.",
                    _pythonExe);
                return;
            }
            if (!Directory.Exists(_workingDir))
            {
                _logger.LogError(
                    "[SPLITTER-SUPERVISOR] Working directory not found at '{WorkingDir}'. Supervisor cannot start.",
                    _workingDir);
                return;
            }

            _logger.LogInformation(
                "[SPLITTER-SUPERVISOR] Starting. Python={PythonExe}, WorkDir={WorkDir}, Port={Port}, StartupDelay={Delay}",
                _pythonExe, _workingDir, _port, _startupDelay);

            try { await Task.Delay(_startupDelay, stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunChildOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SPLITTER-SUPERVISOR] Unexpected error in supervisor loop");
                }

                if (stoppingToken.IsCancellationRequested) break;

                // Backoff before respawn. Linear-with-cap, resets after stable run.
                var uptime = DateTime.UtcNow - _childStartedAtUtc;
                if (uptime >= TimeSpan.FromMinutes(5))
                {
                    _consecutiveCrashes = 0;
                }
                else
                {
                    _consecutiveCrashes = Math.Min(_consecutiveCrashes + 1, 5);
                }

                var backoffSeconds = Math.Min(60, 3 * (1 << (_consecutiveCrashes - 1 < 0 ? 0 : _consecutiveCrashes - 1)));
                _logger.LogWarning(
                    "[SPLITTER-SUPERVISOR] Child exited (consecutiveCrashes={Crashes}, lastUptime={Uptime}). Restarting in {Backoff}s.",
                    _consecutiveCrashes, uptime, backoffSeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken); }
                catch (TaskCanceledException) { break; }
            }

            await TerminateChildAsync();
            _logger.LogInformation("[SPLITTER-SUPERVISOR] Stopped.");
        }

        private async Task RunChildOnceAsync(CancellationToken stoppingToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pythonExe,
                WorkingDirectory = _workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("uvicorn");
            psi.ArgumentList.Add("main:app");
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add("0.0.0.0");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(_port.ToString());

            _child = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _child.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogInformation("[SPLITTER] {Line}", e.Data); };
            _child.ErrorDataReceived  += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogWarning("[SPLITTER/err] {Line}", e.Data); };

            if (!_child.Start())
            {
                throw new InvalidOperationException("Process.Start returned false");
            }
            _childStartedAtUtc = DateTime.UtcNow;
            _child.BeginOutputReadLine();
            _child.BeginErrorReadLine();

            _logger.LogInformation("[SPLITTER-SUPERVISOR] Spawned Python PID={Pid}", _child.Id);

            // Wait for either the child to exit or the supervisor to be cancelled,
            // polling health in between so a hung child (process up, endpoint dead)
            // still gets detected and restarted.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var exitTask = _child.WaitForExitAsync(cts.Token);

            while (!exitTask.IsCompleted && !stoppingToken.IsCancellationRequested)
            {
                var delay = Task.Delay(_healthCheckInterval, cts.Token);
                var completed = await Task.WhenAny(exitTask, delay);
                if (completed == exitTask) break;

                // Child still running — check health
                bool healthy;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var splitter = scope.ServiceProvider.GetRequiredService<IImageSplitterService>();
                    healthy = await splitter.IsHealthyAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SPLITTER-SUPERVISOR] Health probe error (treating as unhealthy)");
                    healthy = false;
                }

                if (!healthy)
                {
                    var age = DateTime.UtcNow - _childStartedAtUtc;
                    if (age < TimeSpan.FromSeconds(20))
                    {
                        _logger.LogDebug("[SPLITTER-SUPERVISOR] Child still starting (age={Age}); health not required yet.", age);
                        continue;
                    }
                    _logger.LogWarning(
                        "[SPLITTER-SUPERVISOR] Child PID={Pid} is unresponsive to health probe after {Age}. Killing to force restart.",
                        _child.Id, age);
                    cts.Cancel();
                    try { _child.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    break;
                }
            }

            try { await exitTask; } catch (OperationCanceledException) { }
            var exitCode = _child?.HasExited == true ? _child.ExitCode : -1;
            _logger.LogInformation("[SPLITTER-SUPERVISOR] Child exited (PID={Pid}, exitCode={ExitCode}, uptime={Uptime}).",
                _child?.Id, exitCode, DateTime.UtcNow - _childStartedAtUtc);

            _child?.Dispose();
            _child = null;
        }

        private async Task TerminateChildAsync()
        {
            var c = _child;
            if (c == null) return;
            try
            {
                if (!c.HasExited)
                {
                    _logger.LogInformation("[SPLITTER-SUPERVISOR] Terminating Python child PID={Pid}.", c.Id);
                    // Uvicorn on Windows has no clean signal path; kill the whole
                    // process tree (Uvicorn spawns worker/reloader processes).
                    c.Kill(entireProcessTree: true);
                    try
                    {
                        await c.WaitForExitAsync(new CancellationTokenSource(_shutdownTimeout).Token);
                    }
                    catch (OperationCanceledException) { /* timed out; OS will reap */ }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SPLITTER-SUPERVISOR] Error while terminating child.");
            }
            finally
            {
                try { c.Dispose(); } catch { }
                _child = null;
            }
        }
    }
}
