using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Local service for generating cargo summaries (no external dependencies)
    /// Processes CargoGroupDto that's already loaded in memory
    /// </summary>
    public class CargoSummaryService
    {
        public Task<CargoSummaryDto> GenerateSummaryAsync(CargoGroupDto cargoGroup)
        {
            var summaryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var textGenerationStopwatch = System.Diagnostics.Stopwatch.StartNew();

            Console.WriteLine($"⏱️ [CargoSummaryService] GenerateSummaryAsync START - GroupIdentifier: {cargoGroup.GroupIdentifier}, Type: {cargoGroup.Type}");

            try
            {
                var summary = new CargoSummaryDto
                {
                    IsConsolidated = cargoGroup.Type == CargoType.Consolidated
                };

                // Extract data from ICUMS records
                extractionStopwatch.Restart();
                var allRecords = cargoGroup.Data.ICUMSData
                    .SelectMany(g => g.Records)
                    .ToList();

                var allBOEDetails = cargoGroup.Data.ICUMSData
                    .SelectMany(g => g.BOEDetails)
                    .DistinctBy(b => b.BOEId)
                    .ToList();
                extractionStopwatch.Stop();
                Console.WriteLine($"⏱️ [CargoSummaryService] Data extraction took: {extractionStopwatch.ElapsedMilliseconds}ms, Records: {allRecords.Count}, BOEs: {allBOEDetails.Count}");

                // Extract key information
                var extractStopwatch = System.Diagnostics.Stopwatch.StartNew();
                ExtractConsignees(summary, allRecords, allBOEDetails, cargoGroup);
                ExtractGoodsDescription(summary, allRecords, allBOEDetails);
                ExtractHSCodes(summary, allRecords);
                ExtractQuantities(summary, allRecords);
                ExtractWeight(summary, allRecords);
                ExtractFOBValue(summary, allRecords);
                ExtractDutyPaid(summary, allRecords, allBOEDetails);
                ExtractCountriesOfOrigin(summary, allRecords);
                ExtractLineItemCount(summary, allRecords);
                ExtractAdditionalDetails(summary, allRecords, cargoGroup);
                extractStopwatch.Stop();
                Console.WriteLine($"⏱️ [CargoSummaryService] Field extraction took: {extractStopwatch.ElapsedMilliseconds}ms");

                // Generate formatted summary text
                textGenerationStopwatch.Restart();
                summary.SummaryText = GenerateSummaryText(summary, cargoGroup);
                textGenerationStopwatch.Stop();
                Console.WriteLine($"⏱️ [CargoSummaryService] Text generation took: {textGenerationStopwatch.ElapsedMilliseconds}ms");

                summaryStopwatch.Stop();
                Console.WriteLine($"⏱️ [CargoSummaryService] GenerateSummaryAsync COMPLETE - Total: {summaryStopwatch.ElapsedMilliseconds}ms ({summaryStopwatch.Elapsed.TotalSeconds:F2}s)");

                return Task.FromResult(summary);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating cargo summary: {ex.Message}");
                return Task.FromResult(new CargoSummaryDto
                {
                    SummaryText = "Unable to generate cargo summary at this time."
                });
            }
        }

        private void ExtractConsignees(CargoSummaryDto summary, List<ICUMSDataRecordDto> records, List<BOEDetailDto> boeDetails, CargoGroupDto cargoGroup)
        {
            var consignees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // From records
            var consigneeRecords = records
                .Where(r => r.Field.Contains("Consignee", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .Select(r => r.Value.Trim())
                .ToList();

            foreach (var consignee in consigneeRecords)
            {
                if (!string.IsNullOrWhiteSpace(consignee))
                    consignees.Add(consignee);
            }

            // From BOE details
            foreach (var boe in boeDetails)
            {
                if (!string.IsNullOrWhiteSpace(boe.ConsigneeName))
                    consignees.Add(boe.ConsigneeName);
            }

            // From cargo group
            if (!string.IsNullOrWhiteSpace(cargoGroup.ConsigneeName))
                consignees.Add(cargoGroup.ConsigneeName);

            summary.Consignees = consignees.ToList();
        }

        private void ExtractGoodsDescription(CargoSummaryDto summary, List<ICUMSDataRecordDto> records, List<BOEDetailDto> boeDetails)
        {
            var descriptions = new List<string>();

            // From records
            var descRecords = records
                .Where(r => (r.Field.Contains("Goods Description", StringComparison.OrdinalIgnoreCase) ||
                            r.Field.Contains("Item Description", StringComparison.OrdinalIgnoreCase)) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .Select(r => r.Value.Trim())
                .Distinct()
                .ToList();

            descriptions.AddRange(descRecords);

            // From BOE details
            foreach (var boe in boeDetails)
            {
                if (!string.IsNullOrWhiteSpace(boe.GoodsDescription))
                    descriptions.Add(boe.GoodsDescription);
            }

            // Show ALL unique descriptions (no truncation)
            if (descriptions.Any())
            {
                var uniqueDescriptions = descriptions.Distinct().ToList();
                summary.GoodsDescription = string.Join("; ", uniqueDescriptions);
            }

            // Extract structured line items for clean display
            var itemGroups = records
                .Where(r => r.Category == "Cargo Info" && r.Field.Contains(":"))
                .GroupBy(r =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(r.Field, @"Item\s+(\d+):");
                    return match.Success ? int.Parse(match.Groups[1].Value) : 0;
                })
                .Where(g => g.Key > 0)
                .OrderBy(g => g.Key);

            foreach (var group in itemGroups)
            {
                var desc = group.FirstOrDefault(r => r.Field.Contains("Description", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrWhiteSpace(desc) || desc == "N/A") continue;

                var hs = group.FirstOrDefault(r => r.Field.Contains("HS Code", StringComparison.OrdinalIgnoreCase))?.Value;
                var qty = group.FirstOrDefault(r => r.Field.Contains("Quantity", StringComparison.OrdinalIgnoreCase))?.Value;
                var wt = group.FirstOrDefault(r => r.Field.Contains("Weight", StringComparison.OrdinalIgnoreCase))?.Value;
                var origin = group.FirstOrDefault(r => r.Field.Contains("Country", StringComparison.OrdinalIgnoreCase))?.Value;

                summary.ItemDescriptions.Add(new LineItemSummary
                {
                    ItemNo = group.Key,
                    Description = desc.Trim(),
                    HsCode = hs,
                    Quantity = qty,
                    Weight = wt,
                    CountryOfOrigin = origin
                });
            }
        }

        private void ExtractHSCodes(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var hsCodes = new HashSet<string>();

            var hsCodeRecords = records
                .Where(r => r.Field.Contains("HS Code", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .Select(r => r.Value.Trim())
                .ToList();

            foreach (var code in hsCodeRecords)
            {
                var match = Regex.Match(code, @"(\d{4,6}[\.]?\d{0,4})");
                if (match.Success)
                {
                    hsCodes.Add(match.Value);
                }
                else if (!string.IsNullOrWhiteSpace(code))
                {
                    hsCodes.Add(code);
                }
            }

            summary.HSCodes = hsCodes.OrderBy(c => c).ToList();
        }

        private void ExtractQuantities(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var quantities = new List<(decimal value, string unit)>();

            var quantityRecords = records
                .Where(r => r.Field.Contains("Quantity", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .ToList();

            foreach (var record in quantityRecords)
            {
                var match = Regex.Match(record.Value, @"([\d,]+\.?\d*)\s*(\w+)?", RegexOptions.IgnoreCase);
                if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
                {
                    var unit = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
                    quantities.Add((qty, unit));
                }
            }

            if (quantities.Any())
            {
                var totalQty = quantities.Sum(q => q.value);
                var units = quantities.Select(q => q.unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
                var unit = units.Count == 1 ? units.First() : (units.Count > 1 ? "units" : "");

                summary.TotalQuantity = $"{totalQty:N0} {unit}".Trim();
                summary.LineItemCount = quantities.Count;
            }
        }

        private void ExtractWeight(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var weights = new List<decimal>();

            var weightRecords = records
                .Where(r => (r.Field.Contains("Weight", StringComparison.OrdinalIgnoreCase) ||
                            r.Field.Contains("Gross Weight", StringComparison.OrdinalIgnoreCase)) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .ToList();

            foreach (var record in weightRecords)
            {
                var match = Regex.Match(record.Value, @"([\d,]+\.?\d*)\s*(kg|KG|kilograms?)?", RegexOptions.IgnoreCase);
                if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var weight))
                {
                    weights.Add(weight);
                }
            }

            if (weights.Any())
            {
                var totalWeight = weights.Sum();
                summary.TotalWeight = $"{totalWeight:N2} kg";
            }
        }

        private void ExtractFOBValue(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var values = new List<(decimal value, string currency)>();

            var fobRecords = records
                .Where(r => r.Field.Contains("FOB", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .ToList();

            foreach (var record in fobRecords)
            {
                var match = Regex.Match(record.Value, @"[\$]?([\d,]+\.?\d*)\s*([A-Z]{3})?", RegexOptions.IgnoreCase);
                if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    var currency = match.Groups[2].Success ? match.Groups[2].Value : "USD";
                    values.Add((val, currency));
                }
            }

            if (values.Any())
            {
                var byCurrency = values.GroupBy(v => v.currency).ToList();
                if (byCurrency.Count == 1)
                {
                    var total = byCurrency.First().Sum(v => v.value);
                    var currency = byCurrency.First().Key;
                    summary.TotalFOBValue = $"{currency} {total:N2}";
                }
                else
                {
                    var summaries = byCurrency.Select(g => $"{g.Key} {g.Sum(v => v.value):N2}").ToList();
                    summary.TotalFOBValue = string.Join(", ", summaries);
                }
            }
        }

        private void ExtractDutyPaid(CargoSummaryDto summary, List<ICUMSDataRecordDto> records, List<BOEDetailDto> boeDetails)
        {
            var duties = new List<(decimal value, string currency)>();

            // From records - check for currency in the value string (like FOB does)
            var dutyRecords = records
                .Where(r => (r.Field.Contains("Duty", StringComparison.OrdinalIgnoreCase) ||
                            r.Field.Contains("Duty Paid", StringComparison.OrdinalIgnoreCase)) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .ToList();

            foreach (var record in dutyRecords)
            {
                // Parse duty value with optional currency (e.g., "$3,618.00 GHS" or "3618.00 GHS" or "3,618.00")
                var match = Regex.Match(record.Value, @"[\$]?([\d,]+\.?\d*)\s*([A-Z]{3})?", RegexOptions.IgnoreCase);
                if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var duty))
                {
                    // If currency is specified in the value, use it; otherwise default to GHS (Ghana Cedi)
                    var currency = match.Groups[2].Success ? match.Groups[2].Value : "GHS";
                    duties.Add((duty, currency));
                }
            }

            // From BOE details - default to GHS for duty paid (Ghana customs uses GHS for duty)
            foreach (var boe in boeDetails)
            {
                if (boe.TotalDutyPaid.HasValue && boe.TotalDutyPaid.Value > 0)
                {
                    duties.Add((boe.TotalDutyPaid.Value, "GHS"));
                }
            }

            if (duties.Any())
            {
                // Group by currency
                var byCurrency = duties.GroupBy(d => d.currency).ToList();
                if (byCurrency.Count == 1)
                {
                    var total = byCurrency.First().Sum(d => d.value);
                    var currency = byCurrency.First().Key;
                    summary.TotalDutyPaid = $"{currency} {total:N2}";
                }
                else
                {
                    var summaries = byCurrency.Select(g => $"{g.Key} {g.Sum(d => d.value):N2}").ToList();
                    summary.TotalDutyPaid = string.Join(", ", summaries);
                }
            }
        }

        private void ExtractCountriesOfOrigin(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var originRecords = records
                .Where(r => (r.Field.Contains("Country of Origin", StringComparison.OrdinalIgnoreCase) ||
                            r.Field.Contains("Origin", StringComparison.OrdinalIgnoreCase)) &&
                           !string.IsNullOrWhiteSpace(r.Value) &&
                           r.Value != "Not available" &&
                           r.Value != "N/A")
                .Select(r => r.Value.Trim())
                .ToList();

            foreach (var country in originRecords)
            {
                if (!string.IsNullOrWhiteSpace(country))
                    countries.Add(country);
            }

            summary.CountriesOfOrigin = countries.ToList();
        }

        private void ExtractLineItemCount(CargoSummaryDto summary, List<ICUMSDataRecordDto> records)
        {
            var itemFields = records
                .Where(r => r.Field.Contains("Item", StringComparison.OrdinalIgnoreCase) &&
                           (r.Field.Contains("HS Code", StringComparison.OrdinalIgnoreCase) ||
                            r.Field.Contains("Item Description", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (itemFields.Any())
            {
                var itemNumbers = new HashSet<int>();
                foreach (var field in itemFields)
                {
                    var match = Regex.Match(field.Field, @"Item\s*(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var itemNum))
                    {
                        itemNumbers.Add(itemNum);
                    }
                }

                if (itemNumbers.Any())
                {
                    summary.LineItemCount = itemNumbers.Count;
                }
                else
                {
                    var uniqueItems = itemFields
                        .Where(f => !string.IsNullOrWhiteSpace(f.Value) && f.Value != "Not available" && f.Value != "N/A")
                        .Select(f => f.Value)
                        .Distinct()
                        .Count();
                    summary.LineItemCount = uniqueItems > 0 ? uniqueItems : 1;
                }
            }
            else
            {
                summary.LineItemCount = 1;
            }
        }

        private void ExtractAdditionalDetails(CargoSummaryDto summary, List<ICUMSDataRecordDto> records, CargoGroupDto cargoGroup)
        {
            var clearanceType = records
                .FirstOrDefault(r => r.Field.Contains("Clearance Type", StringComparison.OrdinalIgnoreCase) &&
                                    !string.IsNullOrWhiteSpace(r.Value) &&
                                    r.Value != "Not available");

            if (clearanceType != null)
            {
                summary.AdditionalDetails["ClearanceType"] = clearanceType.Value;
            }
            else if (!string.IsNullOrWhiteSpace(cargoGroup.ClearanceType))
            {
                summary.AdditionalDetails["ClearanceType"] = cargoGroup.ClearanceType;
            }
        }

        private string GenerateSummaryText(CargoSummaryDto summary, CargoGroupDto cargoGroup)
        {
            var sb = new StringBuilder();

            // Header: Consignee(s) and Origin
            if (summary.Consignees.Any())
            {
                var consigneeText = summary.Consignees.Count == 1
                    ? summary.Consignees.First()
                    : $"{summary.Consignees.Count} consignee(s): {string.Join(", ", summary.Consignees.Take(2))}" +
                      (summary.Consignees.Count > 2 ? $" and {summary.Consignees.Count - 2} more" : "");

                sb.Append($"Consignee: {consigneeText}");

                if (summary.CountriesOfOrigin.Any())
                {
                    var originText = summary.CountriesOfOrigin.Count == 1
                        ? summary.CountriesOfOrigin.First()
                        : string.Join(", ", summary.CountriesOfOrigin.Take(2)) +
                          (summary.CountriesOfOrigin.Count > 2 ? $" and {summary.CountriesOfOrigin.Count - 2} more" : "");
                    sb.Append($" | Origin: {originText}");
                }

                sb.AppendLine();
            }

            // Goods Description
            if (!string.IsNullOrWhiteSpace(summary.GoodsDescription))
            {
                sb.AppendLine($"Description: {summary.GoodsDescription}");
            }

            // Key Metrics
            var metrics = new List<string>();

            if (summary.LineItemCount > 0)
            {
                metrics.Add($"{summary.LineItemCount} line item(s)");
            }

            if (summary.HSCodes.Any())
            {
                var hsText = summary.HSCodes.Count <= 3
                    ? string.Join(", ", summary.HSCodes)
                    : $"{string.Join(", ", summary.HSCodes.Take(3))} and {summary.HSCodes.Count - 3} more";
                metrics.Add($"HS Codes: {hsText}");
            }

            if (!string.IsNullOrWhiteSpace(summary.TotalQuantity))
            {
                metrics.Add($"Quantity: {summary.TotalQuantity}");
            }

            if (!string.IsNullOrWhiteSpace(summary.TotalWeight))
            {
                metrics.Add($"Weight: {summary.TotalWeight}");
            }

            if (!string.IsNullOrWhiteSpace(summary.TotalFOBValue))
            {
                metrics.Add($"FOB Value: {summary.TotalFOBValue}");
            }

            if (!string.IsNullOrWhiteSpace(summary.TotalDutyPaid))
            {
                metrics.Add($"Duty Paid: {summary.TotalDutyPaid}");
            }

            if (metrics.Any())
            {
                sb.AppendLine(string.Join(" | ", metrics));
            }

            return sb.ToString().Trim();
        }
    }
}

