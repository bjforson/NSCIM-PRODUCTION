using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent
{
    /// <summary>
    /// Evaluates a single risk condition against a cargo group's data.
    /// </summary>
    public interface IConditionEvaluator
    {
        /// <summary>The condition key(s) this evaluator handles (e.g. "risk_red").</summary>
        bool CanHandle(string conditionKey);

        /// <summary>Evaluate the condition and return whether it matched.</summary>
        Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct);
    }

    /// <summary>
    /// Result of evaluating a single condition.
    /// </summary>
    public class ConditionResult
    {
        public bool Matched { get; set; }

        /// <summary>Raw evidence for audit logging (e.g. "CrmsLevel=Red", "CountryOfOrigin=VE").</summary>
        public string? RawValue { get; set; }

        public static ConditionResult Hit(string rawValue) => new() { Matched = true, RawValue = rawValue };
        public static ConditionResult Miss(string? rawValue = null) => new() { Matched = false, RawValue = rawValue };
    }

    /// <summary>
    /// All data needed to evaluate conditions for a single AnalysisGroup.
    /// Built once per group, shared across all evaluators.
    /// </summary>
    public class DecisionAgentContext
    {
        public required AnalysisGroup Group { get; set; }
        public required List<AnalysisRecord> Records { get; set; }
        public required List<BOEDocument> BOEDocuments { get; set; }
        public required List<DownloadedManifestItem> ManifestItems { get; set; }
        public required List<NickScanCentralImagingPortal.Core.Models.VehicleImport> VehicleImports { get; set; }

        /// <summary>The specific condition being evaluated (provides access to DynamicValue for configurable lists).</summary>
        public DecisionAgentCondition? CurrentCondition { get; set; }
    }

    /// <summary>
    /// Per-condition scoring result used in the audit log JSON.
    /// </summary>
    public class ConditionScoreEntry
    {
        public string ConditionKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Matched { get; set; }
        public double Weight { get; set; }
        public string? RawValue { get; set; }
    }
}
