using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Repositories;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Helper for calculating completeness of consolidated vs non-consolidated cargo
    /// </summary>
    public static class ConsolidatedCargoCompletenessHelper
    {
        /// <summary>
        /// Calculate completeness for a container considering consolidated cargo rules
        /// </summary>
        public static Task<ConsolidatedCargoCompleteness> CalculateConsolidatedCompletenessAsync(
            string containerNumber,
            List<BOEDocument> allBOEsForContainer)
        {
            var result = new ConsolidatedCargoCompleteness
            {
                ContainerNumber = containerNumber
            };

            if (!allBOEsForContainer.Any())
            {
                result.IsComplete = false;
                result.CompletionPercentage = 0;
                result.MissingData.Add("No ICUMS data found for container");
                return Task.FromResult(result);
            }

            // Detect cargo type from first BOE
            var isConsolidated = allBOEsForContainer.Any(b => b.IsConsolidated);
            result.IsConsolidated = isConsolidated;

            if (isConsolidated)
            {
                // CONSOLIDATED: Check ALL House BLs are complete
                result.TotalHouseBLs = allBOEsForContainer.Count;
                result.HouseBLDetails = new List<HouseBLCompleteness>();

                foreach (var boe in allBOEsForContainer)
                {
                    var houseBLCompleteness = CheckSingleHouseBLCompleteness(boe);
                    result.HouseBLDetails.Add(houseBLCompleteness);

                    if (houseBLCompleteness.IsComplete)
                    {
                        result.CompletedHouseBLs++;
                    }
                }

                // Container is complete only if ALL House BLs are complete
                result.IsComplete = result.CompletedHouseBLs == result.TotalHouseBLs;
                result.CompletionPercentage = result.TotalHouseBLs > 0
                    ? (result.CompletedHouseBLs * 100.0 / result.TotalHouseBLs)
                    : 0;

                if (!result.IsComplete)
                {
                    result.MissingData.Add($"{result.TotalHouseBLs - result.CompletedHouseBLs} House BL(s) incomplete");
                }
            }
            else
            {
                // NON-CONSOLIDATED: Single BOE (may cover multiple containers)
                // Just check if this one BOE is complete
                var boe = allBOEsForContainer.First(); // All should be same BOE
                var boeCompleteness = CheckSingleBOECompleteness(boe);

                result.IsComplete = boeCompleteness.IsComplete;
                result.CompletionPercentage = boeCompleteness.CompletionPercentage;
                result.MissingData = boeCompleteness.MissingFields;
                result.NoOfContainersInBOE = boe.NoOfContainers ?? 1;
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// Check completeness of a single House BL
        /// </summary>
        private static HouseBLCompleteness CheckSingleHouseBLCompleteness(BOEDocument boe)
        {
            var result = new HouseBLCompleteness
            {
                HouseBL = boe.HouseBl ?? "",
                MasterBL = boe.BlNumber ?? "",
                DeclarationNumber = boe.DeclarationNumber ?? "",
                ConsigneeName = boe.ConsigneeName ?? "",
                ClearanceType = boe.ClearanceType ?? ""
            };

            var missingFields = new List<string>();
            int score = 0;
            int totalFields = 0;

            // Critical fields for House BL
            totalFields++; if (!string.IsNullOrEmpty(boe.HouseBl)) score++; else missingFields.Add("House BL");
            totalFields++; if (!string.IsNullOrEmpty(boe.BlNumber)) score++; else missingFields.Add("Master BL");
            totalFields++; if (!string.IsNullOrEmpty(boe.DeclarationNumber)) score++; else missingFields.Add("Declaration Number");
            totalFields++; if (!string.IsNullOrEmpty(boe.RotationNumber)) score++; else missingFields.Add("Rotation Number");
            totalFields++; if (!string.IsNullOrEmpty(boe.ConsigneeName)) score++; else missingFields.Add("Consignee Name");
            totalFields++; if (!string.IsNullOrEmpty(boe.GoodsDescription)) score++; else missingFields.Add("Goods Description");

            result.CompletionPercentage = totalFields > 0 ? (score * 100.0 / totalFields) : 0;
            result.IsComplete = result.CompletionPercentage >= 90; // 90% threshold
            result.MissingFields = missingFields;

            return result;
        }

        /// <summary>
        /// Check completeness of a single BOE (non-consolidated)
        /// </summary>
        private static SingleBOECompleteness CheckSingleBOECompleteness(BOEDocument boe)
        {
            var result = new SingleBOECompleteness();
            var missingFields = new List<string>();
            int score = 0;
            int totalFields = 0;

            // Critical fields for BOE
            totalFields++; if (!string.IsNullOrEmpty(boe.DeclarationNumber)) score++; else missingFields.Add("Declaration Number");
            totalFields++; if (!string.IsNullOrEmpty(boe.BlNumber)) score++; else missingFields.Add("BL Number");
            totalFields++; if (!string.IsNullOrEmpty(boe.RotationNumber)) score++; else missingFields.Add("Rotation Number");
            totalFields++; if (!string.IsNullOrEmpty(boe.ConsigneeName)) score++; else missingFields.Add("Consignee Name");
            totalFields++; if (!string.IsNullOrEmpty(boe.GoodsDescription)) score++; else missingFields.Add("Goods Description");
            totalFields++; if (!string.IsNullOrEmpty(boe.ContainerNumber)) score++; else missingFields.Add("Container Number");

            result.CompletionPercentage = totalFields > 0 ? (score * 100.0 / totalFields) : 0;
            result.IsComplete = result.CompletionPercentage >= 90;
            result.MissingFields = missingFields;

            return result;
        }
    }

    /// <summary>
    /// Completeness result for consolidated or non-consolidated cargo
    /// </summary>
    public class ConsolidatedCargoCompleteness
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public bool IsConsolidated { get; set; }
        public bool IsComplete { get; set; }
        public double CompletionPercentage { get; set; }
        public List<string> MissingData { get; set; } = new();

        // For Consolidated Cargo
        public int TotalHouseBLs { get; set; }
        public int CompletedHouseBLs { get; set; }
        public List<HouseBLCompleteness> HouseBLDetails { get; set; } = new();

        // For Non-Consolidated Cargo
        public int NoOfContainersInBOE { get; set; }
    }

    /// <summary>
    /// Completeness of a single House BL within consolidated cargo
    /// </summary>
    public class HouseBLCompleteness
    {
        public string HouseBL { get; set; } = string.Empty;
        public string MasterBL { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public string ConsigneeName { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public double CompletionPercentage { get; set; }
        public List<string> MissingFields { get; set; } = new();
    }

    /// <summary>
    /// Completeness of a single BOE (non-consolidated)
    /// </summary>
    public class SingleBOECompleteness
    {
        public bool IsComplete { get; set; }
        public double CompletionPercentage { get; set; }
        public List<string> MissingFields { get; set; } = new();
    }
}







