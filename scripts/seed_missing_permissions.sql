-- ============================================
-- Seed Missing Permissions to Database
-- Migration: Add New Permissions from Permissions.cs
-- ============================================

USE NS_CIS;
GO

PRINT '========================================';
PRINT 'Seeding missing permissions to database';
PRINT '========================================';
PRINT '';

-- Get current permission count
DECLARE @CurrentCount INT;
SELECT @CurrentCount = COUNT(*) FROM Permissions WHERE IsActive = 1;
PRINT CONCAT('📊 Current permissions in database: ', @CurrentCount);
PRINT '';

-- List of all permissions that should exist (from Permissions.GetAllPermissions())
-- Note: Only adding permissions that don't already exist in the database

-- Dashboard permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'dashboard.comprehensive' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('dashboard.comprehensive', 'View Comprehensive Dashboard', 'Access comprehensive dashboard with all metrics', 'Dashboard', 1, SYSUTCDATETIME());
    PRINT '✅ Added: dashboard.comprehensive';
END

-- Container permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'containers.validate' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('containers.validate', 'Validate Containers', 'Validate container data', 'Containers', 1, SYSUTCDATETIME());
    PRINT '✅ Added: containers.validate';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'containers.details.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('containers.details.view', 'View Container Details', 'View detailed container information', 'Containers', 1, SYSUTCDATETIME());
    PRINT '✅ Added: containers.details.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'containers.completeness.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('containers.completeness.view', 'View Container Completeness', 'View container completeness status', 'Containers', 1, SYSUTCDATETIME());
    PRINT '✅ Added: containers.completeness.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'containers.completeness.manage' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('containers.completeness.manage', 'Manage Container Completeness', 'Manage container completeness records', 'Containers', 1, SYSUTCDATETIME());
    PRINT '✅ Added: containers.completeness.manage';
END

-- ICUMS permissions (checking if these are missing)
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'icums.sync' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('icums.sync', 'Sync ICUMS Data', 'Synchronize ICUMS data', 'ICUMS', 1, SYSUTCDATETIME());
    PRINT '✅ Added: icums.sync';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'icums.submit' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('icums.submit', 'Submit to ICUMS', 'Submit data to ICUMS', 'ICUMS', 1, SYSUTCDATETIME());
    PRINT '✅ Added: icums.submit';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'icums.export' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('icums.export', 'Export ICUMS Data', 'Export ICUMS data', 'ICUMS', 1, SYSUTCDATETIME());
    PRINT '✅ Added: icums.export';
END

-- Image permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'images.delete' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('images.delete', 'Delete Images', 'Delete container images', 'Images', 1, SYSUTCDATETIME());
    PRINT '✅ Added: images.delete';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'images.export' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('images.export', 'Export Images', 'Export images', 'Images', 1, SYSUTCDATETIME());
    PRINT '✅ Added: images.export';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'images.upload' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('images.upload', 'Upload Images', 'Upload new images', 'Images', 1, SYSUTCDATETIME());
    PRINT '✅ Added: images.upload';
END

-- Scanner permissions (checking if these are missing)
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.ase.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.ase.view', 'View ASE Scanner', 'View ASE scanner information', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.ase.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.ase.sync' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.ase.sync', 'Sync ASE Data', 'Synchronize ASE scanner data', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.ase.sync';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.fs6000.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.fs6000.view', 'View FS6000 Scanner', 'View FS6000 scanner information', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.fs6000.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.fs6000.sync' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.fs6000.sync', 'Sync FS6000 Data', 'Synchronize FS6000 scanner data', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.fs6000.sync';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.ingestion.monitor' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.ingestion.monitor', 'Monitor Scanner Ingestion', 'Monitor scanner data ingestion', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.ingestion.monitor';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'scanners.ingestion.trigger' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('scanners.ingestion.trigger', 'Trigger Scanner Ingestion', 'Trigger scanner data ingestion', 'Scanners', 1, SYSUTCDATETIME());
    PRINT '✅ Added: scanners.ingestion.trigger';
END

-- Vehicle permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'vehicles.details.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('vehicles.details.view', 'View Vehicle Details', 'View detailed vehicle information', 'Vehicles', 1, SYSUTCDATETIME());
    PRINT '✅ Added: vehicles.details.view';
END

