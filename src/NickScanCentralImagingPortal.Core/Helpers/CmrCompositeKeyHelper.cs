using System.Security.Cryptography;
using System.Text;

namespace NickScanCentralImagingPortal.Core.Helpers;

public static class CmrCompositeKeyHelper
{
    public const string KeyPrefix = "CMR-";

    public static bool TryCreate(
        string? rotationNumber,
        string? containerNumber,
        string? blNumber,
        out CmrCompositeKey compositeKey)
    {
        var rotation = NormalizePart(rotationNumber);
        var container = NormalizePart(containerNumber);
        var bl = NormalizePart(blNumber);

        if (string.IsNullOrWhiteSpace(rotation)
            || string.IsNullOrWhiteSpace(container)
            || string.IsNullOrWhiteSpace(bl))
        {
            compositeKey = CmrCompositeKey.Empty;
            return false;
        }

        var hashInput = $"{rotation}|{container}|{bl}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..20];
        compositeKey = new CmrCompositeKey(
            OperationalKey: $"{KeyPrefix}{hash}",
            DisplayLabel: $"CMR {container} / {rotation} / {bl}",
            RotationNumber: rotation,
            ContainerNumber: container,
            BlNumber: bl);
        return true;
    }

    public static bool HasRequiredParts(
        string? rotationNumber,
        string? containerNumber,
        string? blNumber)
    {
        return TryCreate(rotationNumber, containerNumber, blNumber, out _);
    }

    public static bool IsOperationalKey(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key)
            || key.Length != KeyPrefix.Length + 20
            || !key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return key[KeyPrefix.Length..].All(c =>
            c is >= '0' and <= '9'
            || c is >= 'A' and <= 'F'
            || c is >= 'a' and <= 'f');
    }

    private static string NormalizePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed record CmrCompositeKey(
    string OperationalKey,
    string DisplayLabel,
    string RotationNumber,
    string ContainerNumber,
    string BlNumber)
{
    public static CmrCompositeKey Empty { get; } = new("", "", "", "", "");
}
