using System.Text;

namespace NickScanWebApp.Shared.Services;

public static class ImageProxyUrlBuilder
{
    public static string Build(string targetUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(targetUrl);
        return $"/api/imageproxy?url={Uri.EscapeDataString(Convert.ToBase64String(bytes))}";
    }
}
