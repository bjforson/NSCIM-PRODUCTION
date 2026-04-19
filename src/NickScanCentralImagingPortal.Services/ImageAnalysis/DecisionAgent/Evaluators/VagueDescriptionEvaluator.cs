using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags vague/generic goods descriptions that may conceal actual cargo contents.
    /// Pattern list is configurable via the condition's DynamicValue (comma-separated phrases).
    /// </summary>
    public class VagueDescriptionEvaluator : IConditionEvaluator
    {
        private static readonly string[] DefaultPatterns = {
            "personal effects", "general cargo", "miscellaneous", "household goods",
            "various", "sundry", "mixed", "assorted", "general merchandise"
        };

        public bool CanHandle(string conditionKey) => conditionKey == "vague_description";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var patterns = DefaultPatterns;
            if (!string.IsNullOrWhiteSpace(context.CurrentCondition?.DynamicValue))
            {
                patterns = context.CurrentCondition.DynamicValue
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => p.ToLowerInvariant())
                    .ToArray();
            }

            var allDescriptions = context.BOEDocuments
                .Select(b => b.GoodsDescription)
                .Concat(context.ManifestItems.Select(m => m.Description))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            foreach (var desc in allDescriptions)
            {
                var lower = desc!.ToLowerInvariant();
                var matched = patterns.FirstOrDefault(p => lower.Contains(p));
                if (matched != null)
                {
                    return Task.FromResult(ConditionResult.Hit(
                        $"Vague pattern '{matched}' in: {desc.Substring(0, Math.Min(80, desc.Length))}"));
                }
            }

            return Task.FromResult(ConditionResult.Miss($"{allDescriptions.Count} description(s) checked"));
        }
    }
}
