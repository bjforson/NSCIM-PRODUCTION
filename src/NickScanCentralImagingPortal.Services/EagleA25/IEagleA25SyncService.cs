namespace NickScanCentralImagingPortal.Services.EagleA25
{
    public interface IEagleA25SyncService
    {
        Task<EagleA25SyncResult> SyncAsync(CancellationToken cancellationToken = default);
    }

    public sealed record EagleA25SyncResult(
        int ScansRead,
        int ScansInserted,
        int ScansUpdated,
        int AssetsRead,
        int AssetsInserted,
        int AssetsUpdated,
        long? LastSyncedAccession);
}
