namespace NickComms.Gateway.Endpoints;

/// <summary>
/// PLATFORM.md §6 platform-contract endpoint — exposes module metadata so the
/// unified portal launcher (Phase 2) and the synthetic monitor (Phase 11) can
/// auto-discover NickComms as a first-class platform module without hard-coding
/// service identity.
///
/// Anonymous on purpose — the manifest contains no secrets, only routing +
/// capability advertisement. No <c>RequireAuthorization()</c> means the default
/// scheme (ApiKey) is bypassed for this single route, matching the controller-
/// based equivalent on NSCIM_API and NickHR.API which use <c>[AllowAnonymous]</c>.
/// </summary>
public static class ModuleEndpoints
{
    public static RouteGroupBuilder MapModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/_module")
            .WithTags("Platform");

        group.MapGet("/manifest", () => Results.Ok(new
        {
            name = "NickComms",
            displayName = "NickComms — Communications Gateway",
            version = ThisAssemblyInformationalVersion(),
            description = "Centralized SMS (Hubtel), email (SMTP), and OTP delivery for the NickERP platform.",
            platformContractVersion = "1.0",
            icon = "send",
            color = "#0891b2",
            routes = new
            {
                // No first-class UI yet; surface swagger as the operator landing.
                home = "/swagger",
                settings = "/swagger"
            },
            // NickComms is a sink module — nothing depends on us upstream. Other
            // modules list "comms" in their platformDependencies array.
            platformDependencies = Array.Empty<string>(),
            capabilities = new[]
            {
                "sms.send",
                "sms.bulk",
                "email.send",
                "email.bulk",
                "otp.send",
                "otp.verify"
            },
            health = "/api/health"
        }))
        .WithName("GetModuleManifest")
        .WithDescription("PLATFORM.md §6 module manifest — capability + routing advertisement.")
        .AllowAnonymous();

        return group;
    }

    private static string ThisAssemblyInformationalVersion()
    {
        var asm = typeof(ModuleEndpoints).Assembly;
        var attr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        return attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
