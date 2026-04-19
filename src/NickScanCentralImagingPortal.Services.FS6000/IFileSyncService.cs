using System;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public interface IFileSyncService
    {
        Task StartSyncAsync();
        Task StopSyncAsync();
        Task<bool> IsHealthyAsync();
        Task<int> GetPendingSyncCountAsync();
        Task<int> GetFailedSyncCountAsync();
    }
}
