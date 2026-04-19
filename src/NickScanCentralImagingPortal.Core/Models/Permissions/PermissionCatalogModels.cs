using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models.Permissions
{
    public record PermissionCatalogDto(
        string Version,
        DateTime GeneratedAtUtc,
        IReadOnlyList<PermissionCategoryDto> Categories);

    public record PermissionCategoryDto(
        string Id,
        string DisplayName,
        string? Description,
        IReadOnlyList<PermissionSummaryDto> Permissions);

    public record PermissionSummaryDto(
        string Name,
        string DisplayName,
        string Description,
        string Category,
        string Type,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> DefaultRoles,
        bool IsActive);
}

