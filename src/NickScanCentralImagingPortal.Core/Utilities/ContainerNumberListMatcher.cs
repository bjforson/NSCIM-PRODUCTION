using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NickScanCentralImagingPortal.Core.Utilities;

public static class ContainerNumberListMatcher
{
    private static readonly Regex ContainerTokenRegex = new(
        "[A-Z]{4}\\d{7}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool ContainsContainer(string? containerList, string? containerNumber)
    {
        var target = Normalize(containerNumber);
        if (string.IsNullOrEmpty(target) || string.IsNullOrWhiteSpace(containerList))
            return false;

        var source = containerList.Trim();
        if (string.Equals(Normalize(source), target, StringComparison.Ordinal))
            return true;

        var matches = ContainerTokenRegex.Matches(source);
        if (matches.Count > 0)
        {
            return matches
                .Select(match => Normalize(match.Value))
                .Any(token => string.Equals(token, target, StringComparison.Ordinal));
        }

        return source
            .Split(new[] { ',', ';', '|', '/', '\\', '\t', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Any(token => string.Equals(token, target, StringComparison.Ordinal));
    }

    public static bool IsCompositeContainerIdentifier(string? containerNumber)
    {
        if (string.IsNullOrWhiteSpace(containerNumber))
            return false;

        var source = containerNumber.Trim();
        if (source.Contains(',', StringComparison.Ordinal) || source.Contains(';', StringComparison.Ordinal))
            return true;

        return ContainerTokenRegex.Matches(source)
            .Select(match => Normalize(match.Value))
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
    }

    public static string Normalize(string? containerNumber) =>
        string.IsNullOrWhiteSpace(containerNumber)
            ? string.Empty
            : containerNumber.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
