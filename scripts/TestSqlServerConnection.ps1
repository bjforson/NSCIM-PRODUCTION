# Test SQL Server Connection
param(
    [string]$Server = "10.0.0.79",
    [string]$Username = "administrator",
    [string]$Password = "Haoyunbeijing18$",
    [string]$Database = "master"
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Testing SQL Server Connection" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $Server" -ForegroundColor White
Write-Host "Username: $Username" -ForegroundColor White
Write-Host "Database: $Database" -ForegroundColor White
Write-Host ""

try {
    # Build connection string
    $connectionString = "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;Connection Timeout=10;"
    
    Write-Host "Attempting to connect..." -ForegroundColor Yellow
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connection successful!" -ForegroundColor Green
    Write-Host ""
    
    # Test query to get server info
    Write-Host "Retrieving server information..." -ForegroundColor Yellow
    $query = @"
    SELECT 
        @@SERVERNAME AS ServerName,
        @@VERSION AS Version,
        DB_NAME() AS CurrentDatabase,
        SYSTEM_USER AS CurrentUser,
        GETDATE() AS ServerTime
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    if ($dataset.Tables[0].Rows.Count -gt 0) {
        $row = $dataset.Tables[0].Rows[0]
        Write-Host "Server Name: $($row['ServerName'])" -ForegroundColor Cyan
        Write-Host "Current Database: $($row['CurrentDatabase'])" -ForegroundColor Cyan
        Write-Host "Current User: $($row['CurrentUser'])" -ForegroundColor Cyan
        Write-Host "Server Time: $($row['ServerTime'])" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "SQL Server Version:" -ForegroundColor Cyan
        Write-Host $row['Version'] -ForegroundColor White
    }
    
    # List available databases
    Write-Host ""
    Write-Host "Listing available databases..." -ForegroundColor Yellow
    $dbQuery = "SELECT name, database_id, create_date FROM sys.databases ORDER BY name"
    $dbCommand = New-Object System.Data.SqlClient.SqlCommand($dbQuery, $connection)
    $dbAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($dbCommand)
    $dbDataset = New-Object System.Data.DataSet
    $dbAdapter.Fill($dbDataset) | Out-Null
    
    if ($dbDataset.Tables[0].Rows.Count -gt 0) {
        Write-Host "Found $($dbDataset.Tables[0].Rows.Count) database(s):" -ForegroundColor Green
        foreach ($dbRow in $dbDataset.Tables[0].Rows) {
            Write-Host "  - $($dbRow['name']) (ID: $($dbRow['database_id']), Created: $($dbRow['create_date']))" -ForegroundColor White
        }
    }
    
    Write-Host ""
    Write-Host "✅ Connection test completed successfully!" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Connection failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Yellow
    Write-Host $_.Exception.ToString() -ForegroundColor Red
    exit 1
} finally {
    if ($connection -and $connection.State -eq 'Open') {
        $connection.Close()
        Write-Host ""
        Write-Host "Connection closed." -ForegroundColor Gray
    }
}

