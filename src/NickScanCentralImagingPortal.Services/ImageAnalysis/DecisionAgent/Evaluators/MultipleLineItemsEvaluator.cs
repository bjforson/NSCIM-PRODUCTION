using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags declarations with more than 3 line items per BOE — complexity increases concealment opportunity.
    /// </summary>
    public class MultipleLineItemsEvaluator : IConditionEvaluator
    {
        private const int Threshold = 3;

        public bool CanHandle(string conditionKey) => conditionKey == "multiple_line_items";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var groupedByBoe = context.ManifestItems
                .GroupBy(m => m.BOEDocumentId)
                .Select(g => new { BoeId = g.Key, Count = g.Count() })
                .Where(g => g.Count > Threshold)
                .ToList();

            if (groupedByBoe.Any())
            {
                var maxCount = groupedByBoe.Max(g => g.Count);
                return Task.FromResult(ConditionResult.Hit(
                    $"{groupedByBoe.Count} BOE(s) with >{Threshold} line items (max {maxCount})"));
            }

            var totalItems = context.ManifestItems.Count;
            return Task.FromResult(ConditionResult.Miss($"{totalItems} total line item(s)"));
        }
    }
}
