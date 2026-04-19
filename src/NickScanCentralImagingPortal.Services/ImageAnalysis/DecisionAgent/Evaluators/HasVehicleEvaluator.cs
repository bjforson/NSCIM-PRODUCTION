using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionAgent.Evaluators
{
    /// <summary>
    /// Flags cargo containing vehicles — high-duty commodities prone to undervaluation.
    /// Checks both VehicleImports table AND manifest item HS codes (chapter 87).
    /// </summary>
    public class HasVehicleEvaluator : IConditionEvaluator
    {
        // HS chapter 87 = vehicles, trailers, cycles
        private static readonly string[] VehicleHsChapters = { "87" };
        // More specific prefixes for common vehicle types
        private static readonly string[] VehicleHsPrefixes = { "8703", "8704", "8711", "8702", "8701" };

        public bool CanHandle(string conditionKey) => conditionKey == "has_vehicle";

        public Task<ConditionResult> EvaluateAsync(DecisionAgentContext context, CancellationToken ct)
        {
            // Check 1: VehicleImports table (explicit vehicle records)
            if (context.VehicleImports.Any())
            {
                var summary = context.VehicleImports
                    .Select(v => $"{v.Make} {v.Model} ({v.VehicleYear})".Trim())
                    .Take(3);
                return Task.FromResult(ConditionResult.Hit($"{context.VehicleImports.Count} vehicle(s): {string.Join("; ", summary)}"));
            }

            // Check 2: Manifest items with vehicle HS codes (fallback)
            var vehicleItems = context.ManifestItems
                .Where(m => !string.IsNullOrWhiteSpace(m.HsCode))
                .Where(m =>
                {
                    var code = m.HsCode!.Trim();
                    if (code.Length < 2) return false;
                    var chapter = code.Substring(0, 2);
                    if (VehicleHsChapters.Contains(chapter)) return true;
                    if (code.Length >= 4)
                    {
                        var prefix = code.Substring(0, 4);
                        return VehicleHsPrefixes.Contains(prefix);
                    }
                    return false;
                })
                .ToList();

            if (vehicleItems.Any())
            {
                var descs = vehicleItems
                    .Where(m => !string.IsNullOrWhiteSpace(m.Description))
                    .Select(m => m.Description!.Trim().Length > 60 ? m.Description!.Trim().Substring(0, 60) + "..." : m.Description!.Trim())
                    .Take(3);
                var hsCodes = vehicleItems.Select(m => m.HsCode!.Trim()).Distinct().Take(3);
                return Task.FromResult(ConditionResult.Hit(
                    $"{vehicleItems.Count} vehicle item(s) by HS code ({string.Join(",", hsCodes)}): {string.Join("; ", descs)}"));
            }

            // Check 3: Goods description keywords (last resort)
            var goodsDesc = context.BOEDocuments
                .Where(b => !string.IsNullOrWhiteSpace(b.GoodsDescription))
                .Select(b => b.GoodsDescription!.ToUpperInvariant())
                .FirstOrDefault();

            if (goodsDesc != null)
            {
                var vehicleKeywords = new[] { "TOYOTA", "HILUX", "MITSUBISHI", "NISSAN", "HYUNDAI", "KIA",
                    "PICKUP", "SEDAN", "SUV", "TRUCK", "MOTORCYCLE", "MOTORBIKE", "TRICYCLE",
                    "MOTOR VEHICLE", "MOTOR CAR", "DOUBLE CABIN" };

                var matched = vehicleKeywords.Where(k => goodsDesc.Contains(k)).ToList();
                if (matched.Any())
                {
                    return Task.FromResult(ConditionResult.Hit(
                        $"Vehicle keywords in goods description: {string.Join(", ", matched.Take(3))}"));
                }
            }

            return Task.FromResult(ConditionResult.Miss("No vehicles"));
        }
    }
}
