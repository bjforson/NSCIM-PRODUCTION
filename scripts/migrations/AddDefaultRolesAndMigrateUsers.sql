-- ============================================================================
-- Migration Script: Add Default Roles and Migrate Users
-- Date: 2025-11-01
-- Description:
--   1. Creates default roles (Lead, Analyst, Audit, CustomsOfficer) as system roles
--   2. Migrates all users from LegacyRole enum to new Role system (assign RoleId)
--   3. Adds UserNumber column to Users table for anonymized reporting
-- ============================================================================

USE NS_CIS;
GO

PRINT '========================================';
PRINT 'Starting Role Migration Script';
PRINT '========================================';
PRINT '';

-- ============================================================================
-- STEP 1: Add UserNumber column to Users table
-- ============================================================================
PRINT 'STEP 1: Adding UserNumber column to Users table...';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.Users') 
    AND name = 'UserNumber'
)
BEGIN
    ALTER TABLE Users
    ADD UserNumber NVARCHAR(20) NULL;
    
    PRINT '✅ UserNumber column added';
    
    -- Create unique index
    CREATE UNIQUE NONCLUSTERED INDEX IX_Users_UserNumber
    ON Users(UserNumber)
    WHERE UserNumber IS NOT NULL;
    
    PRINT '✅ Unique index created on UserNumber';
END
ELSE
BEGIN
    PRINT 'ℹ️ UserNumber column already exists - skipping';
END
PRINT '';
GO

-- ============================================================================
-- STEP 2: Generate UserNumber for existing users
-- ============================================================================
PRINT 'STEP 2: Generating UserNumber for existing users...';

DECLARE @UserId INT;
DECLARE @UserCounter INT = 1;
DECLARE @UserNumber NVARCHAR(20);

DECLARE user_cursor CURSOR FOR
SELECT Id FROM Users WHERE UserNumber IS NULL ORDER BY Id;

OPEN user_cursor;
FETCH NEXT FROM user_cursor INTO @UserId;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @UserNumber = 'USR-' + RIGHT('00000' + CAST(@UserCounter AS VARCHAR(5)), 5);
    
    UPDATE Users
    SET UserNumber = @UserNumber
    WHERE Id = @UserId;
    
    SET @UserCounter = @UserCounter + 1;
    FETCH NEXT FROM user_cursor INTO @UserId;
END;

CLOSE user_cursor;
DEALLOCATE user_cursor;

PRINT '✅ UserNumber generated for ' + CAST(@UserCounter - 1 AS VARCHAR(10)) + ' existing users';
PRINT '';
GO

-- ============================================================================
-- STEP 3: Create default roles if they don't exist
-- ============================================================================
PRINT 'STEP 3: Creating default roles...';

-- Lead Role (maps to Admin base role)
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Lead')
BEGIN
    INSERT INTO Roles (Name, DisplayName, Description, BaseRole, IsSystemRole, IsActive, CreatedAt, CreatedBy)
    VALUES ('Lead', 'Lead Analyst', 'Lead analyst with administrative capabilities', 5, 1, 1, GETUTCDATE(), 'System');
    PRINT '✅ Created role: Lead';
END
ELSE
BEGIN
    PRINT 'ℹ️ Role "Lead" already exists - skipping';
END

-- Analyst Role (maps to Operator base role)
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Analyst')
BEGIN
    INSERT INTO Roles (Name, DisplayName, Description, BaseRole, IsSystemRole, IsActive, CreatedAt, CreatedBy)
    VALUES ('Analyst', 'Image Analyst', 'Image analysis specialist', 1, 1, 1, GETUTCDATE(), 'System');
    PRINT '✅ Created role: Analyst';
END
ELSE
BEGIN
    PRINT 'ℹ️ Role "Analyst" already exists - skipping';
END

-- Audit Role (maps to Supervisor base role)
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Audit')
BEGIN
    INSERT INTO Roles (Name, DisplayName, Description, BaseRole, IsSystemRole, IsActive, CreatedAt, CreatedBy)
    VALUES ('Audit', 'Audit Reviewer', 'Second-tier audit and review specialist', 3, 1, 1, GETUTCDATE(), 'System');
    PRINT '✅ Created role: Audit';
END
ELSE
BEGIN
    PRINT 'ℹ️ Role "Audit" already exists - skipping';
END

-- CustomsOfficer Role (maps to Operator base role)
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'CustomsOfficer')
BEGIN
    INSERT INTO Roles (Name, DisplayName, Description, BaseRole, IsSystemRole, IsActive, CreatedAt, CreatedBy)
    VALUES ('CustomsOfficer', 'Customs Officer', 'Customs officer with validation capabilities', 1, 1, 1, GETUTCDATE(), 'System');
    PRINT '✅ Created role: CustomsOfficer';
END
ELSE
BEGIN
    PRINT 'ℹ️ Role "CustomsOfficer" already exists - skipping';
END
PRINT '';

