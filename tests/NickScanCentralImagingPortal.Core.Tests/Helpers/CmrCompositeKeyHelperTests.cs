using NickScanCentralImagingPortal.Core.Helpers;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Helpers;

public class CmrCompositeKeyHelperTests
{
    [Fact]
    public void TryCreate_BuildsRouteSafeStableKeyAndDisplayLabel()
    {
        var created = CmrCompositeKeyHelper.TryCreate(
            rotationNumber: "  rot-123  ",
            containerNumber: " pidu4444900 ",
            blNumber: " bl 789 ",
            out var key);

        Assert.True(created);
        Assert.StartsWith("CMR-", key.OperationalKey);
        Assert.Equal(24, key.OperationalKey.Length);
        Assert.Matches("^CMR-[0-9A-F]{20}$", key.OperationalKey);
        Assert.Equal("CMR PIDU4444900 / ROT-123 / BL 789", key.DisplayLabel);
    }

    [Fact]
    public void TryCreate_NormalizesWhitespaceAndCasingForStableOutput()
    {
        CmrCompositeKeyHelper.TryCreate("rot-123", "pidu4444900", "bl   789", out var first);
        CmrCompositeKeyHelper.TryCreate(" ROT-123 ", " PIDU4444900 ", " BL 789 ", out var second);

        Assert.Equal(first.OperationalKey, second.OperationalKey);
    }

    [Fact]
    public void IsOperationalKey_RecognizesOnlyCmrHashKeys()
    {
        CmrCompositeKeyHelper.TryCreate("ROT123", "PIDU4444900", "BL789", out var key);

        Assert.True(CmrCompositeKeyHelper.IsOperationalKey(key.OperationalKey));
        Assert.True(CmrCompositeKeyHelper.IsOperationalKey(key.OperationalKey.ToLowerInvariant()));
        Assert.False(CmrCompositeKeyHelper.IsOperationalKey("PIDU4444900"));
        Assert.False(CmrCompositeKeyHelper.IsOperationalKey("CMR-XYZ"));
        Assert.False(CmrCompositeKeyHelper.IsOperationalKey("CMR-1234567890ABCDEFGHIJ"));
    }

    [Theory]
    [InlineData(null, "PIDU4444900", "BL789")]
    [InlineData("ROT123", null, "BL789")]
    [InlineData("ROT123", "PIDU4444900", null)]
    [InlineData("", "PIDU4444900", "BL789")]
    [InlineData("ROT123", "", "BL789")]
    [InlineData("ROT123", "PIDU4444900", "")]
    public void TryCreate_FailsWhenAnyRequiredPartIsMissing(
        string? rotationNumber,
        string? containerNumber,
        string? blNumber)
    {
        var created = CmrCompositeKeyHelper.TryCreate(
            rotationNumber,
            containerNumber,
            blNumber,
            out var key);

        Assert.False(created);
        Assert.Equal(CmrCompositeKey.Empty, key);
    }
}
