using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Tracks field extraction completeness to ensure we're capturing all available data from JSON
    /// </summary>
    public class FieldExtractionTracker
    {
        private readonly ILogger<FieldExtractionTracker>? _logger;
        private readonly Dictionary<string, FieldExtractionStats> _fieldStats = new();
        private readonly HashSet<string> _unmappedFields = new();
        private readonly object _lock = new();

        public FieldExtractionTracker(ILogger<FieldExtractionTracker>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Records that a field was successfully extracted
        /// </summary>
        public void RecordExtraction(string section, string fieldName, bool extracted, string? jsonFieldName = null)
        {
            lock (_lock)
            {
                var key = $"{section}.{fieldName}";
                if (!_fieldStats.ContainsKey(key))
                {
                    _fieldStats[key] = new FieldExtractionStats
                    {
                        Section = section,
                        FieldName = fieldName,
                        JsonFieldName = jsonFieldName ?? fieldName
                    };
                }

                var stats = _fieldStats[key];
                stats.TotalAttempts++;
                if (extracted)
                {
                    stats.SuccessfulExtractions++;
                }
                else
                {
                    stats.FailedExtractions++;
                }
            }
        }

        /// <summary>
        /// Records an unmapped field found in JSON that we're not extracting
        /// </summary>
        public void RecordUnmappedField(string section, string jsonFieldName)
        {
            lock (_lock)
            {
                var key = $"{section}.{jsonFieldName}";
                if (!_unmappedFields.Contains(key))
                {
                    _unmappedFields.Add(key);
                    _logger?.LogWarning("🔍 UNMAPPED FIELD DETECTED: {Section}.{FieldName} - This field exists in JSON but is not being extracted", section, jsonFieldName);
                }
            }
        }

        /// <summary>
        /// Scans a JSON element for all properties and compares against expected fields
        /// </summary>
        public void ScanForUnmappedFields(JsonElement element, string section, HashSet<string> expectedFields)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in element.EnumerateObject())
            {
                var fieldName = prop.Name;
                if (!expectedFields.Contains(fieldName) &&
                    !expectedFields.Contains(fieldName.ToUpper()) &&
                    !expectedFields.Contains(fieldName.ToLower()))
                {
                    RecordUnmappedField(section, fieldName);
                }
            }
        }

        /// <summary>
        /// Gets extraction statistics for all fields
        /// </summary>
        public Dictionary<string, FieldExtractionStats> GetStatistics()
        {
            lock (_lock)
            {
                return new Dictionary<string, FieldExtractionStats>(_fieldStats);
            }
        }

        /// <summary>
        /// Gets all unmapped fields detected
        /// </summary>
        public HashSet<string> GetUnmappedFields()
        {
            lock (_lock)
            {
                return new HashSet<string>(_unmappedFields);
            }
        }

        /// <summary>
        /// Generates a completeness report
        /// </summary>
        public FieldExtractionReport GenerateReport()
        {
            lock (_lock)
            {
                var report = new FieldExtractionReport
                {
                    TotalFieldsTracked = _fieldStats.Count,
                    UnmappedFieldsCount = _unmappedFields.Count,
                    FieldStatistics = new Dictionary<string, FieldExtractionStats>(_fieldStats),
                    UnmappedFields = new HashSet<string>(_unmappedFields)
                };

                // Calculate completeness percentages
                foreach (var kvp in _fieldStats)
                {
                    var stats = kvp.Value;
                    if (stats.TotalAttempts > 0)
                    {
                        stats.SuccessRate = (double)stats.SuccessfulExtractions / stats.TotalAttempts * 100;
                    }
                }

                return report;
            }
        }

        /// <summary>
        /// Resets all statistics
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _fieldStats.Clear();
                _unmappedFields.Clear();
            }
        }
    }

    public class FieldExtractionStats
    {
        public string Section { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string JsonFieldName { get; set; } = string.Empty;
        public int TotalAttempts { get; set; }
        public int SuccessfulExtractions { get; set; }
        public int FailedExtractions { get; set; }
        public double SuccessRate { get; set; }
    }

    public class FieldExtractionReport
    {
        public int TotalFieldsTracked { get; set; }
        public int UnmappedFieldsCount { get; set; }
        public Dictionary<string, FieldExtractionStats> FieldStatistics { get; set; } = new();
        public HashSet<string> UnmappedFields { get; set; } = new();
    }
}

