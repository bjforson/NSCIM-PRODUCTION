using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Seeds the database with default permissions and system roles
    /// </summary>
    public class PermissionSeeder
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PermissionSeeder> _logger;

        public PermissionSeeder(ApplicationDbContext context, ILogger<PermissionSeeder> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Seed all permissions and default roles
        /// </summary>
        public async Task SeedAsync()
        {
            _logger.LogInformation("[PERMISSION-SEEDER] Starting permission and role seeding...");

            try
            {
                // Step 1: Seed Permissions
                await SeedPermissionsAsync();

                // Step 2: Seed Default Roles
                await SeedDefaultRolesAsync();

                // Step 3: Assign Permissions to Roles
                await AssignPermissionsToRolesAsync();

                _logger.LogInformation("[PERMISSION-SEEDER] Seeding completed successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-SEEDER] Error during seeding: {Message}", ex.Message);
                throw;
            }
        }

        private async Task SeedPermissionsAsync()
        {
            _logger.LogInformation("[PERMISSION-SEEDER] Seeding permissions...");

            var allPermissions = Permissions.GetAllPermissions();
            var existingPermissions = await _context.Permissions
                .Where(p => p.IsActive)
                .Select(p => p.Name)
                .ToListAsync();

            var permissionsToAdd = allPermissions
                .Where(p => !existingPermissions.Contains(p.Name))
                .Select(p => new Permission
                {
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Description = p.Description,
                    Category = p.Category,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList();

            if (permissionsToAdd.Any())
            {
                await _context.Permissions.AddRangeAsync(permissionsToAdd);
                await _context.SaveChangesAsync();
                _logger.LogInformation("[PERMISSION-SEEDER] Added {Count} new permissions", permissionsToAdd.Count);
            }
            else
            {
                _logger.LogInformation("[PERMISSION-SEEDER] All permissions already exist");
            }
        }

        private async Task SeedDefaultRolesAsync()
        {
            _logger.LogInformation("[PERMISSION-SEEDER] Seeding default system roles...");

            var defaultRoles = new[]
            {
                new { Name = "SuperAdmin", DisplayName = "Super Administrator", Description = "Full system access with all permissions", BaseRole = UserRole.SuperAdmin },
                new { Name = "Admin", DisplayName = "Administrator", Description = "System administration with most permissions", BaseRole = UserRole.Admin },
                new { Name = "Manager", DisplayName = "Manager", Description = "Department-level access and user management", BaseRole = UserRole.Manager },
                new { Name = "Supervisor", DisplayName = "Supervisor", Description = "Team lead with approval capabilities", BaseRole = UserRole.Supervisor },
                new { Name = "ScannerOperator", DisplayName = "Scanner Operator", Description = "Scanner equipment operations", BaseRole = UserRole.ScannerOperator },
                new { Name = "Operator", DisplayName = "Operator", Description = "Basic operational access", BaseRole = UserRole.Operator },
                new { Name = "Viewer", DisplayName = "Viewer", Description = "Read-only access for viewing and reporting", BaseRole = UserRole.Viewer },
                new { Name = "Analyst", DisplayName = "Image Analyst", Description = "Performs primary image analysis workflows", BaseRole = UserRole.Operator },
                new { Name = "Audit", DisplayName = "Audit Reviewer", Description = "Conducts secondary audit review of analyst decisions", BaseRole = UserRole.Supervisor }
            };

            foreach (var roleData in defaultRoles)
            {
                var existingRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleData.Name);
                if (existingRole == null)
                {
                    var newRole = new Role
                    {
                        Name = roleData.Name,
                        DisplayName = roleData.DisplayName,
                        Description = roleData.Description,
                        BaseRole = roleData.BaseRole,
                        IsSystemRole = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System"
                    };

                    await _context.Roles.AddAsync(newRole);
                    _logger.LogInformation("[PERMISSION-SEEDER] Created role: {RoleName}", roleData.Name);
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("[PERMISSION-SEEDER] Default roles seeding completed");
        }

        private async Task AssignPermissionsToRolesAsync()
        {
            _logger.LogInformation("[PERMISSION-SEEDER] Assigning permissions to roles...");

            // Get all permissions and roles
            var allPermissions = await _context.Permissions.ToListAsync();
            var allRoles = await _context.Roles.ToListAsync();

            // Define role-permission mappings (used only for initial seed)
            var roleMappings = new Dictionary<string, List<string>>
            {
                ["Viewer"] = GetViewerPermissions(),
                ["Operator"] = GetOperatorPermissions(),
                ["Analyst"] = GetAnalystPermissions(),
                ["Audit"] = GetAuditPermissions(),
                ["ScannerOperator"] = GetScannerOperatorPermissions(),
                ["Supervisor"] = GetSupervisorPermissions(),
                ["Manager"] = GetManagerPermissions(),
                ["Admin"] = GetAdminPermissions(),
                ["SuperAdmin"] = GetSuperAdminPermissions()
            };

            foreach (var mapping in roleMappings)
            {
                var role = allRoles.FirstOrDefault(r => r.Name == mapping.Key);
                if (role == null) continue;

                var existingPermissions = await _context.RolePermissions
                    .Where(rp => rp.RoleId == role.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync();

                // System roles always get their full permission set re-applied.
                // Custom (non-system) roles are skipped to preserve user customizations.
                var roleEntity = allRoles.FirstOrDefault(r => r.Name == mapping.Key);
                if (existingPermissions.Any() && roleEntity?.IsSystemRole != true)
                {
                    _logger.LogDebug("[PERMISSION-SEEDER] Skipping {RoleName} - custom role with {Count} permissions (user customizations preserved)",
                        mapping.Key, existingPermissions.Count);
                    continue;
                }

                var addedPermissionIds = new HashSet<int>(existingPermissions);

                foreach (var permissionName in mapping.Value)
                {
                    var permission = allPermissions.FirstOrDefault(p => p.Name == permissionName);
                    if (permission == null)
                    {
                        _logger.LogWarning("[PERMISSION-SEEDER] Permission not found: {PermissionName}", permissionName);
                        continue;
                    }

                    if (addedPermissionIds.Add(permission.Id))
                    {
                        await _context.RolePermissions.AddAsync(new RolePermission
                        {
                            RoleId = role.Id,
                            PermissionId = permission.Id,
                            GrantedAt = DateTime.UtcNow,
                            GrantedBy = "System"
                        });
                    }
                }

                _logger.LogInformation("[PERMISSION-SEEDER] Assigned {Count} permissions to {RoleName}",
                    mapping.Value.Count, mapping.Key);
            }

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("[PERMISSION-SEEDER] Permission assignment completed");
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("duplicate key") == true
                                                || ex.InnerException?.Message?.Contains("unique constraint") == true)
            {
                _logger.LogWarning("[PERMISSION-SEEDER] Some role-permissions already existed (prior partial seed). Skipping duplicates.");
                _context.ChangeTracker.Clear();
            }
        }

        #region Permission Lists by Role

        private List<string> GetViewerPermissions()
        {
            return new List<string>
            {
                // Dashboard - Read only
                Permissions.DashboardView,
                
                // Containers - View only
                Permissions.ContainersView,
                Permissions.ContainersSearch,
                Permissions.ContainersExport,
                Permissions.ContainersDetailsView,
                
                // ICUMS - View only
                Permissions.IcumsView,
                Permissions.IcumsRefresh,
                
                // Images - View only
                Permissions.ImagesView,
                Permissions.ImagesFullscreen,
                Permissions.ImagesTools,
                
                // Scanners - View only
                Permissions.ScannersView,
                Permissions.ScannersStatus,
                
                // Vehicles - View only
                Permissions.VehiclesView,
                Permissions.VehiclesSearch,
                Permissions.VehiclesExport,
                Permissions.VehiclesDetailsView,
                
                // Reports
                Permissions.ReportsView,
                Permissions.ReportsExport,
                
                // Page Access - Read only pages
                Permissions.PagesDashboardView,
                Permissions.PagesContainersView,
                Permissions.PagesContainersDetails,
                Permissions.PagesScannersView,
                Permissions.PagesIcumsView,
                Permissions.PagesVehiclesView,
                Permissions.PagesReportsView,
                Permissions.PagesNotifications,
                
                // Controller Access - Read only endpoints
                Permissions.ControllersContainersView,
                Permissions.ControllersContainersDetails,
                Permissions.ControllersIcumsView,
                Permissions.ControllersCompletenessView,
                Permissions.ControllersPermissionsMy
            };
        }

        private List<string> GetOperatorPermissions()
        {
            var permissions = GetViewerPermissions(); // Inherit all Viewer permissions

            permissions.AddRange(new List<string>
            {
                // Dashboard - Refresh
                Permissions.DashboardRefresh,

                // Containers - Edit capabilities
                Permissions.ContainersCreate,
                Permissions.ContainersEdit,
                Permissions.ContainersAnnotate,
                Permissions.ContainersValidate,
                
                // ICUMS - Download capabilities
                Permissions.IcumsDownload,
                
                // Images - Annotation
                Permissions.ImagesDownload,
                Permissions.ImagesAnnotate,
                
                // Vehicles - Edit
                Permissions.VehiclesCreate,
                Permissions.VehiclesEdit,
                
                // Reports
                Permissions.ReportsGenerate,
                
                // Page Access - Operations pages
                Permissions.PagesContainerProcessing,
                Permissions.PagesSearch,
                Permissions.PagesIcumsBatchDownload,
                
                // Controller Access - Edit endpoints
                Permissions.ControllersContainersCreate,
                Permissions.ControllersContainersEdit,
                Permissions.ControllersIcumsDownload
            });

            return permissions;
        }

        private List<string> GetAnalystPermissions()
        {
            var permissions = GetViewerPermissions();

            permissions.AddRange(new List<string>
            {
                // Dashboard
                Permissions.DashboardRefresh,

                // Images
                Permissions.ImagesDownload,
                Permissions.ImagesAnnotate,
                Permissions.ImagesAnalysisView,
                
                // Pages
                Permissions.PagesImageAnalysisView,
                Permissions.PagesCompletedRecords,
                Permissions.PagesCrossRecordScans,
                Permissions.PagesContainerCompleteness,
                Permissions.PagesSearch,
                Permissions.PagesValidationBoeLookup,
                Permissions.PagesValidationRecordCompleteness,
                
                // Controllers
                Permissions.ControllersImageAnalysisMyAssignments,
                Permissions.ControllersImageAnalysisAvailable,
                Permissions.ControllersImageAnalysisClaim,
                Permissions.ControllersImageAnalysisDecisionAnalyst,
                Permissions.ControllersImageAnalysisLeaseRenew,
                Permissions.ControllersAiWorkflow
            });

            return permissions.Distinct().ToList();
        }

        private List<string> GetAuditPermissions()
        {
            var permissions = GetAnalystPermissions();

            permissions.Remove(Permissions.ControllersImageAnalysisDecisionAnalyst);
            permissions.Add(Permissions.ControllersImageAnalysisDecisionAudit);
            permissions.Add(Permissions.PagesImageAnalysisAudit);

            // Audit reviewers need audit trail access
            permissions.Add(Permissions.AuditView);
            permissions.Add(Permissions.AuditSearch);

            return permissions.Distinct().ToList();
        }

        private List<string> GetScannerOperatorPermissions()
        {
            var permissions = GetOperatorPermissions();

            permissions.AddRange(new List<string>
            {
                // Scanners - Full control
                Permissions.ScannersConfigure,
                Permissions.ScannersManage,
                Permissions.ScannersDiagnostics,
                Permissions.ScannersTriggerScan,
                Permissions.ScannersAseView,
                Permissions.ScannersAseSync,
                Permissions.ScannersFs6000View,
                Permissions.ScannersFs6000Sync,
                Permissions.ScannersIngestionMonitor,
                Permissions.ScannersIngestionTrigger,
                
                // Images - Edit & Upload
                Permissions.ImagesEdit,
                Permissions.ImagesUpload,
                
                // Page Access - Scanner pages
                Permissions.PagesScannersAse,
                Permissions.PagesScannersFs6000,
                Permissions.PagesScannersHeimann
            });

            return permissions;
        }

        private List<string> GetSupervisorPermissions()
        {
            var permissions = GetScannerOperatorPermissions();

            permissions.AddRange(new List<string>
            {
                // Dashboard - Comprehensive view for team leads
                Permissions.DashboardComprehensive,

                // Containers - Approval capabilities
                Permissions.ContainersApprove,
                Permissions.ContainersReject,
                Permissions.ContainersBulkOperations,
                Permissions.ContainersCompletenessView,
                
                // Vehicles - Approval
                Permissions.VehiclesApprove,
                Permissions.VehiclesReject,
                
                // System - Monitoring
                Permissions.SystemLogsView,
                Permissions.SystemLogsFilter,
                Permissions.SystemPerformanceView,
                Permissions.SystemHealthView,
                
                // Audit
                Permissions.AuditView,
                Permissions.AuditSearch,
                
                // Reports
                Permissions.ReportsSchedule,
                
                // Page Access - Supervisor pages
                Permissions.PagesContainerCompleteness,
                Permissions.PagesCmrValidation,
                Permissions.PagesPerformance,
                Permissions.PagesIcumsDownloadQueue,
                Permissions.PagesOperationsErrors,
                
                // Controller Access - Approval endpoints
                Permissions.ControllersContainersApprove,
                Permissions.ControllersContainersReject,

                // Assistive AI (ops triage / monitoring)
                Permissions.ControllersAiWorkflow
            });

            return permissions;
        }

        private List<string> GetManagerPermissions()
        {
            var permissions = GetSupervisorPermissions();

            permissions.AddRange(new List<string>
            {
                // Users - Management (limited)
                Permissions.UsersView,
                Permissions.UsersCreate,
                Permissions.UsersEdit,
                Permissions.UsersDeactivate,
                Permissions.UsersActivate,
                Permissions.UsersResetPassword,
                Permissions.UsersActivityView,
                
                // Roles - View and assign
                Permissions.RolesView,
                Permissions.RolesAssign,
                
                // System - More access
                Permissions.SystemLogsDownload,
                Permissions.SystemPerformanceMonitor,
                Permissions.SystemConfigView,
                
                // Audit
                Permissions.AuditExport,
                Permissions.UsersViewAudit,
                
                // Analytics
                Permissions.AnalyticsView,

                // ICUMS
                Permissions.IcumsQueueManagement,
                Permissions.IcumsExport,
                
                // Containers - Completeness management
                Permissions.ContainersCompletenessManage,

                // Page Access - Manager pages
                Permissions.PagesDashboardAnalytics,
                Permissions.PagesImageAnalysisView,
                Permissions.PagesImageAnalysisAudit,
                Permissions.PagesCompletedRecords,
                Permissions.PagesCrossRecordScans,
                Permissions.PagesIcumsSubmissionQueue,
                Permissions.PagesIcumsBoeRequest,
                Permissions.PagesIcumsLooseCargo,
                Permissions.PagesIcumsAnalytics,
                Permissions.PagesValidationCompleteness,
                Permissions.PagesReportsTemplates,
                Permissions.PagesAdminUsers,
                Permissions.PagesAdminAudit,
                
                // Controller Access - Manager endpoints
                Permissions.ControllersUsersView,
                Permissions.ControllersUsersCreate,
                Permissions.ControllersUsersEdit,
                Permissions.ControllersUsersPassword,
                Permissions.ControllersRolesView,
                Permissions.ControllersImageAnalysisMyAssignments,
                Permissions.ControllersImageAnalysisAvailable,
                Permissions.ControllersIcumsQueueSubmission,
                Permissions.ControllersCompletenessManage
            });

            return permissions;
        }

        private List<string> GetAdminPermissions()
        {
            var permissions = GetManagerPermissions();

            permissions.AddRange(new List<string>
            {
                // Dashboard - Full access
                Permissions.DashboardExport,

                // Users - Full control
                Permissions.UsersDelete,
                Permissions.UsersManageRoles,
                
                // Roles - Full control
                Permissions.RolesCreate,
                Permissions.RolesEdit,
                Permissions.RolesDelete,
                Permissions.RolesManagePermissions,
                
                // Permissions - Full control
                Permissions.PermissionsView,
                Permissions.PermissionsManage,
                Permissions.PermissionsAssign,
                Permissions.PermissionsCreate,
                Permissions.PermissionsEdit,
                Permissions.PermissionsDelete,
                
                // System - Full control
                Permissions.SystemLogsClear,
                Permissions.SystemLogsExport,
                Permissions.SystemServicesView,
                Permissions.SystemServicesStart,
                Permissions.SystemServicesStop,
                Permissions.SystemServicesRestart,
                Permissions.SystemServicesControl,
                Permissions.SystemDatabaseView,
                Permissions.SystemDatabaseBrowse,
                Permissions.SystemDatabaseQuery,
                Permissions.SystemDatabaseBackup,
                Permissions.SystemConfigEdit,
                Permissions.SystemSettingsView,
                Permissions.SystemSettingsEdit,
                Permissions.SystemMonitoringView,
                Permissions.SystemFilesBrowse,
                Permissions.SystemFilesDownload,
                Permissions.SystemFilesDelete,

                // Database - Full control
                Permissions.DatabaseBackup,
                Permissions.DatabaseRestore,
                Permissions.DatabaseMaintenance,
                Permissions.DatabaseQuery,
                Permissions.DatabaseView,

                // Analytics - Full access
                Permissions.AnalyticsAdvanced,

                // Performance
                Permissions.PerformanceMetricsView,

                // Diagnostics
                Permissions.DiagnosticsView,
                Permissions.DiagnosticsRun,

                // API Tools
                Permissions.ApiTest,
                Permissions.ApiDebug,

                // ICUMS - Full control
                Permissions.IcumsManualIngestion,
                Permissions.IcumsSync,
                Permissions.IcumsSubmit,
                Permissions.IcumsDownloadQueue,
                Permissions.IcumsSubmissionQueue,
                Permissions.IcumsManualRequest,
                
                // Containers - Full control
                Permissions.ContainersDelete,
                
                // Vehicles - Full control
                Permissions.VehiclesDelete,
                
                // Images - Full control
                Permissions.ImagesDelete,
                Permissions.ImagesExport,
                Permissions.ImagesAnalysisView,
                Permissions.ImagesAnalysisTrigger,

                // Page Access - All admin and services pages
                Permissions.PagesImageAnalysisManagement,
                Permissions.PagesValidationRules,
                Permissions.PagesAdminRoles,
                Permissions.PagesAdminPermissions,
                Permissions.PagesAdminSettings,
                Permissions.PagesAdminLogs,
                Permissions.PagesAdminDatabase,
                Permissions.PagesAdminServiceControl,
                Permissions.PagesServicesMonitoring,
                Permissions.PagesServicesPerformanceMetrics,
                Permissions.PagesServicesDatabase,
                Permissions.PagesServicesDiagnostics,
                Permissions.PagesServicesGateway,
                Permissions.PagesServicesIngestion,
                Permissions.PagesServicesImageProcessing,
                Permissions.PagesServicesAseSync,
                Permissions.PagesServicesFs6000Completeness,
                Permissions.PagesServicesConsolidatedCargo,
                Permissions.PagesServicesAccessReview,
                Permissions.PagesServicesDebug,
                
                // Controller Access - All controller permissions
                Permissions.ControllersImageAnalysisAssign,
                Permissions.ControllersImageAnalysisClaim,
                Permissions.ControllersImageAnalysisDecisionAnalyst,
                Permissions.ControllersImageAnalysisDecisionAudit,
                Permissions.ControllersImageAnalysisLeaseRenew,
                Permissions.ControllersImageAnalysisManagementView,
                Permissions.ControllersImageAnalysisManagementSettings,
                Permissions.ControllersImageAnalysisIntake,
                Permissions.ControllersImageAnalysisSubmit,
                Permissions.ControllersImageAnalysisUsers,
                Permissions.ControllersContainersDelete,
                Permissions.ControllersIcumsSubmit,
                Permissions.ControllersIcumsQueueDownload,
                Permissions.ControllersIcumsQueueSubmission,
                Permissions.ControllersIcumsManual,
                Permissions.ControllersUsersDelete,
                Permissions.ControllersUsersRoles,
                Permissions.ControllersRolesCreate,
                Permissions.ControllersRolesEdit,
                Permissions.ControllersRolesDelete,
                Permissions.ControllersRolesPermissions,
                Permissions.ControllersPermissionsView,
                Permissions.ControllersPermissionsManage,
                Permissions.ControllersPermissionsMy,
                Permissions.ControllersSystemSettings,
                Permissions.ControllersSystemLogs,
                Permissions.ControllersSystemServices,
                Permissions.ControllersSystemDatabase,
                Permissions.ControllersSystemAudit
            });

            return permissions;
        }

        private List<string> GetSuperAdminPermissions()
        {
            // SuperAdmin gets ALL permissions
            return Permissions.GetAllPermissions().Select(p => p.Name).ToList();
        }

        #endregion
    }
}

