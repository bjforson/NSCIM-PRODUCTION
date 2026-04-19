using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Collects, prioritizes, and stores unmapped fields from JSON documents
    /// Ensures 100% data capture - no fields left behind
    /// </summary>
    public class UnmappedFieldCollector
    {
        private readonly ILogger<UnmappedFieldCollector>? _logger;
        private readonly List<UnmappedField> _unmappedFields = new();

        public UnmappedFieldCollector(ILogger<UnmappedFieldCollector>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Adds an unmapped field from a JSON section
        /// </summary>
        public void AddUnmappedField(string section, string fieldName, JsonElement valueElement)
        {
            var value = ExtractValue(valueElement);
            _unmappedFields.Add(new UnmappedField
            {
                Section = section,
                FieldName = fieldName,
                Value = value,
                HasValue = value != null && value != "null"
            });
        }

        /// <summary>
        /// Scans a JSON element for unmapped fields
        /// </summary>
        public void ScanSection(JsonElement element, string section, HashSet<string> expectedFields)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in element.EnumerateObject())
            {
                var fieldName = prop.Name;

                // Check if field is in expected set (case-insensitive)
                var isMapped = expectedFields.Contains(fieldName) ||
                              expectedFields.Contains(fieldName.ToUpper()) ||
                              expectedFields.Contains(fieldName.ToLower());

                if (!isMapped)
                {
                    AddUnmappedField(section, fieldName, prop.Value);
                    _logger?.LogDebug("🔍 Unmapped field detected: {Section}.{FieldName}", section, fieldName);
                }
            }
        }

        /// <summary>
        /// Gets all unmapped fields, prioritized according to hybrid strategy
        /// Priority: Section (Header > ContainerDetails > ManifestDetails) > Non-null > Alphabetical
        /// </summary>
        public List<UnmappedField> GetPrioritizedFields()
        {
            // Section priority mapping
            var sectionPriority = new Dictionary<string, int>
            {
                { "Header", 1 },
                { "ContainerDetails", 2 },
                { "ManifestDetails", 3 },
                { "ManifestItem", 4 }
            };

            return _unmappedFields
                .OrderBy(f => sectionPriority.GetValueOrDefault(f.Section, 999))  // Section priority
                .ThenByDescending(f => f.HasValue)  // Non-null values first
                .ThenBy(f => f.FieldName)  // Alphabetical tiebreaker
                .ToList();
        }

        /// <summary>
        /// Gets count of unmapped fields
        /// </summary>
        public int Count => _unmappedFields.Count;

        /// <summary>
        /// Clears all collected fields
        /// </summary>
        public void Clear()
        {
            _unmappedFields.Clear();
        }

        /// <summary>
        /// Extracts string value from JsonElement
        /// </summary>
        private string? ExtractValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                JsonValueKind.Object => element.GetRawText(),  // Store as JSON string
                JsonValueKind.Array => element.GetRawText(),   // Store as JSON string
                _ => element.GetRawText()
            };
        }
    }

    /// <summary>
    /// Represents an unmapped field found in JSON
    /// </summary>
    public class UnmappedField
    {
        public string Section { get; set; } = string.Empty;  // e.g., "Header", "ContainerDetails"
        public string FieldName { get; set; } = string.Empty;  // e.g., "NewField"
        public string? Value { get; set; }  // Full value
        public bool HasValue { get; set; }  // true if value is not null/empty
    }
}

