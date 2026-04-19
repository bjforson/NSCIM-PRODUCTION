using System.Text.RegularExpressions;

namespace NickScanCentralImagingPortal.Core.Helpers;

/// <summary>
/// Helpers for GroupIdentifier normalization (date-suffix handling).
/// Format: "41025661190_20250101_20250131" → "41025661190"
/// </summary>
public static class GroupIdentifierHelper
{
    /// <summary>
    /// Extracts normalized/original GroupIdentifier from date-suffixed format.
    /// e.g. "70825542327_20250101_20250131" → "70825542327"
    /// </summary>
    public static string? GetNormalizedGroupIdentifier(string? groupIdentifier)
    {
        if (string.IsNullOrEmpty(groupIdentifier)) return null;
        var lastUnderscore = groupIdentifier.LastIndexOf('_');
        if (lastUnderscore < 0) return groupIdentifier;
        var secondLast = groupIdentifier.LastIndexOf('_', lastUnderscore - 1);
        if (secondLast < 0) return groupIdentifier;
        var suffix = groupIdentifier.Substring(secondLast + 1);
        if (suffix.Length == 17 && Regex.IsMatch(suffix, @"^\d{8}_\d{8}$"))
            return groupIdentifier.Substring(0, secondLast); // M2: Use secondLast so "ABC_123_20250101_20250131" -> "ABC_123"
        return groupIdentifier;
    }
}
