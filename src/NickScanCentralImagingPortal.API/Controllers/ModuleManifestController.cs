using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NickScanCentralImagingPortal.API.Controllers;

/// <summary>
/// PLATFORM.md §6 platform-contract endpoint. Exposes module metadata so the
/// unified portal launcher (Phase 2) and the synthetic monitor (Phase 11) can
/// auto-discover NSCIM as a first-class platform module without hard-coding
/// service identity. Anonymous on purpose — the manifest contains no secrets,
/// only routing + capability advertisement.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/_module")]
public class ModuleManifestController : ControllerBase
{
    [HttpGet("manifest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetManifest() => Ok(new
    {
        name = "NSCIM",
        displayName = "NickScan Central Imaging System",
        version = ThisAssemblyInformationalVersion(),
        description = "Customs cargo scanning, image analysis, ICUMS integration",
        platformContractVersion = "1.0",
        icon = "shield-check",
        color = "#1d4ed8",
        routes = new
        {
            home = "/dashboard",
            settings = "/admin/settings"
        },
        platformDependencies = new[] { "comms" },
        capabilities = new[]
        {
            "container.scan",
            "container.review",
            "icums.submit",
            "image.analysis"
        },
        health = "/health"
    });

    private static string ThisAssemblyInformationalVersion()
    {
        var asm = typeof(ModuleManifestController).Assembly;
        var attr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        return attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
