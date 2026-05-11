using System.Text;
using NickScanCentralImagingPortal.Core.Security;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Security;

public class SignedImageUrlCanonicalTests
{
    [Fact]
    public void ComputeSignature_PercentEncodedRouteValues_MatchDecodedRequestPath()
    {
        var key = Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF");
        const string exp = "1778452027";
        const string uid = "analyst";

        const string decodedPath = "/api/ImageProcessing/container/CAXU6863152, MSBU3047832/complete/image";
        const string encodedPath = "/api/ImageProcessing/container/CAXU6863152%2C%20MSBU3047832/complete/image";

        var decodedSignature = SignedImageUrlCanonical.ComputeSignature(key, decodedPath, exp, uid);
        var encodedSignature = SignedImageUrlCanonical.ComputeSignature(key, encodedPath, exp, uid);

        Assert.Equal(decodedSignature, encodedSignature);
    }

    [Fact]
    public void ComputeSignature_IsCaseInsensitiveForPath()
    {
        var key = Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF");
        const string exp = "1778452027";
        const string uid = "analyst";

        var upper = SignedImageUrlCanonical.ComputeSignature(
            key,
            "/api/ImageProcessing/container/MSNU2643860/complete/image",
            exp,
            uid);
        var lower = SignedImageUrlCanonical.ComputeSignature(
            key,
            "/api/imageprocessing/container/msnu2643860/complete/image",
            exp,
            uid);

        Assert.Equal(upper, lower);
    }
}
