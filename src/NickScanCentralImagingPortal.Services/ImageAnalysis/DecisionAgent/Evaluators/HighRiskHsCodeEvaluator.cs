using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags HS codes that fall within high-risk chapters.
    /// Chapter list is configurable via DynamicValue (comma-separated 2-digit chapter codes).
    /// Default: 22 (alcohol), 24 (tobacco), 28-29 (chemicals), 36 (explosives),
    /// 71 (precious metals), 84-87 (machinery/vehicles), 93 (arms/ammunition).
    /// </summary>
    public class HighRiskHsCodeEvaluator : IConditionEvaluator
    {
        private static readonly string[] DefaultChapters = {
            "22", "24", "28", "29", "36", "71", "84", "85", "86", "87", "93"
        };

        public bool CanHandle(string conditionKey) => conditionKey == "high_risk_hs_code";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var chapters = DefaultChapters;
            if (!string.IsNullOrWhiteSpace(context.CurrentCondition?.DynamicValue))
            {
                chapters = context.CurrentCondition.DynamicValue
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }

            var riskHsCodes = context.ManifestItems
                .Where(m => !string.IsNullOrWhiteSpace(m.HsCode))
                .Where(m =>
                {
                    var code = m.HsCode!.Trim();
                    if (code.Length < 2) return false;
                    var chapter = code.Substring(0, 2);
                    return chapters.Contains(chapter);
                })
                .Select(m => m.HsCode!.Trim())
                .Distinct()
                .ToList();

            if (riskHsCodes.Any())
                return Task.FromResult(ConditionResult.Hit($"High-risk HS codes: {string.Join(",", riskHsCodes.Take(5))}"));

            var allCodes = context.ManifestItems
                .Where(m => !string.IsNullOrWhiteSpace(m.HsCode))
                .Select(m => m.HsCode!.Trim().Substring(0, Math.Min(4, m.HsCode!.Trim().Length)))
                .Distinct();
            return Task.FromResult(ConditionResult.Miss($"HS code prefixes: [{string.Join(",", allCodes)}]"));
        }
    }
}
