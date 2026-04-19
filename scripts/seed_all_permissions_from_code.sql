-- ============================================
-- Seed All Permissions from Permissions.cs
-- This script ensures all permissions defined in Permissions.GetAllPermissions()
-- are added to the database
-- ============================================

USE NS_CIS;
GO

PRINT '========================================';
PRINT 'Seeding all permissions from Permissions.cs';
PRINT '========================================';
PRINT '';

DECLARE @BeforeCount INT;
SELECT @BeforeCount = COUNT(*) FROM Permissions WHERE IsActive = 1;
PRINT CONCAT('📊 Permissions before: ', @BeforeCount);
PRINT '';

-- This will be handled by PermissionSeeder, but we'll add any that are missing
-- Using MERGE to ensure all permissions from GetAllPermissions() exist

-- Dashboard
MERGE Permissions AS target
USING (SELECT 'dashboard.view' AS Name, 'View Dashboard' AS DisplayName, 'Access to the main dashboard' AS Description, 'Dashboard' AS Category) AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

MERGE Permissions AS target
USING (SELECT 'dashboard.refresh' AS Name, 'Refresh Dashboard' AS DisplayName, 'Ability to refresh dashboard data' AS Description, 'Dashboard' AS Category) AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

MERGE Permissions AS target
USING (SELECT 'dashboard.export' AS Name, 'Export Dashboard' AS DisplayName, 'Export dashboard data and reports' AS Description, 'Dashboard' AS Category) AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

MERGE Permissions AS target
USING (SELECT 'dashboard.comprehensive' AS Name, 'View Comprehensive Dashboard' AS DisplayName, 'Access comprehensive dashboard with all metrics' AS Description, 'Dashboard' AS Category) AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Containers (all 14)
DECLARE @ContainerPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @ContainerPerms VALUES
('containers.view', 'View Containers', 'View container details and lists', 'Containers'),
('containers.search', 'Search Containers', 'Search and filter containers', 'Containers'),
('containers.create', 'Create Containers', 'Create new container records', 'Containers'),
('containers.edit', 'Edit Containers', 'Modify container information', 'Containers'),
('containers.delete', 'Delete Containers', 'Delete container records', 'Containers'),
('containers.approve', 'Approve Containers', 'Approve container validations', 'Containers'),
('containers.reject', 'Reject Containers', 'Reject container validations', 'Containers'),
('containers.export', 'Export Containers', 'Export container data to CSV/Excel', 'Containers'),
('containers.bulk', 'Bulk Operations', 'Perform bulk actions on containers', 'Containers'),
('containers.annotate', 'Annotate Containers', 'Add notes and annotations', 'Containers'),
('containers.validate', 'Validate Containers', 'Validate container data', 'Containers'),
('containers.details.view', 'View Container Details', 'View detailed container information', 'Containers'),
('containers.completeness.view', 'View Container Completeness', 'View container completeness status', 'Containers'),
('containers.completeness.manage', 'Manage Container Completeness', 'Manage container completeness records', 'Containers');

MERGE Permissions AS target
USING @ContainerPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- ICUMS (all 11)
DECLARE @IcumsPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @IcumsPerms VALUES
('icums.view', 'View ICUMS Data', 'View ICUMS/BOE information', 'ICUMS'),
('icums.download', 'Download ICUMS Data', 'Trigger manual ICUMS downloads', 'ICUMS'),
('icums.refresh', 'Refresh ICUMS Data', 'Refresh ICUMS data from source', 'ICUMS'),
('icums.ingest', 'Manual Ingestion', 'Trigger manual JSON file processing', 'ICUMS'),
('icums.queue', 'Manage Download Queue', 'Manage ICUMS download queue', 'ICUMS'),
('icums.sync', 'Sync ICUMS Data', 'Synchronize ICUMS data', 'ICUMS'),
('icums.submit', 'Submit to ICUMS', 'Submit data to ICUMS', 'ICUMS'),
('icums.export', 'Export ICUMS Data', 'Export ICUMS data', 'ICUMS'),
('icums.download.queue', 'Manage Download Queue', 'Manage ICUMS download queue', 'ICUMS'),
('icums.submission.queue', 'Manage Submission Queue', 'Manage ICUMS submission queue', 'ICUMS'),
('icums.manual.request', 'Manual BOE Request', 'Make manual BOE requests', 'ICUMS');

