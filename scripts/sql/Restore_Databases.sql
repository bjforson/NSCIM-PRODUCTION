-- ================================================
-- Database Restore Script
-- Restores databases from backups to MSSQLSERVER instance
-- Run this script on the MSSQLSERVER (default) instance
-- ================================================

-- Note: Adjust file paths in MOVE clauses based on your SQL Server installation
-- Default paths for MSSQLSERVER instance:
-- Data: C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA
-- Log:  C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA

-- Step 1: Restore NS_CIS database
PRINT 'Restoring NS_CIS database...';

-- Drop existing database if it exists
IF EXISTS (SELECT name FROM sys.databases WHERE name = 'NS_CIS')
BEGIN
    ALTER DATABASE [NS_CIS] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [NS_CIS];
END
GO

-- Restore database (adjust MOVE paths as needed)
RESTORE DATABASE [NS_CIS] 
FROM DISK = 'C:\Temp\DB_Backups\NS_CIS.bak'
WITH REPLACE, STATS = 10,
MOVE 'NS_CIS' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\NS_CIS.mdf',
MOVE 'NS_CIS_log' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\NS_CIS.ldf';
GO

-- Step 2: Restore ICUMS database
PRINT 'Restoring ICUMS database...';

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'ICUMS')
BEGIN
    ALTER DATABASE [ICUMS] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [ICUMS];
END
GO

RESTORE DATABASE [ICUMS] 
FROM DISK = 'C:\Temp\DB_Backups\ICUMS.bak'
WITH REPLACE, STATS = 10,
MOVE 'ICUMS' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ICUMS.mdf',
MOVE 'ICUMS_log' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ICUMS.ldf';
GO

-- Step 3: Restore ICUMS_Downloads database
PRINT 'Restoring ICUMS_Downloads database...';

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'ICUMS_Downloads')
BEGIN
    ALTER DATABASE [ICUMS_Downloads] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [ICUMS_Downloads];
END
GO

RESTORE DATABASE [ICUMS_Downloads] 
FROM DISK = 'C:\Temp\DB_Backups\ICUMS_Downloads.bak'
WITH REPLACE, STATS = 10,
MOVE 'ICUMS_Downloads' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ICUMS_Downloads.mdf',
MOVE 'ICUMS_Downloads_log' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ICUMS_Downloads.ldf';
GO

PRINT 'Restore complete!';
PRINT 'All databases have been replicated from NS_CIS instance to MSSQLSERVER instance.';

