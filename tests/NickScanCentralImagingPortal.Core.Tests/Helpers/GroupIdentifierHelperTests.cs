using NickScanCentralImagingPortal.Core.Helpers;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Helpers;

public class GroupIdentifierHelperTests
{
    [Theory]
    [InlineData("40426305424_W1", "40426305424")]
    [InlineData("40426305424_w12", "40426305424")]
    [InlineData("CMR-C40FEA9B3C7FA383450D_W2", "CMR-C40FEA9B3C7FA383450D")]
    public void GetNormalizedGroupIdentifier_StripsWaveSuffix(string input, string expected)
    {
        Assert.Equal(expected, GroupIdentifierHelper.GetNormalizedGroupIdentifier(input));
    }

    [Theory]
    [InlineData("70825542327_20250101_20250131", "70825542327")]
    [InlineData("ABC_123_20250101_20250131", "ABC_123")]
    public void GetNormalizedGroupIdentifier_StripsDateRangeSuffix(string input, string expected)
    {
        Assert.Equal(expected, GroupIdentifierHelper.GetNormalizedGroupIdentifier(input));
    }

    [Fact]
    public void GetNormalizedGroupIdentifier_StripsDateRangeThenWaveSuffix()
    {
        Assert.Equal("40426305424", GroupIdentifierHelper.GetNormalizedGroupIdentifier("40426305424_W1_20250101_20250131"));
    }
}
