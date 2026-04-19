using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags cargo from high-risk origin countries.
    /// Country list is configurable via the condition's DynamicValue (comma-separated ISO codes).
    /// </summary>
    public class HighRiskCountryEvaluator : IConditionEvaluator
    {
        public bool CanHandle(string conditionKey) => conditionKey == "high_risk_country";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var countryList = context.CurrentCondition?.DynamicValue;
            if (string.IsNullOrWhiteSpace(countryList))
                return Task.FromResult(ConditionResult.Miss("No country list configured"));

            var highRiskCountries = countryList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .ToHashSet();

            var matchingOrigins = context.BOEDocuments
                .Where(b => !string.IsNullOrWhiteSpace(b.CountryOfOrigin))
                .Where(b => highRiskCountries.Contains(b.CountryOfOrigin!.Trim().ToUpperInvariant()))
                .Select(b => b.CountryOfOrigin!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            // Also check manifest-level country of origin
            var manifestOrigins = context.ManifestItems
                .Where(m => !string.IsNullOrWhiteSpace(m.CountryOfOrigin))
                .Where(m => highRiskCountries.Contains(m.CountryOfOrigin!.Trim().ToUpperInvariant()))
                .Select(m => m.CountryOfOrigin!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            var allMatches = matchingOrigins.Union(manifestOrigins).Distinct().ToList();

            if (allMatches.Any())
                return Task.FromResult(ConditionResult.Hit($"High-risk origin(s): {string.Join(",", allMatches)}"));

            var origins = context.BOEDocuments
                .Where(b => !string.IsNullOrWhiteSpace(b.CountryOfOrigin))
                .Select(b => b.CountryOfOrigin)
                .Distinct();
            return Task.FromResult(ConditionResult.Miss($"Origins: [{string.Join(",", origins)}]"));
        }
    }
}