-- User permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'users.activate' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('users.activate', 'Activate Users', 'Activate/deactivate user accounts', 'User Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: users.activate';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'users.activity.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('users.activity.view', 'View User Activity', 'View user activity logs', 'User Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: users.activity.view';
END

-- Role permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'roles.assign' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('roles.assign', 'Assign Roles', 'Assign roles to users', 'Role Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: roles.assign';
END

-- Permission permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'permissions.assign' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('permissions.assign', 'Assign Permissions', 'Assign permissions to users/roles', 'Permission Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: permissions.assign';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'permissions.create' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('permissions.create', 'Create Permissions', 'Create new permissions', 'Permission Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: permissions.create';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'permissions.edit' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('permissions.edit', 'Edit Permissions', 'Modify permissions', 'Permission Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: permissions.edit';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'permissions.delete' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('permissions.delete', 'Delete Permissions', 'Delete permissions', 'Permission Management', 1, SYSUTCDATETIME());
    PRINT '✅ Added: permissions.delete';
END

-- System permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'database.backup' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('database.backup', 'Backup Database', 'Create database backups', 'Database', 1, SYSUTCDATETIME());
    PRINT '✅ Added: database.backup';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'database.restore' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('database.restore', 'Restore Database', 'Restore from database backups', 'Database', 1, SYSUTCDATETIME());
    PRINT '✅ Added: database.restore';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'database.maintenance' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('database.maintenance', 'Database Maintenance', 'Run database maintenance tasks', 'Database', 1, SYSUTCDATETIME());
    PRINT '✅ Added: database.maintenance';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'database.query' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('database.query', 'Execute Database Queries', 'Run custom database queries', 'Database', 1, SYSUTCDATETIME());
    PRINT '✅ Added: database.query';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'database.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('database.view', 'View Database Info', 'View database information', 'Database', 1, SYSUTCDATETIME());
    PRINT '✅ Added: database.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.settings.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.settings.view', 'View System Settings', 'View system configuration', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.settings.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.settings.edit' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.settings.edit', 'Edit System Settings', 'Edit system configuration', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.settings.edit';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.health.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.health.view', 'View System Health', 'View system health status', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.health.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.monitoring.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.monitoring.view', 'View System Monitoring', 'View system monitoring dashboard', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.monitoring.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.logs.export' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.logs.export', 'Export System Logs', 'Export system logs', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.logs.export';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'system.services.control' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('system.services.control', 'Control Background Services', 'Start/stop/restart services', 'System', 1, SYSUTCDATETIME());
    PRINT '✅ Added: system.services.control';
END

-- Performance permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'performance.metrics.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('performance.metrics.view', 'View Performance Metrics', 'View detailed performance metrics', 'Performance', 1, SYSUTCDATETIME());
    PRINT '✅ Added: performance.metrics.view';
END

-- Analytics permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'analytics.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('analytics.view', 'View Analytics', 'View analytics dashboard', 'Analytics', 1, SYSUTCDATETIME());
    PRINT '✅ Added: analytics.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'analytics.advanced' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('analytics.advanced', 'Advanced Analytics', 'Advanced analytics features', 'Analytics', 1, SYSUTCDATETIME());
    PRINT '✅ Added: analytics.advanced';
END

-- API & Diagnostics permissions
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'api.test' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('api.test', 'Test API Endpoints', 'Access API testing tools', 'API', 1, SYSUTCDATETIME());
    PRINT '✅ Added: api.test';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'api.debug' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('api.debug', 'Debug API', 'Access debugging tools and endpoints', 'API', 1, SYSUTCDATETIME());
    PRINT '✅ Added: api.debug';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'diagnostics.view' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('diagnostics.view', 'View Diagnostics', 'Access system diagnostics', 'Diagnostics', 1, SYSUTCDATETIME());
    PRINT '✅ Added: diagnostics.view';
END

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Name = 'diagnostics.run' AND IsActive = 1)
BEGIN
    INSERT INTO Permissions (Name, DisplayName, Description, Category, IsActive, CreatedAt)
    VALUES ('diagnostics.run', 'Run Diagnostics', 'Execute diagnostic tests', 'Diagnostics', 1, SYSUTCDATETIME());
    PRINT '✅ Added: diagnostics.run';
END

-- Verify final count
DECLARE @FinalCount INT;
SELECT @FinalCount = COUNT(*) FROM Permissions WHERE IsActive = 1;
PRINT '';
PRINT CONCAT('✅ Final permissions count: ', @FinalCount, ' (was ', @CurrentCount, ')');
PRINT CONCAT('✅ Added ', @FinalCount - @CurrentCount, ' new permissions');
PRINT '';
PRINT '✅ Migration complete!';
GO