MERGE Permissions AS target
USING @IcumsPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Images (all 11)
DECLARE @ImagePerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @ImagePerms VALUES
('images.view', 'View Images', 'View container images', 'Images'),
('images.download', 'Download Images', 'Download container images', 'Images'),
('images.annotate', 'Annotate Images', 'Add annotations to images', 'Images'),
('images.edit', 'Edit Images', 'Adjust image properties', 'Images'),
('images.fullscreen', 'Fullscreen Viewer', 'Use fullscreen image viewer', 'Images'),
('images.tools', 'Image Tools', 'Use zoom, pan, rotate, brightness tools', 'Images'),
('images.delete', 'Delete Images', 'Delete container images', 'Images'),
('images.export', 'Export Images', 'Export images', 'Images'),
('images.upload', 'Upload Images', 'Upload new images', 'Images'),
('images.analysis.view', 'View Image Analysis', 'View image analysis results', 'Images'),
('images.analysis.trigger', 'Trigger Image Analysis', 'Trigger image analysis workflow', 'Images');

MERGE Permissions AS target
USING @ImagePerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Scanners (all 12)
DECLARE @ScannerPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @ScannerPerms VALUES
('scanners.view', 'View Scanners', 'View scanner information', 'Scanners'),
('scanners.status', 'Scanner Status', 'View scanner health and status', 'Scanners'),
('scanners.configure', 'Configure Scanners', 'Modify scanner settings', 'Scanners'),
('scanners.manage', 'Manage Scanners', 'Add/remove/configure scanners', 'Scanners'),
('scanners.diagnostics', 'Scanner Diagnostics', 'View scanner diagnostics', 'Scanners'),
('scanners.trigger', 'Trigger Scans', 'Manually trigger scanner operations', 'Scanners'),
('scanners.ase.view', 'View ASE Scanner', 'View ASE scanner information', 'Scanners'),
('scanners.ase.sync', 'Sync ASE Data', 'Synchronize ASE scanner data', 'Scanners'),
('scanners.fs6000.view', 'View FS6000 Scanner', 'View FS6000 scanner information', 'Scanners'),
('scanners.fs6000.sync', 'Sync FS6000 Data', 'Synchronize FS6000 scanner data', 'Scanners'),
('scanners.ingestion.monitor', 'Monitor Scanner Ingestion', 'Monitor scanner data ingestion', 'Scanners'),
('scanners.ingestion.trigger', 'Trigger Scanner Ingestion', 'Trigger scanner data ingestion', 'Scanners');

MERGE Permissions AS target
USING @ScannerPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Vehicles (all 9)
DECLARE @VehiclePerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @VehiclePerms VALUES
('vehicles.view', 'View Vehicles', 'View vehicle import records', 'Vehicles'),
('vehicles.search', 'Search Vehicles', 'Search vehicle imports', 'Vehicles'),
('vehicles.create', 'Create Vehicle Records', 'Create new vehicle imports', 'Vehicles'),
('vehicles.edit', 'Edit Vehicle Records', 'Modify vehicle information', 'Vehicles'),
('vehicles.delete', 'Delete Vehicle Records', 'Delete vehicle imports', 'Vehicles'),
('vehicles.approve', 'Approve Vehicles', 'Approve vehicle imports', 'Vehicles'),
('vehicles.reject', 'Reject Vehicles', 'Reject vehicle imports', 'Vehicles'),
('vehicles.export', 'Export Vehicles', 'Export vehicle data', 'Vehicles'),
('vehicles.details.view', 'View Vehicle Details', 'View detailed vehicle information', 'Vehicles');

