# Get SQL Server Information
param(
    [string]$Server = "10.0.0.79"
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "SQL Server Information" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connectionString = "Server=$Server;Database=master;Integrated Security=true;Connection Timeout=10;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to SQL Server" -ForegroundColor Green
    Write-Host ""
    
    # Get server information
    Write-Host "Server Information:" -ForegroundColor Yellow
    $serverInfoQuery = @"
    SELECT 
        @@SERVERNAME AS ServerName,
        @@VERSION AS Version,
        @@SERVICENAME AS ServiceName,
        DB_NAME() AS CurrentDatabase,
        SYSTEM_USER AS CurrentUser,
        SUSER_SNAME() AS LoginName,
        GETDATE() AS ServerTime,
        SERVERPROPERTY('ProductVersion') AS ProductVersion,
        SERVERPROPERTY('ProductLevel') AS ProductLevel,
        SERVERPROPERTY('Edition') AS Edition,
        SERVERPROPERTY('InstanceName') AS InstanceName
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($serverInfoQuery, $connection)
    $reader = $command.ExecuteReader()
    if ($reader.Read()) {
        Write-Host "   Server Name: $($reader['ServerName'])" -ForegroundColor Cyan
        Write-Host "   Instance Name: $(if ([DBNull]::Value.Equals($reader['InstanceName']) -or [string]::IsNullOrEmpty($reader['InstanceName'])) { 'Default' } else { $reader['InstanceName'] })" -ForegroundColor Cyan
        Write-Host "   Service Name: $($reader['ServiceName'])" -ForegroundColor Cyan
        Write-Host "   Edition: $($reader['Edition'])" -ForegroundColor Cyan
        Write-Host "   Product Level: $($reader['ProductLevel'])" -ForegroundColor Cyan
        Write-Host "   Product Version: $($reader['ProductVersion'])" -ForegroundColor Cyan
        Write-Host "   Current User: $($reader['CurrentUser'])" -ForegroundColor Cyan
        Write-Host "   Login Name: $($reader['LoginName'])" -ForegroundColor Cyan
        Write-Host "   Server Time: $($reader['ServerTime'])" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "   SQL Server Version:" -ForegroundColor Cyan
        $version = $reader['Version'].ToString()
        $versionLines = $version -split "`n"
        foreach ($line in $versionLines) {
            Write-Host "     $line" -ForegroundColor White
        }
    }
    $reader.Close()
    
    Write-Host ""
    Write-Host "Available Databases:" -ForegroundColor Yellow
    $dbQuery = @"
    SELECT 
        name,
        database_id,
        create_date,
        state_desc,
        recovery_model_desc,
        compatibility_level
    FROM sys.databases
    ORDER BY name
"@
    
    $dbCommand = New-Object System.Data.SqlClient.SqlCommand($dbQuery, $connection)
    $dbReader = $dbCommand.ExecuteReader()
    $dbCount = 0
    while ($dbReader.Read()) {
        $dbCount++
        $state = $dbReader['state_desc']
        $stateColor = if ($state -eq "ONLINE") { "Green" } else { "Red" }
        Write-Host "   [$dbCount] $($dbReader['name'])" -ForegroundColor White
        Write-Host "       State: $state" -ForegroundColor $stateColor
        Write-Host "       Created: $($dbReader['create_date'])" -ForegroundColor Gray
        Write-Host "       Compatibility: $($dbReader['compatibility_level'])" -ForegroundColor Gray
        Write-Host ""
    }
    $dbReader.Close()
    
    Write-Host "Total databases: $dbCount" -ForegroundColor Cyan
    Write-Host ""
    
    # Check for NS_CIS database specifically
    Write-Host "Checking for NS_CIS database..." -ForegroundColor Yellow
    $nsCisQuery = "SELECT name FROM sys.databases WHERE name = 'NS_CIS'"
    $nsCisCommand = New-Object System.Data.SqlClient.SqlCommand($nsCisQuery, $connection)
    $nsCisExists = $nsCisCommand.ExecuteScalar()
    
    if ($nsCisExists) {
        Write-Host "✅ NS_CIS database found!" -ForegroundColor Green
        Write-Host ""
        
        # Get NS_CIS database info
        $nsCisInfoQuery = @"
        USE NS_CIS;
        SELECT 
            DB_NAME() AS DatabaseName,
            COUNT(*) AS TableCount
        FROM sys.tables;
        
        SELECT 
            COUNT(*) AS UserCount
        FROM sys.database_principals
        WHERE type = 'S';
"@
        
        $nsCisInfoCommand = New-Object System.Data.SqlClient.SqlCommand($nsCisInfoQuery, $connection)
        $nsCisInfoReader = $nsCisInfoCommand.ExecuteReader()
        if ($nsCisInfoReader.Read()) {
            Write-Host "   Database: $($nsCisInfoReader['DatabaseName'])" -ForegroundColor Cyan
            Write-Host "   Tables: $($nsCisInfoReader['TableCount'])" -ForegroundColor Cyan
        }
        if ($nsCisInfoReader.NextResult() -and $nsCisInfoReader.Read()) {
            Write-Host "   Users: $($nsCisInfoReader['UserCount'])" -ForegroundColor Cyan
        }
        $nsCisInfoReader.Close()
    } else {
        Write-Host "⚠️ NS_CIS database not found" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "✅ Information retrieval completed!" -ForegroundColor Green
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
}

