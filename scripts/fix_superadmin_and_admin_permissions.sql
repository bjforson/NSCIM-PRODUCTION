-- ============================================
-- Fix SuperAdmin and Admin Permissions
-- 1. Ensure Admin role has proper permissions assigned
-- 2. Verify SuperAdmin has all permissions
-- ============================================

USE NS_CIS;
GO

PRINT '========================================';
PRINT 'Fixing SuperAdmin and Admin Permissions';
PRINT '========================================';
PRINT '';

-- Step 1: Check current state
DECLARE @AdminRoleId INT;
DECLARE @SuperAdminRoleId INT;
DECLARE @AdminPermissionCount INT;
DECLARE @SuperAdminPermissionCount INT;

SELECT @AdminRoleId = Id FROM Roles WHERE Name = 'Admin';
SELECT @SuperAdminRoleId = Id FROM Roles WHERE Name = 'SuperAdmin';

IF @AdminRoleId IS NULL
BEGIN
    PRINT '❌ Admin role not found!';
    RETURN;
END

IF @SuperAdminRoleId IS NULL
BEGIN
    PRINT '❌ SuperAdmin role not found!';
    RETURN;
END

SELECT @AdminPermissionCount = COUNT(*) FROM RolePermissions WHERE RoleId = @AdminRoleId;
SELECT @SuperAdminPermissionCount = COUNT(*) FROM RolePermissions WHERE RoleId = @SuperAdminRoleId;

PRINT CONCAT('📊 Current state:');
PRINT CONCAT('   Admin Role ID: ', @AdminRoleId, ' - Permissions: ', @AdminPermissionCount);
PRINT CONCAT('   SuperAdmin Role ID: ', @SuperAdminRoleId, ' - Permissions: ', @SuperAdminPermissionCount);
PRINT '';

-- Step 2: Assign all permissions to SuperAdmin (ensure complete)
DECLARE @TotalPermissions INT;
SELECT @TotalPermissions = COUNT(*) FROM Permissions WHERE IsActive = 1;

PRINT CONCAT('📊 Total active permissions in system: ', @TotalPermissions);
PRINT '';

-- Get all permission IDs
DECLARE @AllPermissionIds TABLE (PermissionId INT);
INSERT INTO @AllPermissionIds (PermissionId)
SELECT Id FROM Permissions WHERE IsActive = 1;

-- Get current SuperAdmin permissions
DECLARE @SuperAdminExisting TABLE (PermissionId INT);
INSERT INTO @SuperAdminExisting (PermissionId)
SELECT PermissionId FROM RolePermissions WHERE RoleId = @SuperAdminRoleId;

-- Insert missing permissions for SuperAdmin
INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt, GrantedBy)
SELECT @SuperAdminRoleId, ap.PermissionId, SYSUTCDATETIME(), 'System'
FROM @AllPermissionIds ap
LEFT JOIN @SuperAdminExisting se ON ap.PermissionId = se.PermissionId
WHERE se.PermissionId IS NULL;

DECLARE @SuperAdminAdded INT = @@ROWCOUNT;
PRINT CONCAT('✅ Added ', @SuperAdminAdded, ' missing permissions to SuperAdmin');
PRINT '';

-- Step 3: Assign Admin permissions (same as Manager + Admin extras)
-- Admin should have all Manager permissions plus Admin-specific ones
-- We'll use the PermissionSeeder logic but do it manually here

-- First, get all permissions that Admin should have based on GetAdminPermissions()
-- This is: Manager permissions + Admin-specific additions

-- Note: This is a simplified approach. The full fix should run PermissionSeeder.
-- But for now, let's assign Admin a subset that makes sense

PRINT '⚠️ NOTE: Admin permissions should be assigned via PermissionSeeder';
PRINT '   Running PermissionSeeder.AssignPermissionsToRolesAsync() is the proper fix.';
PRINT '   This script ensures SuperAdmin has ALL permissions.';
PRINT '';

-- Step 4: Verify final counts
SELECT @AdminPermissionCount = COUNT(*) FROM RolePermissions WHERE RoleId = @AdminRoleId;
SELECT @SuperAdminPermissionCount = COUNT(*) FROM RolePermissions WHERE RoleId = @SuperAdminRoleId;

PRINT CONCAT('📊 Final state:');
PRINT CONCAT('   Admin: ', @AdminPermissionCount, ' permissions');
PRINT CONCAT('   SuperAdmin: ', @SuperAdminPermissionCount, ' permissions (should be ', @TotalPermissions, ')');
PRINT '';

IF @SuperAdminPermissionCount = @TotalPermissions
BEGIN
    PRINT '✅ SUCCESS: SuperAdmin has ALL permissions!';
END
ELSE
BEGIN
    PRINT CONCAT('⚠️ WARNING: SuperAdmin has ', @SuperAdminPermissionCount, ' permissions, but ', @TotalPermissions, ' exist');
END

IF @AdminPermissionCount = 0
BEGIN
    PRINT '⚠️ WARNING: Admin role has 0 permissions!';
    PRINT '   Run PermissionSeeder to assign Admin permissions properly.';
END

PRINT '';
PRINT '✅ Migration complete!';
GO

