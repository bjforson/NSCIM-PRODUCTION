using System.Text.RegularExpressions;

namespace NickScanCentralImagingPortal.Core.Helpers;

/// <summary>
/// Helpers for GroupIdentifier normalization (wave/date-suffix handling).
/// Formats:
/// "41025661190_W1" -> "41025661190"
/// "41025661190_20250101_20250131" -> "41025661190"
/// </summary>
public static class GroupIdentifierHelper
{
    /// <summary>
    /// Extracts the stable/original GroupIdentifier from wave and date-suffixed formats.
    /// e.g. "70825542327_W1" -> "70825542327";
    /// "70825542327_20250101_20250131" -> "70825542327".
    /// </summary>
    public static string? GetNormalizedGroupIdentifier(string? groupIdentifier)
    {
        if (string.IsNullOrEmpty(groupIdentifier)) return null;

        var normalized = groupIdentifier.Trim();

        normalized = StripDateRangeSuffix(normalized);

        var waveMatch = Regex.Match(
            normalized,
            @"^(?<base>.+)_W\d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (waveMatch.Success)
            return StripDateRangeSuffix(waveMatch.Groups["base"].Value);

        return normalized;
    }

    private static string StripDateRangeSuffix(string value)
    {
        var lastUnderscore = value.LastIndexOf('_');
        if (lastUnderscore < 0) return value;

        var secondLast = value.LastIndexOf('_', lastUnderscore - 1);
        if (secondLast < 0) return value;

        var suffix = value.Substring(secondLast + 1);
        return suffix.Length == 17 && Regex.IsMatch(suffix, @"^\d{8}_\d{8}$")
            ? value.Substring(0, secondLast) // M2: Use secondLast so "ABC_123_20250101_20250131" -> "ABC_123"
            : value;
    }
}
