using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Orchestrates loading all data needed for an audit review view.
    /// </summary>
    public class AuditReviewViewPreloader
    {
        private readonly ViewContextCache _viewContextCache;
        private readonly ILogger<AuditReviewViewPreloader> _logger;
        private readonly IConfiguration? _configuration;

        public AuditReviewViewPreloader(
            ViewContextCache viewContextCache,
            ILogger<AuditReviewViewPreloader> logger,
            IConfiguration? configuration = null)
        {
            _viewContextCache = viewContextCache;
            _logger = logger;
            _configuration = configuration;
        }

        private bool IsPreloadingEnabled() =>
            _configuration?.GetValue<bool>("ViewContextPreloading:Enabled", true) ?? true;
    }
}

