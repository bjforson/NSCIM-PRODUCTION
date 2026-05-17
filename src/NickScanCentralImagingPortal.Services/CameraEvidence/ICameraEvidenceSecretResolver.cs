namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public interface ICameraEvidenceSecretResolver
    {
        string? Resolve(string? secretName, string? inlineValue = null);
    }
}