MERGE Permissions AS target
USING @VehiclePerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Users (all 10)
DECLARE @UserPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @UserPerms VALUES
('users.view', 'View Users', 'View user list and details', 'User Management'),
('users.create', 'Create Users', 'Create new user accounts', 'User Management'),
('users.edit', 'Edit Users', 'Modify user information', 'User Management'),
('users.delete', 'Delete Users', 'Delete user accounts', 'User Management'),
('users.deactivate', 'Deactivate Users', 'Activate/deactivate users', 'User Management'),
('users.roles', 'Manage User Roles', 'Assign roles to users', 'User Management'),
('users.password', 'Reset Passwords', 'Reset user passwords', 'User Management'),
('users.audit', 'View User Audit', 'View user activity logs', 'User Management'),
('users.activate', 'Activate Users', 'Activate/deactivate user accounts', 'User Management'),
('users.activity.view', 'View User Activity', 'View user activity logs', 'User Management');

MERGE Permissions AS target
USING @UserPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Roles & Permissions (all 12)
DECLARE @RolePerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @RolePerms VALUES
('roles.view', 'View Roles', 'View role definitions', 'Role Management'),
('roles.create', 'Create Roles', 'Create new custom roles', 'Role Management'),
('roles.edit', 'Edit Roles', 'Modify role definitions', 'Role Management'),
('roles.delete', 'Delete Roles', 'Delete custom roles', 'Role Management'),
('roles.permissions', 'Manage Role Permissions', 'Assign permissions to roles', 'Role Management'),
('roles.assign', 'Assign Roles', 'Assign roles to users', 'Role Management'),
('permissions.view', 'View Permissions', 'View available permissions', 'Permission Management'),
('permissions.manage', 'Manage Permissions', 'Create/edit permissions', 'Permission Management'),
('permissions.assign', 'Assign Permissions', 'Assign permissions to users/roles', 'Permission Management'),
('permissions.create', 'Create Permissions', 'Create new permissions', 'Permission Management'),
('permissions.edit', 'Edit Permissions', 'Modify permissions', 'Permission Management'),
('permissions.delete', 'Delete Permissions', 'Delete permissions', 'Permission Management');

MERGE Permissions AS target
USING @RolePerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- System permissions (all)
DECLARE @SystemPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @SystemPerms VALUES
-- Logs
('system.logs.view', 'View System Logs', 'View application logs', 'System'),
('system.logs.download', 'Download Logs', 'Download log files', 'System'),
('system.logs.clear', 'Clear Logs', 'Delete log files', 'System'),
('system.logs.filter', 'Filter Logs', 'Advanced log filtering', 'System'),
('system.logs.export', 'Export System Logs', 'Export system logs', 'System'),
-- Services
('system.services.view', 'View Services', 'View background service status', 'System'),
('system.services.start', 'Start Services', 'Start background services', 'System'),
('system.services.stop', 'Stop Services', 'Stop background services', 'System'),
('system.services.restart', 'Restart Services', 'Restart background services', 'System'),
('system.services.control', 'Control Background Services', 'Start/stop/restart services', 'System'),
-- Database
('system.database.view', 'View Database', 'View database connections', 'System'),
('system.database.browse', 'Browse Database', 'Browse database tables', 'System'),
('system.database.query', 'Query Database', 'Execute SQL queries', 'System'),
('system.database.backup', 'Backup Database', 'Create database backups', 'System'),
-- Config
('system.config.view', 'View Configuration', 'View system configuration', 'System'),
('system.config.edit', 'Edit Configuration', 'Modify system settings', 'System'),
('system.settings.view', 'View System Settings', 'View system configuration', 'System'),
('system.settings.edit', 'Edit System Settings', 'Edit system configuration', 'System'),
-- Health/Monitoring
('system.health.view', 'View System Health', 'View system health status', 'System'),
('system.monitoring.view', 'View System Monitoring', 'View system monitoring dashboard', 'System'),
-- Performance
('system.performance.view', 'View Performance', 'View performance metrics', 'System'),
('system.performance.monitor', 'Monitor Performance', 'Real-time performance monitoring', 'System'),
-- Files
('system.files.browse', 'Browse Files', 'Browse ICUMS download files', 'System'),
('system.files.download', 'Download Files', 'Download system files', 'System'),
('system.files.delete', 'Delete Files', 'Delete system files', 'System');

