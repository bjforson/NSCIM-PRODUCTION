using System.Threading.Tasks;

namespace NickScanWebApp.Mobile.Services.Permissions
{
    public interface IPermissionProvider
    {
        bool IsAuthenticated { get; }
        bool HasPermission(string permission);
        bool HasAnyPermission(params string[] permissions);
        bool HasAllPermissions(params string[] permissions);
    }
}

