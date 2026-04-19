-- ================================================
-- Create Databases for SQL Server 2014
-- NickScan Central Imaging Portal
-- Compatibility Level: 120 (SQL Server 2014)
-- ================================================

PRINT '========================================';
PRINT 'Creating Databases for SQL Server 2014';
PRINT '========================================';
PRINT '';

-- ================================================
-- Step 1: Create NS_CIS Database
-- ================================================
PRINT 'Creating NS_CIS database...';

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'NS_CIS')
BEGIN
    PRINT '  → NS_CIS database already exists. Dropping...';
    ALTER DATABASE NS_CIS SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE NS_CIS;
END

CREATE DATABASE NS_CIS;
GO

USE NS_CIS;
GO

-- Set compatibility level to SQL Server 2014 (120)
ALTER DATABASE NS_CIS SET COMPATIBILITY_LEVEL = 120;
PRINT '  ✓ NS_CIS created with compatibility level 120';

-- Set database options
ALTER DATABASE NS_CIS SET RECOVERY MODEL SIMPLE;
ALTER DATABASE NS_CIS SET AUTO_SHRINK ON;
ALTER DATABASE NS_CIS SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE NS_CIS SET AUTO_UPDATE_STATISTICS ON;
PRINT '  ✓ Database options configured';
GO

PRINT '';

-- ================================================
-- Step 2: Create ICUMS Database
-- ================================================
PRINT 'Creating ICUMS database...';

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'ICUMS')
BEGIN
    PRINT '  → ICUMS database already exists. Dropping...';
    ALTER DATABASE ICUMS SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ICUMS;
END

CREATE DATABASE ICUMS;
GO

USE ICUMS;
GO

-- Set compatibility level to SQL Server 2014 (120)
ALTER DATABASE ICUMS SET COMPATIBILITY_LEVEL = 120;
PRINT '  ✓ ICUMS created with compatibility level 120';

-- Set database options
ALTER DATABASE ICUMS SET RECOVERY MODEL SIMPLE;
ALTER DATABASE ICUMS SET AUTO_SHRINK ON;
ALTER DATABASE ICUMS SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE ICUMS SET AUTO_UPDATE_STATISTICS ON;
PRINT '  ✓ Database options configured';
GO

PRINT '';

-- ================================================
-- Step 3: Create ICUMS_Downloads Database
-- ================================================
PRINT 'Creating ICUMS_Downloads database...';

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'ICUMS_Downloads')
BEGIN
    PRINT '  → ICUMS_Downloads database already exists. Dropping...';
    ALTER DATABASE ICUMS_Downloads SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ICUMS_Downloads;
END

CREATE DATABASE ICUMS_Downloads;
GO

USE ICUMS_Downloads;
GO

-- Set compatibility level to SQL Server 2014 (120)
ALTER DATABASE ICUMS_Downloads SET COMPATIBILITY_LEVEL = 120;
PRINT '  ✓ ICUMS_Downloads created with compatibility level 120';

-- Set database options
ALTER DATABASE ICUMS_Downloads SET RECOVERY MODEL SIMPLE;
ALTER DATABASE ICUMS_Downloads SET AUTO_SHRINK ON;
ALTER DATABASE ICUMS_Downloads SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE ICUMS_Downloads SET AUTO_UPDATE_STATISTICS ON;
PRINT '  ✓ Database options configured';
GO

PRINT '';

-- ================================================
-- Step 4: Verify Databases
-- ================================================
PRINT '========================================';
PRINT 'Verification';
PRINT '========================================';
PRINT '';

SELECT 
    name AS DatabaseName,
    compatibility_level AS CompatibilityLevel,
    recovery_model_desc AS RecoveryModel,
    state_desc AS State
FROM sys.databases
WHERE name IN ('NS_CIS', 'ICUMS', 'ICUMS_Downloads')
ORDER BY name;

PRINT '';
PRINT '========================================';
PRINT 'Database Creation Complete!';
PRINT '========================================';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Update connection strings in appsettings.json';
PRINT '2. Run EF Core migrations using:';
PRINT '   .\scripts\Run-DatabaseMigrations.ps1';
PRINT '';

