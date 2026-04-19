using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Helper class to store unmapped fields in BOEDocument and DownloadedManifestItem entities
    /// Uses reflection to set unmapped field properties dynamically
    /// </summary>
    public static class UnmappedFieldStorageHelper
    {
        /// <summary>
        /// Stores unmapped fields in BOEDocument using two-tier strategy
        /// Tier 1: First 20 in structured columns
        /// Tier 2: ALL in RawJsonData as JSON
        /// </summary>
        public static void StoreUnmappedFields(BOEDocument boeDocument, List<UnmappedField> unmappedFields, JsonElement completeJsonElement)
        {
            if (unmappedFields == null || unmappedFields.Count == 0)
            {
                boeDocument.UnmappedFieldsCount = 0;
                boeDocument.UnmappedFieldsOverflow = false;

                // ✅ PHASE 1 FIX: Only set RawJsonData if it's not already set (preserve existing raw JSON)
                if (string.IsNullOrEmpty(boeDocument.RawJsonData))
                {
                    // Still store complete JSON for backup
                    boeDocument.RawJsonData = JsonSerializer.Serialize(new
                    {
                        CompleteDocument = completeJsonElement.GetRawText(),
                        UnmappedFields = new List<object>()
                    });
                }
                return;
            }

            var prioritizedFields = unmappedFields
                .OrderBy(f => GetSectionPriority(f.Section))
                .ThenByDescending(f => f.HasValue)
                .ThenBy(f => f.FieldName)
                .ToList();

            // Tier 1: Store first 20 in structured columns
            var fieldsToStoreInColumns = prioritizedFields.Take(20).ToList();
            for (int i = 0; i < fieldsToStoreInColumns.Count; i++)
            {
                var field = fieldsToStoreInColumns[i];
                var label = $"{field.Section}:{field.FieldName}";  // Format: "Header:NewField"
                var value = TruncateValue(field.Value, 4000);  // Truncate if > 4000 chars

                SetUnmappedField(boeDocument, i + 1, label, value);
            }

            // Tier 2: Store ALL in RawJsonData (complete backup - nothing lost)
            // ✅ PHASE 1 FIX: Preserve existing RawJsonData if it was already set (contains raw JSON)
            // Otherwise, create new structure with complete document
            var rawJsonText = completeJsonElement.GetRawText();
            if (string.IsNullOrEmpty(boeDocument.RawJsonData))
            {
                // No existing RawJsonData, create new structure
                boeDocument.RawJsonData = JsonSerializer.Serialize(new
                {
                    CompleteDocument = rawJsonText,  // Full JSON document
                    UnmappedFields = prioritizedFields.Select(f => new
                    {
                        Section = f.Section,
                        Field = f.FieldName,
                        Value = f.Value  // Full value (not truncated)
                    }).ToList()
                }, new JsonSerializerOptions { WriteIndented = false });
            }
            else
            {
                // RawJsonData already contains raw JSON, add unmapped fields metadata
                try
                {
                    var existingData = JsonSerializer.Deserialize<Dictionary<string, object>>(boeDocument.RawJsonData);
                    if (existingData == null)
                    {
                        // If deserialization fails, preserve raw JSON and add metadata
                        boeDocument.RawJsonData = JsonSerializer.Serialize(new
                        {
                            CompleteDocument = boeDocument.RawJsonData,  // Preserve existing
                            UnmappedFields = prioritizedFields.Select(f => new
                            {
                                Section = f.Section,
                                Field = f.FieldName,
                                Value = f.Value
                            }).ToList()
                        }, new JsonSerializerOptions { WriteIndented = false });
                    }
                    else
                    {
                        // Add unmapped fields to existing structure
                        existingData["UnmappedFields"] = prioritizedFields.Select(f => new
                        {
                            Section = f.Section,
                            Field = f.FieldName,
                            Value = f.Value
                        }).ToList();
                        boeDocument.RawJsonData = JsonSerializer.Serialize(existingData, new JsonSerializerOptions { WriteIndented = false });
                    }
                }
                catch
                {
                    // If we can't parse existing RawJsonData, wrap it
                    boeDocument.RawJsonData = JsonSerializer.Serialize(new
                    {
                        CompleteDocument = boeDocument.RawJsonData,  // Preserve existing raw JSON
                        UnmappedFields = prioritizedFields.Select(f => new
                        {
                            Section = f.Section,
                            Field = f.FieldName,
                            Value = f.Value
                        }).ToList()
                    }, new JsonSerializerOptions { WriteIndented = false });
                }
            }

            // Metadata
            boeDocument.UnmappedFieldsCount = prioritizedFields.Count;
            boeDocument.UnmappedFieldsOverflow = prioritizedFields.Count > 20;
        }

        /// <summary>
        /// Stores unmapped fields in DownloadedManifestItem using two-tier strategy
        /// </summary>
        public static void StoreUnmappedFields(DownloadedManifestItem manifestItem, List<UnmappedField> unmappedFields, JsonElement completeJsonElement)
        {
            if (unmappedFields == null || unmappedFields.Count == 0)
            {
                manifestItem.UnmappedFieldsCount = 0;
                manifestItem.UnmappedFieldsOverflow = false;

                // Still store complete JSON for backup
                manifestItem.RawJsonData = JsonSerializer.Serialize(new
                {
                    CompleteDocument = completeJsonElement.GetRawText(),
                    UnmappedFields = new List<object>()
                });
                return;
            }

            var prioritizedFields = unmappedFields
                .OrderByDescending(f => f.HasValue)
                .ThenBy(f => f.FieldName)
                .ToList();

            // Tier 1: Store first 20 in structured columns
            var fieldsToStoreInColumns = prioritizedFields.Take(20).ToList();
            for (int i = 0; i < fieldsToStoreInColumns.Count; i++)
            {
                var field = fieldsToStoreInColumns[i];
                var label = $"ManifestItem:{field.FieldName}";  // Format: "ManifestItem:NewField"
                var value = TruncateValue(field.Value, 4000);

                SetUnmappedField(manifestItem, i + 1, label, value);
            }

            // Tier 2: Store ALL in RawJsonData
            manifestItem.RawJsonData = JsonSerializer.Serialize(new
            {
                CompleteDocument = completeJsonElement.GetRawText(),
                UnmappedFields = prioritizedFields.Select(f => new
                {
                    Section = "ManifestItem",
                    Field = f.FieldName,
                    Value = f.Value
                }).ToList()
            }, new JsonSerializerOptions { WriteIndented = false });

            // Metadata
            manifestItem.UnmappedFieldsCount = prioritizedFields.Count;
            manifestItem.UnmappedFieldsOverflow = prioritizedFields.Count > 20;
        }

        /// <summary>
        /// Sets an unmapped field in BOEDocument using reflection
        /// </summary>
        private static void SetUnmappedField(BOEDocument boeDocument, int index, string label, string? value)
        {
            var labelProperty = typeof(BOEDocument).GetProperty($"UnmappedField{index}Label");
            var valueProperty = typeof(BOEDocument).GetProperty($"UnmappedField{index}Value");

            labelProperty?.SetValue(boeDocument, label);
            valueProperty?.SetValue(boeDocument, value);
        }

        /// <summary>
        /// Sets an unmapped field in DownloadedManifestItem using reflection
        /// </summary>
        private static void SetUnmappedField(DownloadedManifestItem manifestItem, int index, string label, string? value)
        {
            var labelProperty = typeof(DownloadedManifestItem).GetProperty($"UnmappedField{index}Label");
            var valueProperty = typeof(DownloadedManifestItem).GetProperty($"UnmappedField{index}Value");

            labelProperty?.SetValue(manifestItem, label);
            valueProperty?.SetValue(manifestItem, value);
        }

        /// <summary>
        /// Gets section priority for sorting (lower number = higher priority)
        /// </summary>
        private static int GetSectionPriority(string section)
        {
            return section switch
            {
                "Header" => 1,
                "ContainerDetails" => 2,
                "ManifestDetails" => 3,
                "ManifestItem" => 4,
                _ => 999
            };
        }

        /// <summary>
        /// Truncates a value to max length, preserving the most important part
        /// </summary>
        private static string? TruncateValue(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length <= maxLength) return value;

            // Truncate and add indicator
            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}

