-- ============================================
-- Ensure SuperAdmin Role Has All Permissions
-- Migration: Fix SuperAdmin Permission Assignment
-- ============================================

USE NS_CIS;
GO

PRINT '========================================';
PRINT 'Ensuring SuperAdmin has all permissions';
PRINT '========================================';
PRINT '';

-- Step 1: Get SuperAdmin Role ID
DECLARE @SuperAdminRoleId INT;
SELECT @SuperAdminRoleId = Id FROM Roles WHERE Name = 'SuperAdmin' AND IsActive = 1;

IF @SuperAdminRoleId IS NULL
BEGIN
    PRINT '❌ ERROR: SuperAdmin role not found!';
    PRINT '   Creating SuperAdmin role...';
    
    -- Try to find by BaseRole
    SELECT @SuperAdminRoleId = Id FROM Roles WHERE BaseRole = 6 AND IsActive = 1; -- SuperAdmin = 6
    
    IF @SuperAdminRoleId IS NULL
    BEGIN
        PRINT '   SuperAdmin role does not exist - please run PermissionSeeder first';
        RETURN;
    END
END

PRINT CONCAT('✅ Found SuperAdmin role with ID: ', @SuperAdminRoleId);
PRINT '';

-- Step 2: Get all active permissions
DECLARE @TotalPermissions INT;
SELECT @TotalPermissions = COUNT(*) FROM Permissions WHERE IsActive = 1;
PRINT CONCAT('📊 Total active permissions in system: ', @TotalPermissions);
PRINT '';

-- Step 3: Get current permissions assigned to SuperAdmin
DECLARE @CurrentPermissions INT;
SELECT @CurrentPermissions = COUNT(*)
FROM RolePermissions rp
INNER JOIN Permissions p ON rp.PermissionId = p.Id
WHERE rp.RoleId = @SuperAdminRoleId AND p.IsActive = 1;

PRINT CONCAT('📋 Current permissions assigned to SuperAdmin: ', @CurrentPermissions);
PRINT '';

-- Step 4: Assign all missing permissions to SuperAdmin
INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt, GrantedBy)
SELECT 
    @SuperAdminRoleId,
    p.Id,
    SYSUTCDATETIME(),
    'System'
FROM Permissions p
WHERE p.IsActive = 1
AND p.Id NOT IN (
    SELECT PermissionId 
    FROM RolePermissions 
    WHERE RoleId = @SuperAdminRoleId
);

DECLARE @NewPermissions INT = @@ROWCOUNT;
PRINT CONCAT('✅ Assigned ', @NewPermissions, ' new permissions to SuperAdmin');
PRINT '';

-- Step 5: Verify final count
DECLARE @FinalCount INT;
SELECT @FinalCount = COUNT(*)
FROM RolePermissions rp
INNER JOIN Permissions p ON rp.PermissionId = p.Id
WHERE rp.RoleId = @SuperAdminRoleId AND p.IsActive = 1;

PRINT CONCAT('✅ Final permissions count for SuperAdmin: ', @FinalCount, ' of ', @TotalPermissions);
PRINT '';

IF @FinalCount = @TotalPermissions
BEGIN
    PRINT '✅ SUCCESS: SuperAdmin has ALL permissions assigned!';
END
ELSE IF @FinalCount < @TotalPermissions
BEGIN
    PRINT CONCAT('⚠️ WARNING: SuperAdmin has ', @FinalCount, ' of ', @TotalPermissions, ' permissions');
    PRINT '   There may be inactive permissions or a mismatch.';
END

-- Step 6: Show summary
PRINT '';
PRINT '========================================';
PRINT 'SuperAdmin Permission Summary';
PRINT '========================================';
SELECT 
    p.Name AS PermissionName,
    p.DisplayName AS DisplayName,
    p.Category AS Category,
    rp.GrantedAt AS AssignedAt
FROM RolePermissions rp
INNER JOIN Permissions p ON rp.PermissionId = p.Id
WHERE rp.RoleId = @SuperAdminRoleId AND p.IsActive = 1
ORDER BY p.Category, p.Name;

PRINT '';
PRINT '✅ Migration complete!';
GO

