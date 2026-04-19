-- ================================================
-- Database Replication Script
-- Replicates databases from NS_CIS instance to MSSQLSERVER instance
-- Run this script on the NS_CIS instance to create backups
-- Then restore on MSSQLSERVER instance
-- ================================================

-- Step 1: Backup NS_CIS database
PRINT 'Backing up NS_CIS database...';
BACKUP DATABASE [NS_CIS] 
TO DISK = 'C:\Temp\DB_Backups\NS_CIS.bak'
WITH FORMAT, INIT, COMPRESSION, STATS = 10;
GO

-- Step 2: Backup ICUMS database
PRINT 'Backing up ICUMS database...';
BACKUP DATABASE [ICUMS] 
TO DISK = 'C:\Temp\DB_Backups\ICUMS.bak'
WITH FORMAT, INIT, COMPRESSION, STATS = 10;
GO

-- Step 3: Backup ICUMS_Downloads database
PRINT 'Backing up ICUMS_Downloads database...';
BACKUP DATABASE [ICUMS_Downloads] 
TO DISK = 'C:\Temp\DB_Backups\ICUMS_Downloads.bak'
WITH FORMAT, INIT, COMPRESSION, STATS = 10;
GO

PRINT 'Backup complete!';
PRINT 'Next: Run Restore_Databases.sql on MSSQLSERVER instance';

