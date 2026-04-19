-- ================================================
-- Step 2: Transfer Data from NS_CIS instance to MSSQLSERVER
-- Run this on MSSQLSERVER (default) instance
-- ================================================

-- Create linked server if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.servers WHERE name = 'NS_CIS_SOURCE')
BEGIN
    EXEC sp_addlinkedserver
        @server = 'NS_CIS_SOURCE',
        @srvproduct = 'SQL Server';
    
    EXEC sp_addlinkedsrvlogin
        @rmtsrvname = 'NS_CIS_SOURCE',
        @useself = 'true',
        @locallogin = NULL,
        @rmtuser = NULL,
        @rmtpassword = NULL;
    
    PRINT 'Linked server NS_CIS_SOURCE created successfully.';
END
ELSE
BEGIN
    PRINT 'Linked server NS_CIS_SOURCE already exists.';
END
GO

-- Test connection
PRINT 'Testing linked server connection...';
SELECT @@SERVERNAME AS LocalServer;
SELECT @@SERVERNAME AS RemoteServer FROM [NS_CIS_SOURCE].master.sys.databases WHERE name = 'master';
GO

PRINT '';
PRINT 'Linked server is ready for data transfer.';
PRINT 'Next: Run Step2_Transfer_Data_AllTables.ps1 to transfer all data.';
GO

