# Test SQL Server Connection with Windows Authentication
param(
    [string]$Server = "10.0.0.79",
    [string]$Username = "administrator",
    [string]$Password = "Haoyunbeijing18$"
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Testing SQL Server Connection (Windows Auth)" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $Server" -ForegroundColor White
Write-Host "Username: $Username" -ForegroundColor White
Write-Host "Authentication: Windows Authentication" -ForegroundColor White
Write-Host ""

# Method 1: SQL Server Authentication (using Windows username/password)
Write-Host "Method 1: SQL Server Authentication (Windows credentials)..." -ForegroundColor Yellow
try {
    $connectionString = "Server=$Server;Database=master;User Id=$Username;Password=$Password;Connection Timeout=10;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ SUCCESS: SQL Server Authentication works!" -ForegroundColor Green
    
    # Get server info
    $query = "SELECT @@SERVERNAME AS ServerName, @@VERSION AS Version, SYSTEM_USER AS CurrentUser, GETDATE() AS ServerTime"
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $reader = $command.ExecuteReader()
    if ($reader.Read()) {
        Write-Host "   Server Name: $($reader['ServerName'])" -ForegroundColor Cyan
        Write-Host "   Current User: $($reader['CurrentUser'])" -ForegroundColor Cyan
        Write-Host "   Server Time: $($reader['ServerTime'])" -ForegroundColor Cyan
    }
    $reader.Close()
    
    # List databases
    Write-Host ""
    Write-Host "   Available databases:" -ForegroundColor Yellow
    $dbQuery = "SELECT name FROM sys.databases ORDER BY name"
    $dbCommand = New-Object System.Data.SqlClient.SqlCommand($dbQuery, $connection)
    $dbReader = $dbCommand.ExecuteReader()
    while ($dbReader.Read()) {
        Write-Host "     - $($dbReader['name'])" -ForegroundColor White
    }
    $dbReader.Close()
    
    $connection.Close()
    Write-Host ""
    Write-Host "✅ Connection test completed successfully!" -ForegroundColor Green
    exit 0
    
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Method 2: Windows Integrated Security (requires running as that user)
Write-Host "Method 2: Windows Integrated Security..." -ForegroundColor Yellow
Write-Host "   Note: This requires running PowerShell as the specified user" -ForegroundColor Gray
Write-Host "   Attempting with current credentials..." -ForegroundColor Gray

try {
    $connectionString = "Server=$Server;Database=master;Integrated Security=true;Connection Timeout=10;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ SUCCESS: Windows Integrated Security works!" -ForegroundColor Green
    
    $query = "SELECT @@SERVERNAME AS ServerName, SYSTEM_USER AS CurrentUser"
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $reader = $command.ExecuteReader()
    if ($reader.Read()) {
        Write-Host "   Server Name: $($reader['ServerName'])" -ForegroundColor Cyan
        Write-Host "   Current User: $($reader['CurrentUser'])" -ForegroundColor Cyan
    }
    $reader.Close()
    
    $connection.Close()
    Write-Host ""
    Write-Host "✅ Connection test completed successfully!" -ForegroundColor Green
    exit 0
    
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   → This is expected if not running as the specified Windows user" -ForegroundColor Gray
}

Write-Host ""

# Method 3: Try with different connection string formats
Write-Host "Method 3: Testing alternative connection formats..." -ForegroundColor Yellow

$formats = @(
    @{ Name = "TCP with port"; String = "Server=tcp:$Server,1433;Database=master;User Id=$Username;Password=$Password;Connection Timeout=10;" },
    @{ Name = "Named instance"; String = "Server=$Server\MSSQLSERVER;Database=master;User Id=$Username;Password=$Password;Connection Timeout=10;" },
    @{ Name = "Trust Server Certificate"; String = "Server=$Server;Database=master;User Id=$Username;Password=$Password;TrustServerCertificate=true;Connection Timeout=10;" }
)

foreach ($format in $formats) {
    Write-Host "   Testing: $($format.Name)..." -ForegroundColor Gray
    try {
        $connection = New-Object System.Data.SqlClient.SqlConnection($format.String)
        $connection.Open()
        Write-Host "   ✅ SUCCESS: $($format.Name) works!" -ForegroundColor Green
        $connection.Close()
        exit 0
    } catch {
        Write-Host "   ❌ Failed: $($format.Name)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Network Connectivity Check" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Check network connectivity
$tcpTest = Test-NetConnection -ComputerName $Server -Port 1433 -WarningAction SilentlyContinue
if (-not $tcpTest.TcpTestSucceeded) {
    Write-Host "❌ Port 1433 is NOT accessible" -ForegroundColor Red
    Write-Host ""
    Write-Host "Network connectivity issues detected:" -ForegroundColor Yellow
    Write-Host "1. Server may not be reachable from this network" -ForegroundColor White
    Write-Host "2. Firewall may be blocking port 1433" -ForegroundColor White
    Write-Host "3. SQL Server may not be configured for remote connections" -ForegroundColor White
    Write-Host "4. SQL Server may be using a different port" -ForegroundColor White
    Write-Host ""
    Write-Host "To resolve:" -ForegroundColor Yellow
    Write-Host "- Verify SQL Server is running on 10.0.0.79" -ForegroundColor White
    Write-Host "- Check Windows Firewall on SQL Server machine" -ForegroundColor White
    Write-Host "- Verify SQL Server is configured to allow remote connections" -ForegroundColor White
    Write-Host "- Test connection from a machine on the same network segment" -ForegroundColor White
}

