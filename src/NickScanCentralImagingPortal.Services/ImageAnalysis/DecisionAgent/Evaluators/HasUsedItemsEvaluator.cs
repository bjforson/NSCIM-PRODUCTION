using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Detects used/second-hand items via:
    /// 1. Description keywords: "used", "second-hand", "pre-owned", "reconditioned", "refurbished", "salvage"
    /// 2. Vehicle age: VehicleImport with year > 3 years old
    /// </summary>
    public class HasUsedItemsEvaluator : IConditionEvaluator
    {
        private static readonly string[] UsedKeywords = {
            "used", "second-hand", "secondhand", "pre-owned", "preowned",
            "reconditioned", "refurbished", "salvage", "rebuilt", "second hand"
        };

        public bool CanHandle(string conditionKey) => conditionKey == "has_used_items";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var evidence = new List<string>();

            // Check descriptions for used-item keywords
            var allDescriptions = context.BOEDocuments
                .Select(b => b.GoodsDescription)
                .Concat(context.ManifestItems.Select(m => m.Description))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            foreach (var desc in allDescriptions)
            {
                var lower = desc!.ToLowerInvariant();
                var matchedKeyword = UsedKeywords.FirstOrDefault(k => lower.Contains(k));
                if (matchedKeyword != null)
                {
                    evidence.Add($"Keyword '{matchedKeyword}' in: {desc.Substring(0, Math.Min(60, desc.Length))}...");
                    break; // one match is enough
                }
            }

            // Check vehicle age (> 3 years old = likely used)
            var currentYear = DateTime.UtcNow.Year;
            foreach (var vehicle in context.VehicleImports)
            {
                if (int.TryParse(vehicle.VehicleYear, out var year) && year > 0 && (currentYear - year) > 3)
                {
                    evidence.Add($"Vehicle year {year} ({currentYear - year} years old): {vehicle.Make} {vehicle.Model}");
                    break;
                }
            }

            if (evidence.Any())
                return Task.FromResult(ConditionResult.Hit(string.Join(" | ", evidence)));

            return Task.FromResult(ConditionResult.Miss("No used items detected"));
        }
    }
}
