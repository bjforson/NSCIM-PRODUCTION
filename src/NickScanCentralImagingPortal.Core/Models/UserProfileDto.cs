using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models
{
    public record UserProfileDto(
        int UserId,
        string Username,
        string DisplayName,
        string Email,
        string PrimaryRole,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        bool HasAllPermissions,
        DateTime IssuedAtUtc,
        bool IsActive);
}

