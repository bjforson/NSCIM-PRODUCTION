namespace NickScanCentralImagingPortal.Core.Constants
{
    /// <summary>
    /// Defines all granular permissions in the system
    /// </summary>
    public static class Permissions
    {
        // ==================== DASHBOARD ====================
        public const string DashboardView = "dashboard.view";
        public const string DashboardRefresh = "dashboard.refresh";
        public const string DashboardExport = "dashboard.export";
        public const string DashboardComprehensive = "dashboard.comprehensive";

        // ==================== CONTAINERS ====================
        public const string ContainersView = "containers.view";
        public const string ContainersSearch = "containers.search";
        public const string ContainersCreate = "containers.create";
        public const string ContainersEdit = "containers.edit";
        public const string ContainersDelete = "containers.delete";
        public const string ContainersApprove = "containers.approve";
        public const string ContainersReject = "containers.reject";
        public const string ContainersExport = "containers.export";
        public const string ContainersBulkOperations = "containers.bulk";
        public const string ContainersAnnotate = "containers.annotate";
        public const string ContainersValidate = "containers.validate";
        public const string ContainersDetailsView = "containers.details.view";
        public const string ContainersCompletenessView = "containers.completeness.view";
        public const string ContainersCompletenessManage = "containers.completeness.manage";

        // ==================== ICUMS ====================
        public const string IcumsView = "icums.view";
        public const string IcumsDownload = "icums.download";
        public const string IcumsRefresh = "icums.refresh";
        public const string IcumsManualIngestion = "icums.ingest";
        public const string IcumsQueueManagement = "icums.queue";
        public const string IcumsSync = "icums.sync";
        public const string IcumsSubmit = "icums.submit";
        public const string IcumsExport = "icums.export";
        public const string IcumsDownloadQueue = "icums.download.queue";
        public const string IcumsSubmissionQueue = "icums.submission.queue";
        public const string IcumsManualRequest = "icums.manual.request";

        // ==================== IMAGES ====================
        public const string ImagesView = "images.view";
        public const string ImagesDownload = "images.download";
        public const string ImagesAnnotate = "images.annotate";
        public const string ImagesEdit = "images.edit";
        public const string ImagesFullscreen = "images.fullscreen";
        public const string ImagesTools = "images.tools"; // Zoom, pan, rotate, etc.
        public const string ImagesDelete = "images.delete";
        public const string ImagesExport = "images.export";
        public const string ImagesUpload = "images.upload";
        public const string ImagesAnalysisView = "images.analysis.view";
        public const string ImagesAnalysisTrigger = "images.analysis.trigger";

        // ==================== SCANNERS ====================
        public const string ScannersView = "scanners.view";
        public const string ScannersStatus = "scanners.status";
        public const string ScannersConfigure = "scanners.configure";
        public const string ScannersManage = "scanners.manage";
        public const string ScannersDiagnostics = "scanners.diagnostics";
        public const string ScannersTriggerScan = "scanners.trigger";
        public const string ScannersAseView = "scanners.ase.view";
        public const string ScannersAseSync = "scanners.ase.sync";
        public const string ScannersFs6000View = "scanners.fs6000.view";
        public const string ScannersFs6000Sync = "scanners.fs6000.sync";
        public const string ScannersIngestionMonitor = "scanners.ingestion.monitor";
        public const string ScannersIngestionTrigger = "scanners.ingestion.trigger";

        // ==================== VEHICLES ====================
        public const string VehiclesView = "vehicles.view";
        public const string VehiclesSearch = "vehicles.search";
        public const string VehiclesCreate = "vehicles.create";
        public const string VehiclesEdit = "vehicles.edit";
        public const string VehiclesDelete = "vehicles.delete";
        public const string VehiclesApprove = "vehicles.approve";
        public const string VehiclesReject = "vehicles.reject";
        public const string VehiclesExport = "vehicles.export";
        public const string VehiclesDetailsView = "vehicles.details.view";

        // ==================== USERS ====================
        public const string UsersView = "users.view";
        public const string UsersCreate = "users.create";
        public const string UsersEdit = "users.edit";
        public const string UsersDelete = "users.delete";
        public const string UsersDeactivate = "users.deactivate";
        public const string UsersManageRoles = "users.roles";
        public const string UsersResetPassword = "users.password";
        public const string UsersViewAudit = "users.audit";
        public const string UsersActivate = "users.activate";
        public const string UsersActivityView = "users.activity.view";

        // ==================== ROLES & PERMISSIONS ====================
        public const string RolesView = "roles.view";
        public const string RolesCreate = "roles.create";
        public const string RolesEdit = "roles.edit";
        public const string RolesDelete = "roles.delete";
        public const string RolesManagePermissions = "roles.permissions";
        public const string RolesAssign = "roles.assign";
        public const string PermissionsView = "permissions.view";
        public const string PermissionsManage = "permissions.manage";
        public const string PermissionsAssign = "permissions.assign";
        public const string PermissionsCreate = "permissions.create";
        public const string PermissionsEdit = "permissions.edit";
        public const string PermissionsDelete = "permissions.delete";

        // ==================== SYSTEM ADMINISTRATION ====================
        // Logs
        public const string SystemLogsView = "system.logs.view";
        public const string SystemLogsDownload = "system.logs.download";
        public const string SystemLogsClear = "system.logs.clear";
        public const string SystemLogsFilter = "system.logs.filter";

        // Services
        public const string SystemServicesView = "system.services.view";
        public const string SystemServicesStart = "system.services.start";
        public const string SystemServicesStop = "system.services.stop";
        public const string SystemServicesRestart = "system.services.restart";

        // Database
        public const string SystemDatabaseView = "system.database.view";
        public const string SystemDatabaseBrowse = "system.database.browse";
        public const string SystemDatabaseQuery = "system.database.query";
        public const string SystemDatabaseBackup = "system.database.backup";
        public const string DatabaseBackup = "database.backup";
        public const string DatabaseRestore = "database.restore";
        public const string DatabaseMaintenance = "database.maintenance";
        public const string DatabaseQuery = "database.query";
        public const string DatabaseView = "database.view";

        // Configuration
        public const string SystemConfigView = "system.config.view";
        public const string SystemConfigEdit = "system.config.edit";
        public const string SystemSettingsView = "system.settings.view";
        public const string SystemSettingsEdit = "system.settings.edit";
        public const string SystemHealthView = "system.health.view";
        public const string SystemMonitoringView = "system.monitoring.view";
        public const string SystemLogsExport = "system.logs.export";
        public const string SystemServicesControl = "system.services.control";

        // Performance
        public const string SystemPerformanceView = "system.performance.view";
        public const string SystemPerformanceMonitor = "system.performance.monitor";
        public const string PerformanceMetricsView = "performance.metrics.view";

        // Files
        public const string SystemFilesBrowse = "system.files.browse";
        public const string SystemFilesDownload = "system.files.download";
        public const string SystemFilesDelete = "system.files.delete";

        // ==================== REPORTS ====================
        public const string ReportsView = "reports.view";
        public const string ReportsGenerate = "reports.generate";
        public const string ReportsExport = "reports.export";
        public const string ReportsSchedule = "reports.schedule";

        // ==================== AUDIT ====================
        public const string AuditView = "audit.view";
        public const string AuditExport = "audit.export";
        public const string AuditSearch = "audit.search";

        // ==================== ANALYTICS ====================
        public const string AnalyticsView = "analytics.view";
        public const string AnalyticsAdvanced = "analytics.advanced";

        // ==================== API & DIAGNOSTICS ====================
        public const string ApiTest = "api.test";
        public const string ApiDebug = "api.debug";
        public const string DiagnosticsView = "diagnostics.view";
        public const string DiagnosticsRun = "diagnostics.run";

        // ==================== PAGE ACCESS PERMISSIONS ====================
        // Dashboard
        public const string PagesDashboardView = "pages.dashboard.view";
        public const string PagesDashboardAnalytics = "pages.dashboard.analytics";

        // Operations
        public const string PagesContainersView = "pages.containers.view";
        public const string PagesContainersDetails = "pages.containers.details";
        public const string PagesContainerProcessing = "pages.containerprocessing.view";
        public const string PagesContainerCompleteness = "pages.containercompleteness.view";
        public const string PagesImageAnalysisView = "pages.imageanalysis.view";
        public const string PagesImageAnalysisAudit = "pages.imageanalysis.audit";
        public const string PagesCmrValidation = "pages.cmrvalidation.view";
        public const string PagesCompletedRecords = "pages.completedrecords.view";
        public const string PagesCrossRecordScans = "pages.crossrecordscans.view";
        public const string PagesVehiclesView = "pages.vehicles.view";

        // Validation
        public const string PagesImageAnalysisManagement = "pages.imageanalysis.management";
        public const string PagesValidationRules = "pages.validation.rules";
        public const string PagesValidationCompleteness = "pages.validation.completeness";
        public const string PagesValidationMatchCorrections = "pages.validation.matchcorrections";
        public const string PagesValidationBoeLookup = "pages.validation.boelookup";
        public const string PagesValidationRecordCompleteness = "pages.validation.recordcompleteness";
        public const string PagesValidationXrayInspector = "pages.validation.xrayinspector";
        public const string XrayInspectorAnalyze = "xrayinspector.analyze";
        public const string XrayInspectorExport = "xrayinspector.export";

        // Scanners
        public const string PagesScannersView = "pages.scanners.view";
        public const string PagesScannersAse = "pages.scanners.ase";
        public const string PagesScannersFs6000 = "pages.scanners.fs6000";
        public const string PagesScannersHeimann = "pages.scanners.heimann";

        // ICUMS
        public const string PagesIcumsView = "pages.icums.view";
        public const string PagesIcumsDownloadQueue = "pages.icums.downloadqueue";
        public const string PagesIcumsSubmissionQueue = "pages.icums.submissionqueue";
        public const string PagesIcumsBoeRequest = "pages.icums.boerequest";
        public const string PagesIcumsLooseCargo = "pages.icums.loosecargo";
        public const string PagesIcumsAnalytics = "pages.icums.analytics";
        public const string PagesIcumsBatchDownload = "pages.icums.batchdownload";
        public const string PagesIcumsPayloads = "pages.icums.payloads";
        public const string PagesIcumsVerifyStatus = "pages.icums.verifystatus";

        // Administration
        public const string PagesAdminUsers = "pages.admin.users";
        public const string PagesAdminRoles = "pages.admin.roles";
        public const string PagesAdminPermissions = "pages.admin.permissions";
        public const string PagesAdminSettings = "pages.admin.settings";
        public const string PagesAdminLogs = "pages.admin.logs";
        public const string PagesAdminDatabase = "pages.admin.database";
        public const string PagesAdminAudit = "pages.admin.audit";
        public const string PagesAdminServiceControl = "pages.admin.servicecontrol";

        // Reports
        public const string PagesReportsView = "pages.reports.view";
        public const string PagesReportsTemplates = "pages.reports.templates";

        // Operations - Error Monitoring
        public const string PagesOperationsErrors = "pages.operations.errors";

        // Other
        public const string PagesSearch = "pages.search";
        public const string PagesNotifications = "pages.notifications";
        public const string PagesPerformance = "pages.performance";

        // Services
        public const string PagesServicesMonitoring = "pages.services.monitoring";
        public const string PagesServicesPerformanceMetrics = "pages.services.performancemetrics";
        public const string PagesServicesDatabase = "pages.services.database";
        public const string PagesServicesDiagnostics = "pages.services.diagnostics";
        public const string PagesServicesGateway = "pages.services.gateway";
        public const string PagesServicesIngestion = "pages.services.ingestion";
        public const string PagesServicesImageProcessing = "pages.services.imageprocessing";
        public const string PagesServicesAseSync = "pages.services.asesync";
        public const string PagesServicesFs6000Completeness = "pages.services.fs6000completeness";
        public const string PagesServicesConsolidatedCargo = "pages.services.consolidatedcargo";
        public const string PagesServicesAccessReview = "pages.services.accessreview";
        public const string PagesServicesDebug = "pages.services.debug";

        // ==================== CONTROLLER ACCESS PERMISSIONS ====================
        // Image Analysis Controllers
        public const string ControllersImageAnalysisAssign = "controllers.imageanalysis.assign";
        public const string ControllersImageAnalysisClaim = "controllers.imageanalysis.claim";
        public const string ControllersImageAnalysisDecisionAnalyst = "controllers.imageanalysis.decision.analyst";
        public const string ControllersImageAnalysisDecisionAudit = "controllers.imageanalysis.decision.audit";
        public const string ControllersImageAnalysisLeaseRenew = "controllers.imageanalysis.lease.renew";
        public const string ControllersImageAnalysisManagementView = "controllers.imageanalysis.management.view";
        public const string ControllersImageAnalysisManagementSettings = "controllers.imageanalysis.management.settings";
        public const string ControllersImageAnalysisIntake = "controllers.imageanalysis.intake";
        public const string ControllersImageAnalysisSubmit = "controllers.imageanalysis.submit";
        public const string ControllersImageAnalysisUsers = "controllers.imageanalysis.users";
        public const string ControllersImageAnalysisMyAssignments = "controllers.imageanalysis.myassignments";
        public const string ControllersImageAnalysisAvailable = "controllers.imageanalysis.available";

        // Container Controllers
        public const string ControllersContainersView = "controllers.containers.view";
        public const string ControllersContainersCreate = "controllers.containers.create";
        public const string ControllersContainersEdit = "controllers.containers.edit";
        public const string ControllersContainersDelete = "controllers.containers.delete";
        public const string ControllersContainersApprove = "controllers.containers.approve";
        public const string ControllersContainersReject = "controllers.containers.reject";
        public const string ControllersContainersDetails = "controllers.containers.details";

        // Container Completeness
        public const string ControllersCompletenessView = "controllers.completeness.view";
        public const string ControllersCompletenessManage = "controllers.completeness.manage";

        // ICUMS Controllers
        public const string ControllersIcumsView = "controllers.icums.view";
        public const string ControllersIcumsDownload = "controllers.icums.download";
        public const string ControllersIcumsSubmit = "controllers.icums.submit";
        public const string ControllersIcumsQueueDownload = "controllers.icums.queue.download";
        public const string ControllersIcumsQueueSubmission = "controllers.icums.queue.submission";
        public const string ControllersIcumsManual = "controllers.icums.manual";

        // Users Controllers
        public const string ControllersUsersView = "controllers.users.view";
        public const string ControllersUsersCreate = "controllers.users.create";
        public const string ControllersUsersEdit = "controllers.users.edit";
        public const string ControllersUsersDelete = "controllers.users.delete";
        public const string ControllersUsersPassword = "controllers.users.password";
        public const string ControllersUsersRoles = "controllers.users.roles";

        // Roles Controllers
        public const string ControllersRolesView = "controllers.roles.view";
        public const string ControllersRolesCreate = "controllers.roles.create";
        public const string ControllersRolesEdit = "controllers.roles.edit";
        public const string ControllersRolesDelete = "controllers.roles.delete";
        public const string ControllersRolesPermissions = "controllers.roles.permissions";

        // Permissions Controllers
        public const string ControllersPermissionsView = "controllers.permissions.view";
        public const string ControllersPermissionsManage = "controllers.permissions.manage";
        public const string ControllersPermissionsMy = "controllers.permissions.my";

        // System Controllers
        public const string ControllersSystemSettings = "controllers.system.settings";
        public const string ControllersSystemLogs = "controllers.system.logs";
        public const string ControllersSystemServices = "controllers.system.services";
        public const string ControllersSystemDatabase = "controllers.system.database";
        public const string ControllersSystemAudit = "controllers.system.audit";

        /// <summary>Assistive AI, lineage, ops triage, and training export APIs.</summary>
        public const string ControllersAiWorkflow = "controllers.ai.workflow";

        /// <summary>
        /// Get all permissions as a list
        /// </summary>
        public static List<PermissionDefinition> GetAllPermissions()
        {
            return new List<PermissionDefinition>
            {
                // Dashboard
                new(DashboardView, "View Dashboard", "Access to the main dashboard", "Dashboard"),
                new(DashboardRefresh, "Refresh Dashboard", "Ability to refresh dashboard data", "Dashboard"),
                new(DashboardExport, "Export Dashboard", "Export dashboard data and reports", "Dashboard"),
                new(DashboardComprehensive, "View Comprehensive Dashboard", "Access comprehensive dashboard with all metrics", "Dashboard"),
                
                // Containers
                new(ContainersView, "View Containers", "View container details and lists", "Containers"),
                new(ContainersSearch, "Search Containers", "Search and filter containers", "Containers"),
                new(ContainersCreate, "Create Containers", "Create new container records", "Containers"),
                new(ContainersEdit, "Edit Containers", "Modify container information", "Containers"),
                new(ContainersDelete, "Delete Containers", "Delete container records", "Containers"),
                new(ContainersApprove, "Approve Containers", "Approve container validations", "Containers"),
                new(ContainersReject, "Reject Containers", "Reject container validations", "Containers"),
                new(ContainersExport, "Export Containers", "Export container data to CSV/Excel", "Containers"),
                new(ContainersBulkOperations, "Bulk Operations", "Perform bulk actions on containers", "Containers"),
                new(ContainersAnnotate, "Annotate Containers", "Add notes and annotations", "Containers"),
                new(ContainersValidate, "Validate Containers", "Validate container data", "Containers"),
                new(ContainersDetailsView, "View Container Details", "View detailed container information", "Containers"),
                new(ContainersCompletenessView, "View Container Completeness", "View container completeness status", "Containers"),
                new(ContainersCompletenessManage, "Manage Container Completeness", "Manage container completeness records", "Containers"),
                
                // ICUMS
                new(IcumsView, "View ICUMS Data", "View ICUMS/BOE information", "ICUMS"),
                new(IcumsDownload, "Download ICUMS Data", "Trigger manual ICUMS downloads", "ICUMS"),
                new(IcumsRefresh, "Refresh ICUMS Data", "Refresh ICUMS data from source", "ICUMS"),
                new(IcumsManualIngestion, "Manual Ingestion", "Trigger manual JSON file processing", "ICUMS"),
                new(IcumsQueueManagement, "Manage Download Queue", "Manage ICUMS download queue", "ICUMS"),
                new(IcumsSync, "Sync ICUMS Data", "Synchronize ICUMS data", "ICUMS"),
                new(IcumsSubmit, "Submit to ICUMS", "Submit data to ICUMS", "ICUMS"),
                new(IcumsExport, "Export ICUMS Data", "Export ICUMS data", "ICUMS"),
                new(IcumsDownloadQueue, "Manage Download Queue", "Manage ICUMS download queue", "ICUMS"),
                new(IcumsSubmissionQueue, "Manage Submission Queue", "Manage ICUMS submission queue", "ICUMS"),
                new(IcumsManualRequest, "Manual BOE Request", "Make manual BOE requests", "ICUMS"),
                
                // Images
                new(ImagesView, "View Images", "View container images", "Images"),
                new(ImagesDownload, "Download Images", "Download container images", "Images"),
                new(ImagesAnnotate, "Annotate Images", "Add annotations to images", "Images"),
                new(ImagesEdit, "Edit Images", "Adjust image properties", "Images"),
                new(ImagesFullscreen, "Fullscreen Viewer", "Use fullscreen image viewer", "Images"),
                new(ImagesTools, "Image Tools", "Use zoom, pan, rotate, brightness tools", "Images"),
                new(ImagesDelete, "Delete Images", "Delete container images", "Images"),
                new(ImagesExport, "Export Images", "Export images", "Images"),
                new(ImagesUpload, "Upload Images", "Upload new images", "Images"),
                new(ImagesAnalysisView, "View Image Analysis", "View image analysis results", "Images"),
                new(ImagesAnalysisTrigger, "Trigger Image Analysis", "Trigger image analysis workflow", "Images"),
                
                // Scanners
                new(ScannersView, "View Scanners", "View scanner information", "Scanners"),
                new(ScannersStatus, "Scanner Status", "View scanner health and status", "Scanners"),
                new(ScannersConfigure, "Configure Scanners", "Modify scanner settings", "Scanners"),
                new(ScannersManage, "Manage Scanners", "Add/remove/configure scanners", "Scanners"),
                new(ScannersDiagnostics, "Scanner Diagnostics", "View scanner diagnostics", "Scanners"),
                new(ScannersTriggerScan, "Trigger Scans", "Manually trigger scanner operations", "Scanners"),
                new(ScannersAseView, "View ASE Scanner", "View ASE scanner information", "Scanners"),
                new(ScannersAseSync, "Sync ASE Data", "Synchronize ASE scanner data", "Scanners"),
                new(ScannersFs6000View, "View FS6000 Scanner", "View FS6000 scanner information", "Scanners"),
                new(ScannersFs6000Sync, "Sync FS6000 Data", "Synchronize FS6000 scanner data", "Scanners"),
                new(ScannersIngestionMonitor, "Monitor Scanner Ingestion", "Monitor scanner data ingestion", "Scanners"),
                new(ScannersIngestionTrigger, "Trigger Scanner Ingestion", "Trigger scanner data ingestion", "Scanners"),
                
                // Vehicles
                new(VehiclesView, "View Vehicles", "View vehicle import records", "Vehicles"),
                new(VehiclesSearch, "Search Vehicles", "Search vehicle imports", "Vehicles"),
                new(VehiclesCreate, "Create Vehicle Records", "Create new vehicle imports", "Vehicles"),
                new(VehiclesEdit, "Edit Vehicle Records", "Modify vehicle information", "Vehicles"),
                new(VehiclesDelete, "Delete Vehicle Records", "Delete vehicle imports", "Vehicles"),
                new(VehiclesApprove, "Approve Vehicles", "Approve vehicle imports", "Vehicles"),
                new(VehiclesReject, "Reject Vehicles", "Reject vehicle imports", "Vehicles"),
                new(VehiclesExport, "Export Vehicles", "Export vehicle data", "Vehicles"),
                new(VehiclesDetailsView, "View Vehicle Details", "View detailed vehicle information", "Vehicles"),
                
                // Users
                new(UsersView, "View Users", "View user list and details", "Users"),
                new(UsersCreate, "Create Users", "Create new user accounts", "Users"),
                new(UsersEdit, "Edit Users", "Modify user information", "Users"),
                new(UsersDelete, "Delete Users", "Delete user accounts", "Users"),
                new(UsersDeactivate, "Deactivate Users", "Activate/deactivate users", "Users"),
                new(UsersManageRoles, "Manage User Roles", "Assign roles to users", "Users"),
                new(UsersResetPassword, "Reset Passwords", "Reset user passwords", "Users"),
                new(UsersViewAudit, "View User Audit", "View user activity logs", "Users"),
                new(UsersActivate, "Activate Users", "Activate/deactivate user accounts", "Users"),
                new(UsersActivityView, "View User Activity", "View user activity logs", "Users"),
                
                // Roles & Permissions
                new(RolesView, "View Roles", "View role definitions", "Roles"),
                new(RolesCreate, "Create Roles", "Create new custom roles", "Roles"),
                new(RolesEdit, "Edit Roles", "Modify role definitions", "Roles"),
                new(RolesDelete, "Delete Roles", "Delete custom roles", "Roles"),
                new(RolesManagePermissions, "Manage Role Permissions", "Assign permissions to roles", "Roles"),
                new(RolesAssign, "Assign Roles", "Assign roles to users", "Roles"),
                new(PermissionsView, "View Permissions", "View available permissions", "Permissions"),
                new(PermissionsManage, "Manage Permissions", "Create/edit permissions", "Permissions"),
                new(PermissionsAssign, "Assign Permissions", "Assign permissions to users/roles", "Permissions"),
                new(PermissionsCreate, "Create Permissions", "Create new permissions", "Permissions"),
                new(PermissionsEdit, "Edit Permissions", "Modify permissions", "Permissions"),
                new(PermissionsDelete, "Delete Permissions", "Delete permissions", "Permissions"),
                
                // System - Logs
                new(SystemLogsView, "View System Logs", "View application logs", "System"),
                new(SystemLogsDownload, "Download Logs", "Download log files", "System"),
                new(SystemLogsClear, "Clear Logs", "Delete log files", "System"),
                new(SystemLogsFilter, "Filter Logs", "Advanced log filtering", "System"),
                
                // System - Services
                new(SystemServicesView, "View Services", "View background service status", "System"),
                new(SystemServicesStart, "Start Services", "Start background services", "System"),
                new(SystemServicesStop, "Stop Services", "Stop background services", "System"),
                new(SystemServicesRestart, "Restart Services", "Restart background services", "System"),
                
                // System - Database
                new(SystemDatabaseView, "View Database", "View database connections", "System"),
                new(SystemDatabaseBrowse, "Browse Database", "Browse database tables", "System"),
                new(SystemDatabaseQuery, "Query Database", "Execute SQL queries", "System"),
                new(SystemDatabaseBackup, "Backup Database", "Create database backups", "System"),
                new(DatabaseBackup, "Backup Database", "Create database backups", "Database"),
                new(DatabaseRestore, "Restore Database", "Restore from database backups", "Database"),
                new(DatabaseMaintenance, "Database Maintenance", "Run database maintenance tasks", "Database"),
                new(DatabaseQuery, "Execute Database Queries", "Run custom database queries", "Database"),
                new(DatabaseView, "View Database Info", "View database information", "Database"),
                
                // System - Configuration
                new(SystemConfigView, "View Configuration", "View system configuration", "System"),
                new(SystemConfigEdit, "Edit Configuration", "Modify system settings", "System"),
                new(SystemSettingsView, "View System Settings", "View system configuration", "System"),
                new(SystemSettingsEdit, "Edit System Settings", "Edit system configuration", "System"),
                new(SystemHealthView, "View System Health", "View system health status", "System"),
                new(SystemMonitoringView, "View System Monitoring", "View system monitoring dashboard", "System"),
                new(SystemLogsExport, "Export System Logs", "Export system logs", "System"),
                new(SystemServicesControl, "Control Background Services", "Start/stop/restart services", "System"),
                
                // System - Performance
                new(SystemPerformanceView, "View Performance", "View performance metrics", "System"),
                new(SystemPerformanceMonitor, "Monitor Performance", "Real-time performance monitoring", "System"),
                new(PerformanceMetricsView, "View Performance Metrics", "View detailed performance metrics", "Performance"),
                
                // System - Files
                new(SystemFilesBrowse, "Browse Files", "Browse ICUMS download files", "System"),
                new(SystemFilesDownload, "Download Files", "Download system files", "System"),
                new(SystemFilesDelete, "Delete Files", "Delete system files", "System"),
                
                // Reports
                new(ReportsView, "View Reports", "View generated reports", "Reports"),
                new(ReportsGenerate, "Generate Reports", "Create new reports", "Reports"),
                new(ReportsExport, "Export Reports", "Export reports to various formats", "Reports"),
                new(ReportsSchedule, "Schedule Reports", "Schedule automated reports", "Reports"),
                
                // Audit
                new(AuditView, "View Audit Logs", "View system audit logs", "Audit"),
                new(AuditExport, "Export Audit Logs", "Export audit data", "Audit"),
                new(AuditSearch, "Search Audit Logs", "Advanced audit log search", "Audit"),
                
                // Analytics
                new(AnalyticsView, "View Analytics", "View analytics dashboard", "Analytics"),
                new(AnalyticsAdvanced, "Advanced Analytics", "Advanced analytics features", "Analytics"),
                
                // API & Diagnostics
                new(ApiTest, "Test API Endpoints", "Access API testing tools", "API"),
                new(ApiDebug, "Debug API", "Access debugging tools and endpoints", "API"),
                new(DiagnosticsView, "View Diagnostics", "Access system diagnostics", "Diagnostics"),
                new(DiagnosticsRun, "Run Diagnostics", "Execute diagnostic tests", "Diagnostics"),
                
                // Page Access Permissions
                // Dashboard
                new(PagesDashboardView, "View Dashboard Page", "Access to main dashboard page", "Pages"),
                new(PagesDashboardAnalytics, "View Analytics Page", "Access to analytics dashboard page", "Pages"),
                
                // Operations
                new(PagesContainersView, "View Containers Page", "Access to containers list page", "Pages"),
                new(PagesContainersDetails, "View Container Details Page", "Access to container details page", "Pages"),
                new(PagesContainerProcessing, "View Container Processing Page", "Access to container processing page", "Pages"),
                new(PagesContainerCompleteness, "View Container Completeness Page", "Access to container completeness page", "Pages"),
                new(PagesImageAnalysisView, "View Image Analysis Page", "Access to image analysis page", "Pages"),
                new(PagesImageAnalysisAudit, "View Audit Review Page", "Access to audit review page", "Pages"),
                new(PagesCmrValidation, "View CMR Validation Page", "Access to CMR validation page", "Pages"),
                new(PagesCompletedRecords, "View Completed Records Page", "Access to completed records page", "Pages"),
                new(PagesCrossRecordScans, "View Cross-Record Scans Page", "Access to cross-record scans page", "Pages"),
                new(PagesVehiclesView, "View Vehicles Page", "Access to vehicles page", "Pages"),
                
                // Validation
                new(PagesImageAnalysisManagement, "View Image Analysis Management Page", "Access to image analysis management page", "Pages"),
                new(PagesValidationRules, "View Business Rules Page", "Access to business rules page", "Pages"),
                new(PagesValidationCompleteness, "View Validation Completeness Page", "Access to validation completeness page", "Pages"),
                new(PagesValidationMatchCorrections, "View Match Corrections Page", "Access to admin Match Corrections page (unmatch / rematch wrong image-to-BOE matches)", "Pages"),
                new(PagesValidationBoeLookup, "View BOE Lookup Page", "Universal cargo / BOE search by container, declaration, BL, master BL, house BL, rotation, or VIN. For analysts and above.", "Pages"),
                new(PagesValidationRecordCompleteness, "View Record Completeness Page", "Record-level completeness view (1.15.0) — see declarations with expected container sets, integrity gaps, partially-ready records. For analysts and above.", "Pages"),
                new(PagesValidationXrayInspector, "View X-Ray Inspector Page", "Full-suite X-ray analysis workbench. Raw 16-bit pixel access for both ASE and FS6000 scans, measurement tools, dual-energy composite, analysis operations. For analysts, AI labelers, and investigators.", "Pages"),
                new(XrayInspectorAnalyze, "Run X-Ray Analysis Operations", "Permission to invoke server-side analysis (ROI stats, edge detection, thresholding, object detection, dual-energy diff) in the X-Ray Inspector.", "Images"),
                new(XrayInspectorExport, "Export X-Ray Analysis", "Permission to export raw 16-bit PNG, CSV stats, and PDF reports from the X-Ray Inspector.", "Images"),
                
                // Scanners
                new(PagesScannersView, "View Scanner Overview Page", "Access to scanner overview page", "Pages"),
                new(PagesScannersAse, "View ASE Scanner Page", "Access to ASE scanner page", "Pages"),
                new(PagesScannersFs6000, "View FS6000 Scanner Page", "Access to FS6000 scanner page", "Pages"),
                new(PagesScannersHeimann, "View Heimann Scanner Page", "Access to Heimann scanner page", "Pages"),
                
                // ICUMS
                new(PagesIcumsView, "View ICUMS Dashboard Page", "Access to ICUMS dashboard page", "Pages"),
                new(PagesIcumsDownloadQueue, "View Download Queue Page", "Access to ICUMS download queue page", "Pages"),
                new(PagesIcumsSubmissionQueue, "View Submission Queue Page", "Access to ICUMS submission queue page", "Pages"),
                new(PagesIcumsBoeRequest, "View BOE Request Page", "Access to BOE request page", "Pages"),
                new(PagesIcumsLooseCargo, "View Loose Cargo Page", "Access to loose cargo page", "Pages"),
                new(PagesIcumsAnalytics, "View ICUMS Analytics Page", "Access to ICUMS analytics page", "Pages"),
                new(PagesIcumsBatchDownload, "View Batch Download Management Page", "Access to ICUMS batch download management page with service control, monitoring, and data viewing", "Pages"),
                new(PagesIcumsPayloads, "View ICUMS Payload Viewer Page", "Access to view generated ICUMS submission payloads and acknowledged submissions", "Pages"),
                new(PagesIcumsVerifyStatus, "Verify ICUMS Submission Status", "Ability to query ICUMS readStatus API to verify container submission receipt", "Pages"),
                
                // Administration
                new(PagesAdminUsers, "View Users Management Page", "Access to users management page", "Pages"),
                new(PagesAdminRoles, "View Roles Management Page", "Access to roles management page", "Pages"),
                new(PagesAdminPermissions, "View Permissions Management Page", "Access to permissions management page", "Pages"),
                new(PagesAdminSettings, "View System Settings Page", "Access to system settings page", "Pages"),
                new(PagesAdminLogs, "View System Logs Page", "Access to system logs page", "Pages"),
                new(PagesAdminDatabase, "View Database Admin Page", "Access to database admin page", "Pages"),
                new(PagesAdminAudit, "View Audit Log Page", "Access to audit log page", "Pages"),
                new(PagesAdminServiceControl, "View Service Control Panel Page", "Access to service control panel page", "Pages"),
                
                // Reports
                new(PagesReportsView, "View Reports Dashboard Page", "Access to reports dashboard page", "Pages"),
                new(PagesReportsTemplates, "View Reports Templates Page", "Access to reports templates page", "Pages"),
                
                // Operations - Error Monitoring
                new(PagesOperationsErrors, "View Error Monitor Page", "Access to the error monitoring dashboard for viewing Warning/Error/Fatal logs in real time", "Pages"),

                // Other
                new(PagesSearch, "Use Search Page", "Access to search page", "Pages"),
                new(PagesNotifications, "View Notifications Page", "Access to notifications page", "Pages"),
                new(PagesPerformance, "View Performance Page", "Access to performance metrics page", "Pages"),
                
                // Services
                new(PagesServicesMonitoring, "View System Monitoring Page", "Access to system monitoring and health dashboard", "Pages"),
                new(PagesServicesPerformanceMetrics, "View Performance Metrics Page", "Access to API performance metrics dashboard", "Pages"),
                new(PagesServicesDatabase, "View Database Admin Page", "Access to database administration and query tools", "Pages"),
                new(PagesServicesDiagnostics, "View Diagnostics Page", "Access to system diagnostics and troubleshooting tools", "Pages"),
                new(PagesServicesGateway, "View Gateway Page", "Access to unified gateway and global search", "Pages"),
                new(PagesServicesIngestion, "View Ingestion Management Page", "Access to file ingestion management and monitoring", "Pages"),
                new(PagesServicesImageProcessing, "View Image Processing Page", "Access to image processing operations and management", "Pages"),
                new(PagesServicesAseSync, "View ASE Sync Page", "Access to ASE scanner synchronization management", "Pages"),
                new(PagesServicesFs6000Completeness, "View FS6000 Completeness Page", "Access to FS6000 image completeness tracking", "Pages"),
                new(PagesServicesConsolidatedCargo, "View Consolidated Cargo Page", "Access to consolidated cargo management", "Pages"),
                new(PagesServicesAccessReview, "View Access Review Page", "Access to access review and audit workflow", "Pages"),
                new(PagesServicesDebug, "View Debug Tools Page", "Access to debugging and development tools (development only)", "Pages"),
                
                // Controller Access Permissions
                // Image Analysis
                new(ControllersImageAnalysisAssign, "Assign Image Analysis Groups", "Access to assign groups endpoint", "Controllers"),
                new(ControllersImageAnalysisClaim, "Claim Image Analysis Groups", "Access to claim groups endpoint", "Controllers"),
                new(ControllersImageAnalysisDecisionAnalyst, "Save Analyst Decisions", "Access to save analyst decisions endpoint", "Controllers"),
                new(ControllersImageAnalysisDecisionAudit, "Save Audit Decisions", "Access to save audit decisions endpoint", "Controllers"),
                new(ControllersImageAnalysisLeaseRenew, "Renew Image Analysis Lease", "Access to renew lease endpoint", "Controllers"),
                new(ControllersImageAnalysisManagementView, "View Image Analysis Management", "Access to view management data endpoint", "Controllers"),
                new(ControllersImageAnalysisManagementSettings, "Update Image Analysis Settings", "Access to update settings endpoint", "Controllers"),
                new(ControllersImageAnalysisIntake, "Intake Complete Records", "Access to intake complete records endpoint", "Controllers"),
                new(ControllersImageAnalysisSubmit, "Submit to ICUMS", "Access to submit to ICUMS endpoint", "Controllers"),
                new(ControllersImageAnalysisUsers, "View Image Analysis Users", "Access to view users endpoint", "Controllers"),
                new(ControllersImageAnalysisMyAssignments, "View My Assignments", "Access to view my assignments endpoint", "Controllers"),
                new(ControllersImageAnalysisAvailable, "View Available Groups", "Access to view available groups endpoint", "Controllers"),
                
                // Containers
                new(ControllersContainersView, "View Containers API", "Access to view containers endpoint", "Controllers"),
                new(ControllersContainersCreate, "Create Containers API", "Access to create containers endpoint", "Controllers"),
                new(ControllersContainersEdit, "Edit Containers API", "Access to edit containers endpoint", "Controllers"),
                new(ControllersContainersDelete, "Delete Containers API", "Access to delete containers endpoint", "Controllers"),
                new(ControllersContainersApprove, "Approve Containers API", "Access to approve containers endpoint", "Controllers"),
                new(ControllersContainersReject, "Reject Containers API", "Access to reject containers endpoint", "Controllers"),
                new(ControllersContainersDetails, "View Container Details API", "Access to container details endpoint", "Controllers"),
                
                // Completeness
                new(ControllersCompletenessView, "View Completeness API", "Access to view completeness endpoint", "Controllers"),
                new(ControllersCompletenessManage, "Manage Completeness API", "Access to manage completeness endpoint", "Controllers"),
                
                // ICUMS
                new(ControllersIcumsView, "View ICUMS API", "Access to view ICUMS endpoint", "Controllers"),
                new(ControllersIcumsDownload, "Download from ICUMS API", "Access to download from ICUMS endpoint", "Controllers"),
                new(ControllersIcumsSubmit, "Submit to ICUMS API", "Access to submit to ICUMS endpoint", "Controllers"),
                new(ControllersIcumsQueueDownload, "Manage Download Queue API", "Access to manage download queue endpoint", "Controllers"),
                new(ControllersIcumsQueueSubmission, "Manage Submission Queue API", "Access to manage submission queue endpoint", "Controllers"),
                new(ControllersIcumsManual, "Manual ICUMS Operations API", "Access to manual ICUMS operations endpoint", "Controllers"),
                
                // Users
                new(ControllersUsersView, "View Users API", "Access to view users endpoint", "Controllers"),
                new(ControllersUsersCreate, "Create Users API", "Access to create users endpoint", "Controllers"),
                new(ControllersUsersEdit, "Edit Users API", "Access to edit users endpoint", "Controllers"),
                new(ControllersUsersDelete, "Delete Users API", "Access to delete users endpoint", "Controllers"),
                new(ControllersUsersPassword, "Reset Passwords API", "Access to reset passwords endpoint", "Controllers"),
                new(ControllersUsersRoles, "Manage User Roles API", "Access to manage user roles endpoint", "Controllers"),
                
                // Roles
                new(ControllersRolesView, "View Roles API", "Access to view roles endpoint", "Controllers"),
                new(ControllersRolesCreate, "Create Roles API", "Access to create roles endpoint", "Controllers"),
                new(ControllersRolesEdit, "Edit Roles API", "Access to edit roles endpoint", "Controllers"),
                new(ControllersRolesDelete, "Delete Roles API", "Access to delete roles endpoint", "Controllers"),
                new(ControllersRolesPermissions, "Manage Role Permissions API", "Access to manage role permissions endpoint", "Controllers"),
                
                // Permissions
                new(ControllersPermissionsView, "View Permissions API", "Access to view permissions endpoint", "Controllers"),
                new(ControllersPermissionsManage, "Manage Permissions API", "Access to manage permissions endpoint", "Controllers"),
                new(ControllersPermissionsMy, "View My Permissions API", "Access to view my permissions endpoint", "Controllers"),
                
                // System
                new(ControllersSystemSettings, "System Settings API", "Access to system settings endpoint", "Controllers"),
                new(ControllersSystemLogs, "System Logs API", "Access to system logs endpoint", "Controllers"),
                new(ControllersSystemServices, "System Services API", "Access to system services endpoint", "Controllers"),
                new(ControllersSystemDatabase, "Database Admin API", "Access to database admin endpoint", "Controllers"),
                new(ControllersSystemAudit, "System Audit API", "Access to system audit endpoint", "Controllers"),
                new(ControllersAiWorkflow, "AI Workflow Assist API", "Access assistive AI, lineage, ops triage, and training export endpoints", "Controllers"),
            };
        }
    }

    /// <summary>
    /// Permission definition for initialization
    /// </summary>
    public record PermissionDefinition(
        string Name,
        string DisplayName,
        string Description,
        string Category
    );
}

