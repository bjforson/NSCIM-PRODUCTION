using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services
{
    public class ScannerWorkflowGate : IScannerWorkflowGate
    {
        private readonly HashSet<string> _disabledAssignmentIntakeTypes;
        private readonly bool _eagleAssignmentEnabled;
        private readonly bool _eagleSplitEnabled;

        public ScannerWorkflowGate(IConfiguration configuration)
        {
            _disabledAssignmentIntakeTypes = configuration
                .GetSection("ScannerWorkflow:DisabledAssignmentIntakeScannerTypes")
                .Get<string[]>()?
                .Select(Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _eagleAssignmentEnabled = configuration.GetValue<bool>("ScannerWorkflow:EagleA25:AssignmentIntakeEnabled", false);
            _eagleSplitEnabled = configuration.GetValue<bool>("ScannerWorkflow:EagleA25:SplitIntakeEnabled", false);

            if (!_eagleAssignmentEnabled)
            {
                _disabledAssignmentIntakeTypes.Add(Normalize(CommonScannerTypes.EagleA25));
            }
        }

        public bool IsAssignmentIntakeEnabled(string? scannerType)
        {
            var normalized = Normalize(scannerType);
            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            if (normalized == Normalize(CommonScannerTypes.EagleA25))
            {
                return _eagleAssignmentEnabled;
            }

            return !_disabledAssignmentIntakeTypes.Contains(normalized);
        }

        public bool IsSplitIntakeEnabled(string? scannerType)
        {
            var normalized = Normalize(scannerType);
            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            if (normalized == Normalize(CommonScannerTypes.EagleA25))
            {
                return _eagleSplitEnabled;
            }

            return true;
        }

        private static string Normalize(string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
            {
                return string.Empty;
            }

            var compact = scannerType
                .Trim()
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);

            return compact.Equals("EagleA25", StringComparison.OrdinalIgnoreCase)
                ? "EagleA25"
                : compact.ToUpperInvariant();
        }
    }
}
