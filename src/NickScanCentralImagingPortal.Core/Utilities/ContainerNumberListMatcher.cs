using System;
using System.Collections.Generic;
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

    public static bool ContainsAllContainers(string? containerList, string? requestedContainerList)
    {
        if (string.IsNullOrWhiteSpace(containerList) || string.IsNullOrWhiteSpace(requestedContainerList))
            return false;

        var requestedTokens = ExtractContainerTokens(requestedContainerList)
            .Select(Normalize)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requestedTokens.Count == 0)
        {
            var requested = Normalize(requestedContainerList);
            return !string.IsNullOrEmpty(requested)
                && string.Equals(Normalize(containerList), requested, StringComparison.Ordinal);
        }

        return requestedTokens.All(token => ContainsContainer(containerList, token));
    }

    public static IReadOnlyList<string> ExtractContainerTokens(string? containerList)
    {
        if (string.IsNullOrWhiteSpace(containerList))
            return Array.Empty<string>();

        var source = containerList.Trim();
        var matches = ContainerTokenRegex.Matches(source);
        if (matches.Count > 0)
        {
            return matches
                .Select(match => Normalize(match.Value))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return source
            .Split(new[] { ',', ';', '|', '/', '\\', '\t', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string Normalize(string? containerNumber) =>
        string.IsNullOrWhiteSpace(containerNumber)
            ? string.Empty
            : containerNumber.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
