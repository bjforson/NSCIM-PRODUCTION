using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent
{
    /// <summary>
    /// Computes a weighted risk score for a cargo group by evaluating all enabled conditions.
    /// Score 0.0 = no risk flags matched, 1.0 = all risk flags matched (weighted).
    /// </summary>
    public static class DecisionAgentScoringEngine
    {
        /// <summary>
        /// Evaluate all conditions against the context and return a weighted score.
        /// </summary>
        public static async Task<ScoringResult> ScoreAsync(
            DecisionAgentContext context,
            List<DecisionAgentCondition> conditions,
            List<IConditionEvaluator> evaluators,
            ILogger logger,
            CancellationToken ct)
        {
            var entries = new List<ConditionScoreEntry>();
            double weightedScore = 0;
            double totalWeight = 0;

            foreach (var condition in conditions.Where(c => c.Enabled).OrderBy(c => c.SortOrder))
            {
                var evaluator = evaluators.FirstOrDefault(e => e.CanHandle(condition.ConditionKey));
                if (evaluator == null)
                {
                    logger.LogWarning("[DECISION-AGENT-SCORING] No evaluator found for condition '{Key}', skipping", condition.ConditionKey);
                    continue;
                }

                try
                {
                    context.CurrentCondition = condition;
                    var result = await evaluator.EvaluateAsync(context, ct);

                    var entry = new ConditionScoreEntry
                    {
                        ConditionKey = condition.ConditionKey,
                        Name = condition.Name,
                        Matched = result.Matched,
                        Weight = condition.Weight,
                        RawValue = result.RawValue
                    };
                    entries.Add(entry);

                    totalWeight += condition.Weight;
                    if (result.Matched)
                        weightedScore += condition.Weight;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[DECISION-AGENT-SCORING] Error evaluating condition '{Key}' for group {Group}",
                        condition.ConditionKey, context.Group.GroupIdentifier);

                    entries.Add(new ConditionScoreEntry
                    {
                        ConditionKey = condition.ConditionKey,
                        Name = condition.Name,
                        Matched = false,
                        Weight = condition.Weight,
                        RawValue = $"ERROR: {ex.Message}"
                    });
                    totalWeight += condition.Weight;
                }
            }

            var finalScore = totalWeight > 0 ? weightedScore / totalWeight : 0.0;

            return new ScoringResult
            {
                Score = Math.Round(finalScore, 4),
                Entries = entries,
                TotalWeight = totalWeight,
                MatchedWeight = weightedScore
            };
        }
    }

    public class ScoringResult
    {
        /// <summary>Final weighted average score (0.0–1.0).</summary>
        public double Score { get; set; }

        /// <summary>Per-condition breakdown for audit logging.</summary>
        public List<ConditionScoreEntry> Entries { get; set; } = new();

        public double TotalWeight { get; set; }
        public double MatchedWeight { get; set; }
    }
}
