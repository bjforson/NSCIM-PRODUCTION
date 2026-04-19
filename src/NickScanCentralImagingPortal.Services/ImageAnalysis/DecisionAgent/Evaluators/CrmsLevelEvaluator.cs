using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Handles both risk_red and risk_yellow conditions.
    /// Checks BOEDocument.CrmsLevel against the target level derived from the condition key.
    /// </summary>
    public class CrmsLevelEvaluator : IConditionEvaluator
    {
        public bool CanHandle(string conditionKey) =>
            conditionKey == "risk_red" || conditionKey == "risk_yellow";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var targetLevel = context.CurrentCondition?.ConditionKey == "risk_yellow" ? "Yellow" : "Red";

            var matchingDocs = context.BOEDocuments
                .Where(b => string.Equals(b.CrmsLevel, targetLevel, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingDocs.Any())
            {
                var containers = string.Join(",", matchingDocs.Select(b => b.ContainerNumber).Distinct());
                return Task.FromResult(ConditionResult.Hit($"CrmsLevel={targetLevel} on {containers}"));
            }

            var actualLevels = context.BOEDocuments
                .Where(b => !string.IsNullOrEmpty(b.CrmsLevel))
                .Select(b => b.CrmsLevel)
                .Distinct();
            return Task.FromResult(ConditionResult.Miss($"CrmsLevels=[{string.Join(",", actualLevels)}]"));
        }
    }
}
