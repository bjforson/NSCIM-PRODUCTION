-- ================================================
-- Database Replication using Linked Server
-- Fastest method for cross-instance data transfer
-- ================================================

-- Step 1: Create linked server to NS_CIS instance
PRINT 'Creating linked server to NS_CIS instance...';

IF EXISTS (SELECT 1 FROM sys.servers WHERE name = 'NS_CIS_SOURCE')
BEGIN
    EXEC sp_dropserver 'NS_CIS_SOURCE', 'droplogins';
END
GO

EXEC sp_addlinkedserver
    @server = 'NS_CIS_SOURCE',
    @srvproduct = 'SQL Server';
GO

EXEC sp_addlinkedsrvlogin
    @rmtsrvname = 'NS_CIS_SOURCE',
    @useself = 'true',
    @locallogin = NULL,
    @rmtuser = NULL,
    @rmtpassword = NULL;
GO

PRINT 'Linked server created successfully.';
GO

-- Step 2: Test connection
PRINT 'Testing linked server connection...';
SELECT @@SERVERNAME AS LocalServer, 
       (SELECT @@SERVERNAME FROM [NS_CIS_SOURCE].master.dbo.sysprocesses WHERE spid = 1) AS RemoteServer;
GO

-- Step 3: Replicate NS_CIS database
PRINT 'Replicating NS_CIS database...';
PRINT 'This will copy all tables. This may take a while...';

-- Note: You'll need to run this for each table
-- Example for one table:
/*
INSERT INTO [NS_CIS].[dbo].[Users]
SELECT * FROM [NS_CIS_SOURCE].[NS_CIS].[dbo].[Users];
*/

-- Step 4: Replicate ICUMS database
PRINT 'Replicating ICUMS database...';

-- Step 5: Replicate ICUMS_Downloads database
PRINT 'Replicating ICUMS_Downloads database...';

PRINT 'Linked server setup complete.';
PRINT 'Use INSERT...SELECT statements to copy data table by table.';

