using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags consolidated cargo with multiple distinct House BLs —
    /// multiple consignees sharing one container increases inspection risk.
    /// </summary>
    public class MultipleHouseBLEvaluator : IConditionEvaluator
    {
        public bool CanHandle(string conditionKey) => conditionKey == "multiple_housebl";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var consolidatedDocs = context.BOEDocuments.Where(b => b.IsConsolidated).ToList();
            if (!consolidatedDocs.Any())
                return Task.FromResult(ConditionResult.Miss("Not consolidated"));

            var distinctHouseBLs = consolidatedDocs
                .Where(b => !string.IsNullOrWhiteSpace(b.HouseBl))
                .Select(b => b.HouseBl!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            if (distinctHouseBLs.Count > 1)
                return Task.FromResult(ConditionResult.Hit($"Consolidated with {distinctHouseBLs.Count} House BLs: {string.Join(",", distinctHouseBLs.Take(5))}"));

            return Task.FromResult(ConditionResult.Miss($"Consolidated with {distinctHouseBLs.Count} House BL(s)"));
        }
    }
}
