-- SQL Server Memory Configuration Script (Fixed)
-- Enables advanced options first, then configures memory
--
-- Usage: Run this script in SQL Server Management Studio or via sqlcmd
-- Requires: SQL Server admin permissions

USE master;
GO

-- Step 1: Enable advanced options (required to change memory settings)
PRINT 'Enabling advanced options...';
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
GO

PRINT 'Advanced options enabled.';
PRINT '';

-- Step 2: Configure max server memory (18 GB = 18432 MB)
-- Adjust this value based on your system RAM:
--   - For 32 GB system: 18 GB (18432 MB) - leaves 14 GB for OS
--   - For 64 GB system: 40 GB (40960 MB) - leaves 24 GB for OS
PRINT 'Setting max server memory to 18 GB (18432 MB)...';
EXEC sp_configure 'max server memory (MB)', 18432;
RECONFIGURE;
GO

PRINT 'Max server memory configured.';
PRINT '';

-- Step 3: Configure min server memory (4 GB = 4096 MB)
PRINT 'Setting min server memory to 4 GB (4096 MB)...';
EXEC sp_configure 'min server memory (MB)', 4096;
RECONFIGURE;
GO

PRINT 'Min server memory configured.';
PRINT '';

-- Step 4: Verify the settings
PRINT '========================================';
PRINT 'Verification: Current Memory Settings';
PRINT '========================================';
SELECT 
    name AS ConfigurationOption,
    CAST(value AS INT) AS ConfiguredValueMB,
    CAST(value_in_use AS INT) AS CurrentValueMB,
    CASE 
        WHEN CAST(value AS INT) = CAST(value_in_use AS INT) THEN 'Applied'
        ELSE 'Pending (requires restart)'
    END AS Status
FROM sys.configurations 
WHERE name IN ('max server memory (MB)', 'min server memory (MB)')
ORDER BY name;
GO

PRINT '';
PRINT '✅ Memory configuration complete!';
PRINT '';
PRINT 'Note: SQL Server will gradually adjust memory usage to the new limits.';
PRINT '      Monitor memory usage over the next few minutes.';
GO