MERGE Permissions AS target
USING @SystemPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Database permissions (separate category)
DECLARE @DatabasePerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @DatabasePerms VALUES
('database.backup', 'Backup Database', 'Create database backups', 'Database'),
('database.restore', 'Restore Database', 'Restore from database backups', 'Database'),
('database.maintenance', 'Database Maintenance', 'Run database maintenance tasks', 'Database'),
('database.query', 'Execute Database Queries', 'Run custom database queries', 'Database'),
('database.view', 'View Database Info', 'View database information', 'Database');

MERGE Permissions AS target
USING @DatabasePerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Reports (all 4)
DECLARE @ReportPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @ReportPerms VALUES
('reports.view', 'View Reports', 'View generated reports', 'Reports'),
('reports.generate', 'Generate Reports', 'Create new reports', 'Reports'),
('reports.export', 'Export Reports', 'Export reports to various formats', 'Reports'),
('reports.schedule', 'Schedule Reports', 'Schedule automated reports', 'Reports');

MERGE Permissions AS target
USING @ReportPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Audit (all 3)
DECLARE @AuditPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @AuditPerms VALUES
('audit.view', 'View Audit Logs', 'View system audit logs', 'Audit'),
('audit.export', 'Export Audit Logs', 'Export audit data', 'Audit'),
('audit.search', 'Search Audit Logs', 'Advanced audit log search', 'Audit');

MERGE Permissions AS target
USING @AuditPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Analytics (all 2)
DECLARE @AnalyticsPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @AnalyticsPerms VALUES
('analytics.view', 'View Analytics', 'View analytics dashboard', 'Analytics'),
('analytics.advanced', 'Advanced Analytics', 'Advanced analytics features', 'Analytics');

MERGE Permissions AS target
USING @AnalyticsPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- API & Diagnostics (all 4)
DECLARE @ApiPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @ApiPerms VALUES
('api.test', 'Test API Endpoints', 'Access API testing tools', 'API'),
('api.debug', 'Debug API', 'Access debugging tools and endpoints', 'API'),
('diagnostics.view', 'View Diagnostics', 'Access system diagnostics', 'Diagnostics'),
('diagnostics.run', 'Run Diagnostics', 'Execute diagnostic tests', 'Diagnostics');

MERGE Permissions AS target
USING @ApiPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Performance (1)
DECLARE @PerfPerms TABLE (Name VARCHAR(100), DisplayName VARCHAR(200), Description VARCHAR(500), Category VARCHAR(50));
INSERT INTO @PerfPerms VALUES
('performance.metrics.view', 'View Performance Metrics', 'View detailed performance metrics', 'Performance');

MERGE Permissions AS target
USING @PerfPerms AS source
ON target.Name = source.Name AND target.IsActive = 1
WHEN NOT MATCHED THEN INSERT (Name, DisplayName, Description, Category, IsActive, CreatedAt) 
    VALUES (source.Name, source.DisplayName, source.Description, source.Category, 1, SYSUTCDATETIME());

-- Verify final count
DECLARE @AfterCount INT;
SELECT @AfterCount = COUNT(*) FROM Permissions WHERE IsActive = 1;
PRINT '';
PRINT CONCAT('📊 Permissions after: ', @AfterCount);
PRINT CONCAT('✅ Added ', @AfterCount - @BeforeCount, ' new permissions');
PRINT '';
PRINT '✅ Migration complete!';
GO

