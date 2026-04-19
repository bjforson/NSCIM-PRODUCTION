using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models.Permissions;
using NickScanCentralImagingPortal.Infrastructure.Data;
using CorePermissions = NickScanCentralImagingPortal.Core.Constants.Permissions;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public class PermissionCatalogBuilder : IPermissionCatalogBuilder
    {
        private static readonly JsonSerializerOptions SerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<PermissionCatalogBuilder> _logger;

        public PermissionCatalogBuilder(
            ApplicationDbContext context,
            ILogger<PermissionCatalogBuilder> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PermissionCatalogDto> BuildAsync(CancellationToken cancellationToken = default)
        {
            var generatedAtUtc = DateTime.UtcNow;

            var definitions = CorePermissions.GetAllPermissions();

            // Load current permission and role assignments from the database
            var permissionEntities = await _context.Permissions
                .AsNoTracking()
                .Select(p => new { p.Name, p.IsActive })
                .ToListAsync(cancellationToken);

            var permissionActivity = permissionEntities
                .ToDictionary(p => p.Name, p => p.IsActive, StringComparer.OrdinalIgnoreCase);

            var rolePermissions = await _context.Roles
                .AsNoTracking()
                .Where(r => r.IsActive)
                .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .ToListAsync(cancellationToken);

            var permissionRoleMap = BuildPermissionRoleMap(rolePermissions);

            var categories = definitions
                .GroupBy(def => string.IsNullOrWhiteSpace(def.Category) ? "Uncategorized" : def.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var permissions = group
                        .OrderBy(def => def.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .Select(def =>
                        {
                            var type = DeterminePermissionType(def.Name, group.Key);
                            var tags = Array.Empty<string>();
                            var roles = permissionRoleMap.TryGetValue(def.Name, out var roleList)
                                ? roleList
                                : Array.Empty<string>();
                            var isActive = permissionActivity.TryGetValue(def.Name, out var active) ? active : false;

                            return new PermissionSummaryDto(
                                def.Name,
                                def.DisplayName,
                                def.Description ?? string.Empty,
                                group.Key,
                                type,
                                tags,
                                roles,
                                isActive);
                        })
                        .ToList();

                    return new PermissionCategoryDto(
                        ToSlug(group.Key),
                        group.Key,
                        null,
                        permissions);
                })
                .ToList();

            var version = CalculateVersion(categories);

            _logger.LogInformation(
                "Built permission catalog with {PermissionCount} permissions across {CategoryCount} categories (version: {Version})",
                definitions.Count,
                categories.Count,
                version);

            return new PermissionCatalogDto(version, generatedAtUtc, categories);
        }

        private static Dictionary<string, string[]> BuildPermissionRoleMap(IEnumerable<Core.Entities.Role> roles)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var role in roles)
            {
                if (role.RolePermissions == null || role.RolePermissions.Count == 0)
                {
                    continue;
                }

                var roleName = !string.IsNullOrWhiteSpace(role.DisplayName)
                    ? role.DisplayName
                    : role.Name;

                foreach (var assignment in role.RolePermissions)
                {
                    var permissionName = assignment.Permission?.Name;
                    if (string.IsNullOrWhiteSpace(permissionName) || !(assignment.Permission?.IsActive ?? false))
                    {
                        continue;
                    }

                    if (!result.TryGetValue(permissionName, out var roleSet))
                    {
                        roleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        result[permissionName] = roleSet;
                    }

                    roleSet.Add(roleName);
                }
            }

            return result.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string DeterminePermissionType(string permissionName, string category)
        {
            if (permissionName.StartsWith("pages.", StringComparison.OrdinalIgnoreCase))
            {
                return "Page";
            }

            if (permissionName.StartsWith("controllers.", StringComparison.OrdinalIgnoreCase))
            {
                return "Controller";
            }

            if (permissionName.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
            {
                return "Api";
            }

            if (category.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                return "System";
            }

            return "Permission";
        }

        private static string ToSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "uncategorized";
            }

            Span<char> buffer = stackalloc char[value.Length];
            var length = 0;

            foreach (var c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToLowerInvariant(c);
                }
                else if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '/')
                {
                    if (length > 0 && buffer[length - 1] != '-')
                    {
                        buffer[length++] = '-';
                    }
                }
            }

            if (length == 0)
            {
                return "uncategorized";
            }

            var trimmed = new string(buffer[..length]).Trim('-');
            return string.IsNullOrEmpty(trimmed) ? "uncategorized" : trimmed;
        }

        private static string CalculateVersion(IEnumerable<PermissionCategoryDto> categories)
        {
            var payload = JsonSerializer.Serialize(categories, SerializationOptions);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

