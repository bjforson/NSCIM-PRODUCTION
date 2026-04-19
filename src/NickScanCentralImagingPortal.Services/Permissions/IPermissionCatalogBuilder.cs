using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models.Permissions;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public interface IPermissionCatalogBuilder
    {
        Task<PermissionCatalogDto> BuildAsync(CancellationToken cancellationToken = default);
    }
}

