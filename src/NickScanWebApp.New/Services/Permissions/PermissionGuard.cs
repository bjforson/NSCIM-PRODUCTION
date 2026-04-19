using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NickScanWebApp.New.Services.Permissions
{
    /// <summary>
    /// Evaluates permissions using typed identifiers and records denied checks for diagnostics.
    /// </summary>
    public class PermissionGuard
    {
        private const int MaxDeniedEvents = 20;

        private readonly IPermissionProvider _permissionProvider;
        private readonly ILogger<PermissionGuard> _logger;
        private readonly ConcurrentQueue<DeniedPermissionEvent> _deniedEvents = new();

        public PermissionGuard(IPermissionProvider permissionProvider, ILogger<PermissionGuard> logger)
        {
            _permissionProvider = permissionProvider;
            _logger = logger;
        }

        public event Action<DeniedPermissionEvent>? PermissionDenied;

        public IReadOnlyCollection<DeniedPermissionEvent> GetDeniedEvents() => _deniedEvents.ToArray();

        public void ClearDeniedEvents()
        {
            while (_deniedEvents.TryDequeue(out _))
            {
                // discard
            }
        }

        public bool Can(PermissionId permission, string? context = null)
        {
            try
            {
                var allowed = _permissionProvider.HasPermission(permission.Value);
                if (!allowed)
                {
                    RegisterDenied(permission, context);
                }

                return allowed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate permission {Permission} in {Context}", permission.Value, context ?? "unknown context");
                RegisterDenied(permission, context);
                return false;
            }
        }

        public bool CanAny(string? context = null, params PermissionId[] permissions)
        {
            if (permissions == null || permissions.Length == 0)
            {
                return false;
            }

            foreach (var permission in permissions)
            {
                if (Can(permission, context))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanAny(params PermissionId[] permissions) => CanAny(null, permissions);

        public bool CanAll(string? context = null, params PermissionId[] permissions)
        {
            if (permissions == null || permissions.Length == 0)
            {
                return false;
            }

            foreach (var permission in permissions)
            {
                if (!Can(permission, context))
                {
                    return false;
                }
            }

            return true;
        }

        public bool CanAll(params PermissionId[] permissions) => CanAll(null, permissions);

        private void RegisterDenied(PermissionId permission, string? context)
        {
            var entry = new DeniedPermissionEvent(DateTime.UtcNow, permission, context);
            _deniedEvents.Enqueue(entry);
            while (_deniedEvents.Count > MaxDeniedEvents && _deniedEvents.TryDequeue(out _)) { }

            // ✅ IMPROVEMENT: Reduce log noise - NavMenu permission denials are expected behavior
            // Users without permissions shouldn't see menu items, so these are normal, not warnings
            if (context == "NavMenu")
            {
                // Use Debug level for expected NavMenu denials
                _logger.LogDebug("Permission denied: {Permission} (Context: {Context}) - Expected behavior for users without access",
                    permission.Value, context);
            }
            else
            {
                // Use Warning level for other contexts where denials might indicate a problem
                _logger.LogWarning("Permission denied: {Permission} (Context: {Context})",
                    permission.Value, context ?? "unspecified");
            }

            PermissionDenied?.Invoke(entry);
        }
    }

    public record DeniedPermissionEvent(DateTime TimestampUtc, PermissionId Permission, string? Context);
}

