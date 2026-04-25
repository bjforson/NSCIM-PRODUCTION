using System;
using System.Runtime.CompilerServices;

namespace NickScanCentralImagingPortal.Tests;

/// <summary>
/// Runs once when the test assembly is loaded — before xunit starts asking
/// for class fixtures. We set the two env vars that <c>API/Program.cs</c> reads
/// during the very first lines of the entry point, so a <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// host can boot inside this process without exiting on the production
/// single-instance Mutex check or the missing-JWT-secret guard.
///
/// Production never loads this assembly; the env vars stay unset on the
/// running Windows service.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("NSCIM_SKIP_SINGLE_INSTANCE", "1");

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NICKSCAN_JWT_SECRET_KEY")))
        {
            // 64-char synthetic key — only valid for the test process.
            Environment.SetEnvironmentVariable(
                "NICKSCAN_JWT_SECRET_KEY",
                "test-only-jwt-key-do-not-use-in-prod-9876543210abcdef-pad-pad-pad");
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NICKSCAN_IMAGE_SIGNING_KEY")))
        {
            // 64-char hex synthetic key for the HMAC-signed image URL pipeline.
            Environment.SetEnvironmentVariable(
                "NICKSCAN_IMAGE_SIGNING_KEY",
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        }
    }
}
