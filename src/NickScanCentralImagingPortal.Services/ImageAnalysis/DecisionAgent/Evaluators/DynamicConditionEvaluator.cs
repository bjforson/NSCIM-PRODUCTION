using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Evaluates user-defined dynamic conditions at runtime.
    /// Uses DynamicFieldPath to resolve a value from the context, then applies
    /// DynamicOperator with DynamicValue to determine a match.
    ///
    /// Supported field paths: BOEDocument.{PropertyName}, ManifestItem.{PropertyName}
    /// Supported operators: Contains, Equals, StartsWith, GreaterThan, Regex, Exists
    /// </summary>
    public class DynamicConditionEvaluator : IConditionEvaluator
    {
        // This evaluator handles any condition with EvaluatorType = "Dynamic"
        // The CanHandle check is done by condition key starting with "dynamic_" or fallback
        public bool CanHandle(string conditionKey) => false; // handled via special dispatch in worker

        /// <summary>
        /// Checks if this evaluator should handle the given condition (called by worker for Dynamic type).
        /// </summary>
        public static bool IsDynamic(string evaluatorType) =>
            string.Equals(evaluatorType, "Dynamic", StringComparison.OrdinalIgnoreCase);

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            var condition = context.CurrentCondition;
            if (condition == null || string.IsNullOrWhiteSpace(condition.DynamicFieldPath))
                return Task.FromResult(ConditionResult.Miss("No field path configured"));

            var fieldPath = condition.DynamicFieldPath.Trim();
            var op = condition.DynamicOperator?.Trim() ?? "Contains";
            var targetValue = condition.DynamicValue ?? "";

            // Resolve field values from context objects
            var resolvedValues = ResolveFieldValues(context, fieldPath);

            if (!resolvedValues.Any())
            {
                if (string.Equals(op, "Exists", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(ConditionResult.Miss($"Field '{fieldPath}' not found or empty"));
                return Task.FromResult(ConditionResult.Miss($"No values for '{fieldPath}'"));
            }

            // For "Exists" operator, presence alone is a match
            if (string.Equals(op, "Exists", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(ConditionResult.Hit($"Field '{fieldPath}' exists with {resolvedValues.Count} value(s)"));

            // Apply operator against each resolved value
            foreach (var value in resolvedValues)
            {
                if (ApplyOperator(value, op, targetValue))
                    return Task.FromResult(ConditionResult.Hit($"{fieldPath}={value} matched {op} '{targetValue}'"));
            }

            return Task.FromResult(ConditionResult.Miss($"{resolvedValues.Count} value(s) checked, none matched"));
        }

        private static List<string> ResolveFieldValues(DecisionAgentContext context, string fieldPath)
        {
            var parts = fieldPath.Split('.', 2);
            if (parts.Length != 2) return new List<string>();

            var objectType = parts[0].Trim();
            var propertyName = parts[1].Trim();

            return objectType.ToLowerInvariant() switch
            {
                "boedocument" => ExtractPropertyValues(context.BOEDocuments, propertyName),
                "manifestitem" or "downloadedmanifestitem" => ExtractPropertyValues(context.ManifestItems, propertyName),
                "vehicleimport" => ExtractPropertyValues(context.VehicleImports, propertyName),
                _ => new List<string>()
            };
        }

        private static List<string> ExtractPropertyValues<T>(List<T> objects, string propertyName) where T : class
        {
            if (!objects.Any()) return new List<string>();

            var type = typeof(T);
            var prop = type.GetProperty(propertyName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop == null) return new List<string>();

            return objects
                .Select(obj => prop.GetValue(obj)?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();
        }

        private static bool ApplyOperator(string value, string op, string target)
        {
            return op.ToLowerInvariant() switch
            {
                "contains" => value.Contains(target, StringComparison.OrdinalIgnoreCase),
                "equals" => string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
                "startswith" => value.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                "greaterthan" => double.TryParse(value, out var v) && double.TryParse(target, out var t) && v > t,
                "regex" => TryRegexMatch(value, target),
                _ => false
            };
        }

        private static bool TryRegexMatch(string value, string pattern)
        {
            try
            {
                return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch
            {
                return false;
            }
        }
    }
}
