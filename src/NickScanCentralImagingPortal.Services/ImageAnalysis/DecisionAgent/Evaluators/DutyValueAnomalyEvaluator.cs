using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Detects suspiciously low duty-to-value ratios — primary indicator of under-declaration fraud.
    /// Flags when TotalDutyPaid / sum(ItemFob) ratio is below 2% or when ItemFob is zero on weighted items.
    /// </summary>
    public class DutyValueAnomalyEvaluator : IConditionEvaluator
    {
        private const double MinDutyRatio = 0.02; // 2% minimum expected duty-to-FOB ratio

        public bool CanHandle(string conditionKey) => conditionKey == "duty_value_anomaly";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            // Calculate total FOB value from manifest items
            var totalFob = context.ManifestItems
                .Where(m => m.ItemFob.HasValue && m.ItemFob.Value > 0)
                .Sum(m => m.ItemFob!.Value);

            // Get total duty from BOE documents
            var totalDuty = context.BOEDocuments
                .Where(b => b.TotalDutyPaid.HasValue && b.TotalDutyPaid.Value > 0)
                .Sum(b => b.TotalDutyPaid!.Value);

            // Skip if we don't have enough data
            if (totalFob <= 0 && totalDuty <= 0)
                return Task.FromResult(ConditionResult.Miss("Insufficient FOB/duty data"));

            // Check 1: If we have FOB but zero/very low duty
            if (totalFob > 0 && totalDuty >= 0)
            {
                var ratio = totalDuty / totalFob;
                if ((double)ratio < MinDutyRatio)
                {
                    return Task.FromResult(ConditionResult.Hit(
                        $"Duty/FOB ratio {ratio:P2} < {MinDutyRatio:P0} threshold (Duty=${totalDuty:N2}, FOB=${totalFob:N2})"));
                }
            }

            // Check 2: Items with weight but zero FOB value
            var zeroValueItems = context.ManifestItems
                .Where(m => m.Weight.HasValue && m.Weight.Value > 100) // heavy items
                .Where(m => !m.ItemFob.HasValue || m.ItemFob.Value <= 0)
                .ToList();

            if (zeroValueItems.Any())
            {
                return Task.FromResult(ConditionResult.Hit(
                    $"{zeroValueItems.Count} heavy item(s) with zero FOB value"));
            }

            return Task.FromResult(ConditionResult.Miss(
                $"Duty/FOB ratio={(totalFob > 0 ? (totalDuty / totalFob) : 0):P2}"));
        }
    }
}
