-- ================================================
-- Setup Database Permissions for SQL Server 2014
-- NickScan Central Imaging Portal
-- ================================================
-- 
-- This script grants necessary permissions to service accounts
-- for Windows Authentication (Trusted_Connection=true)
--
-- IMPORTANT: Replace 'DOMAIN\ServiceAccount' with your actual service account
-- ================================================

PRINT '========================================';
PRINT 'Setting up Database Permissions';
PRINT '========================================';
PRINT '';

-- ================================================
-- Configuration: Update these values
-- ================================================
-- Using current Windows user for setup
DECLARE @ServiceAccount NVARCHAR(255) = SYSTEM_USER;  -- Current Windows user
-- For production, update to your service account:
-- 'DOMAIN\ServiceAccount' or 'IIS APPPOOL\YourAppPoolName'

DECLARE @Databases TABLE (DatabaseName NVARCHAR(255));
INSERT INTO @Databases VALUES ('NS_CIS'), ('ICUMS'), ('ICUMS_Downloads');

PRINT 'Service Account: ' + @ServiceAccount;
PRINT '';

-- ================================================
-- Step 1: Create SQL Server Login (if needed)
-- ================================================
PRINT 'Step 1: Creating SQL Server Login...';

IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = @ServiceAccount)
BEGIN
    -- For Windows accounts, create login from Windows
    DECLARE @CreateLoginSQL NVARCHAR(MAX) = 
        'CREATE LOGIN [' + @ServiceAccount + '] FROM WINDOWS;';
    
    EXEC sp_executesql @CreateLoginSQL;
    PRINT '  ✓ Created SQL Server login: ' + @ServiceAccount;
END
ELSE
BEGIN
    PRINT '  ✓ SQL Server login already exists: ' + @ServiceAccount;
END
PRINT '';

-- ================================================
-- Step 2: Grant Database Permissions
-- ================================================
PRINT 'Step 2: Granting database permissions...';

DECLARE @DbName NVARCHAR(255);
DECLARE @SQL NVARCHAR(MAX);

DECLARE db_cursor CURSOR FOR
SELECT DatabaseName FROM @Databases;

OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @DbName;

WHILE @@FETCH_STATUS = 0
BEGIN
    PRINT '  Processing database: ' + @DbName;
    
    -- Create user in database (if doesn't exist)
    SET @SQL = N'USE [' + @DbName + N'];
    IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = ''' + @ServiceAccount + ''')
    BEGIN
        CREATE USER [' + @ServiceAccount + N'] FOR LOGIN [' + @ServiceAccount + N'];
        PRINT ''    ✓ Created database user'';
    END
    ELSE
    BEGIN
        PRINT ''    ✓ Database user already exists'';
    END';
    
    EXEC sp_executesql @SQL;
    
    -- Grant db_datareader role (read access)
    SET @SQL = N'USE [' + @DbName + N'];
    ALTER ROLE db_datareader ADD MEMBER [' + @ServiceAccount + N'];
    PRINT ''    ✓ Granted db_datareader role'';
    ';
    
    EXEC sp_executesql @SQL;
    
    -- Grant db_datawriter role (write access)
    SET @SQL = N'USE [' + @DbName + N'];
    ALTER ROLE db_datawriter ADD MEMBER [' + @ServiceAccount + N'];
    PRINT ''    ✓ Granted db_datawriter role'';
    ';
    
    EXEC sp_executesql @SQL;
    
    -- Grant db_ddladmin role (schema changes for migrations)
    SET @SQL = N'USE [' + @DbName + N'];
    ALTER ROLE db_ddladmin ADD MEMBER [' + @ServiceAccount + N'];
    PRINT ''    ✓ Granted db_ddladmin role (for migrations)'';
    ';
    
    EXEC sp_executesql @SQL;
    
    -- Grant explicit permissions for EF Core migrations
    SET @SQL = N'USE [' + @DbName + N'];
    GRANT CREATE TABLE TO [' + @ServiceAccount + N'];
    GRANT CREATE VIEW TO [' + @ServiceAccount + N'];
    GRANT CREATE PROCEDURE TO [' + @ServiceAccount + N'];
    GRANT CREATE FUNCTION TO [' + @ServiceAccount + N'];
    GRANT ALTER ON SCHEMA::dbo TO [' + @ServiceAccount + N'];
    PRINT ''    ✓ Granted DDL permissions for migrations'';
    ';
    
    EXEC sp_executesql @SQL;
    
    PRINT '';
    
    FETCH NEXT FROM db_cursor INTO @DbName;
END

CLOSE db_cursor;
DEALLOCATE db_cursor;

-- ================================================
-- Step 3: Verify Permissions
-- ================================================
PRINT 'Step 3: Verifying permissions...';
PRINT '';

DECLARE @VerifySQL NVARCHAR(MAX) = N'
SELECT 
    dp.name AS DatabaseUser,
    dp.type_desc AS UserType,
    r.name AS RoleName,
    r.type_desc AS RoleType
FROM sys.database_role_members rm
INNER JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
INNER JOIN sys.database_principals dp ON rm.member_principal_id = dp.principal_id
WHERE dp.name = ''' + @ServiceAccount + '''
ORDER BY r.name;
';

DECLARE @DbName2 NVARCHAR(255);
DECLARE db_cursor2 CURSOR FOR
SELECT DatabaseName FROM @Databases;

OPEN db_cursor2;
FETCH NEXT FROM db_cursor2 INTO @DbName2;

WHILE @@FETCH_STATUS = 0
BEGIN
    PRINT '  Database: ' + @DbName2;
    SET @SQL = N'USE [' + @DbName2 + N']; ' + @VerifySQL;
    EXEC sp_executesql @SQL;
    PRINT '';
    
    FETCH NEXT FROM db_cursor2 INTO @DbName2;
END

CLOSE db_cursor2;
DEALLOCATE db_cursor2;

PRINT '========================================';
PRINT 'Permission Setup Complete!';
PRINT '========================================';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Verify service account can connect to databases';
PRINT '2. Test EF Core migrations with service account';
PRINT '3. Monitor for permission-related errors in logs';
PRINT '';