-- ============================================================================
-- STEP 4: Migrate users from LegacyRole to RoleId
-- ============================================================================
PRINT 'STEP 4: Migrating users from LegacyRole to RoleId...';

-- Migrate Viewer (0) → find Viewer role or skip
DECLARE @ViewerRoleId INT;
SELECT @ViewerRoleId = Id FROM Roles WHERE Name = 'Viewer' OR BaseRole = 0;
IF @ViewerRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @ViewerRoleId WHERE LegacyRole = 0 AND RoleId IS NULL;
    PRINT '✅ Migrated Viewer users';
END

-- Migrate Operator (1) → find Operator role or Analyst/CustomsOfficer
DECLARE @OperatorRoleId INT;
SELECT @OperatorRoleId = Id FROM Roles WHERE Name = 'Operator' OR BaseRole = 1 ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
IF @OperatorRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @OperatorRoleId WHERE LegacyRole = 1 AND RoleId IS NULL;
    PRINT '✅ Migrated Operator users';
END

-- Migrate ScannerOperator (2) → find ScannerOperator role
DECLARE @ScannerOperatorRoleId INT;
SELECT @ScannerOperatorRoleId = Id FROM Roles WHERE Name = 'ScannerOperator' OR BaseRole = 2;
IF @ScannerOperatorRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @ScannerOperatorRoleId WHERE LegacyRole = 2 AND RoleId IS NULL;
    PRINT '✅ Migrated ScannerOperator users';
END

-- Migrate Supervisor (3) → find Supervisor role or Audit
DECLARE @SupervisorRoleId INT;
SELECT @SupervisorRoleId = Id FROM Roles WHERE Name = 'Supervisor' OR Name = 'Audit' OR BaseRole = 3 ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
IF @SupervisorRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @SupervisorRoleId WHERE LegacyRole = 3 AND RoleId IS NULL;
    PRINT '✅ Migrated Supervisor users';
END

-- Migrate Manager (4) → find Manager role
DECLARE @ManagerRoleId INT;
SELECT @ManagerRoleId = Id FROM Roles WHERE Name = 'Manager' OR BaseRole = 4;
IF @ManagerRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @ManagerRoleId WHERE LegacyRole = 4 AND RoleId IS NULL;
    PRINT '✅ Migrated Manager users';
END

-- Migrate Admin (5) → find Admin role or Lead
DECLARE @AdminRoleId INT;
SELECT @AdminRoleId = Id FROM Roles WHERE Name = 'Admin' OR Name = 'Lead' OR BaseRole = 5 ORDER BY CASE WHEN Name = 'Admin' THEN 1 ELSE 2 END;
IF @AdminRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @AdminRoleId WHERE LegacyRole = 5 AND RoleId IS NULL;
    PRINT '✅ Migrated Admin users';
END

-- Migrate SuperAdmin (6) → find SuperAdmin role
DECLARE @SuperAdminRoleId INT;
SELECT @SuperAdminRoleId = Id FROM Roles WHERE Name = 'SuperAdmin' OR BaseRole = 6;
IF @SuperAdminRoleId IS NOT NULL
BEGIN
    UPDATE Users SET RoleId = @SuperAdminRoleId WHERE LegacyRole = 6 AND RoleId IS NULL;
    PRINT '✅ Migrated SuperAdmin users';
END

-- Count migrated users
DECLARE @MigratedCount INT;
SELECT @MigratedCount = COUNT(*) FROM Users WHERE RoleId IS NOT NULL;
PRINT '✅ Total users with RoleId assigned: ' + CAST(@MigratedCount AS VARCHAR(10));
PRINT '';

-- ============================================================================
-- STEP 5: Verify migration
-- ============================================================================
PRINT 'STEP 5: Verifying migration...';

-- Check for users without RoleId
DECLARE @UnmigratedCount INT;
SELECT @UnmigratedCount = COUNT(*) FROM Users WHERE RoleId IS NULL AND IsActive = 1;
IF @UnmigratedCount > 0
BEGIN
    PRINT '⚠️ WARNING: ' + CAST(@UnmigratedCount AS VARCHAR(10)) + ' active users still have NULL RoleId';
    PRINT '   These users may need manual role assignment';
END
ELSE
BEGIN
    PRINT '✅ All active users have RoleId assigned';
END

-- Check for roles created
DECLARE @RolesCreated INT;
SELECT @RolesCreated = COUNT(*) FROM Roles WHERE Name IN ('Lead', 'Analyst', 'Audit', 'CustomsOfficer');
PRINT '✅ ' + CAST(@RolesCreated AS VARCHAR(10)) + ' default roles created';
PRINT '';

PRINT '========================================';
PRINT 'Migration Script Complete!';
PRINT '========================================';
PRINT '';
PRINT 'Summary:';
PRINT '  - UserNumber column added to Users table';
PRINT '  - Default roles created (Lead, Analyst, Audit, CustomsOfficer)';
PRINT '  - Users migrated from LegacyRole to RoleId';
PRINT '  - All active users should now have RoleId assigned';
PRINT '';
GO

