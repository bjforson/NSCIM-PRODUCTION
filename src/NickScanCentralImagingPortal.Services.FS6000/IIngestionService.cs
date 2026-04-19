using System;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public interface IIngestionService
    {
        Task StartIngestionAsync();
        Task StopIngestionAsync();
        Task<bool> IsHealthyAsync();
        Task<int> GetPendingIngestionCountAsync();
        Task<int> GetFailedIngestionCountAsync();
        Task ProcessFolderAsync(string folderPath);
    }
}
