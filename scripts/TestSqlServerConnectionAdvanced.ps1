# Test SQL Server Connection with multiple methods
param(
    [string]$Server = "10.0.0.79",
    [string]$Username = "administrator",
    [string]$Password = "Haoyunbeijing18$"
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Testing SQL Server Connection (Advanced)" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Basic connection with default port
Write-Host "Test 1: Basic connection (default port 1433)..." -ForegroundColor Yellow
try {
    $connectionString = "Server=$Server;Database=master;User Id=$Username;Password=$Password;Connection Timeout=5;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ SUCCESS: Basic connection works!" -ForegroundColor Green
    $connection.Close()
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 2: TCP/IP protocol explicitly
Write-Host "Test 2: TCP/IP protocol (port 1433)..." -ForegroundColor Yellow
try {
    $connectionString = "Server=tcp:$Server,1433;Database=master;User Id=$Username;Password=$Password;Connection Timeout=5;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ SUCCESS: TCP/IP connection works!" -ForegroundColor Green
    $connection.Close()
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 3: Try with default instance name
Write-Host "Test 3: Default instance (SERVER\SQLEXPRESS)..." -ForegroundColor Yellow
try {
    $connectionString = "Server=$Server\SQLEXPRESS;Database=master;User Id=$Username;Password=$Password;Connection Timeout=5;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ SUCCESS: SQLEXPRESS instance works!" -ForegroundColor Green
    $connection.Close()
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 4: Try with MSSQLSERVER instance
Write-Host "Test 4: Default instance (SERVER\MSSQLSERVER)..." -ForegroundColor Yellow
try {
    $connectionString = "Server=$Server\MSSQLSERVER;Database=master;User Id=$Username;Password=$Password;Connection Timeout=5;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ SUCCESS: MSSQLSERVER instance works!" -ForegroundColor Green
    $connection.Close()
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 5: Try different ports
Write-Host "Test 5: Testing common SQL Server ports..." -ForegroundColor Yellow
$ports = @(1433, 1434, 14330, 14331)
foreach ($port in $ports) {
    try {
        $connectionString = "Server=tcp:$Server,$port;Database=master;User Id=$Username;Password=$Password;Connection Timeout=3;"
        $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
        $connection.Open()
        Write-Host "✅ SUCCESS: Port $port works!" -ForegroundColor Green
        $connection.Close()
        break
    } catch {
        Write-Host "   Port $port failed" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Network Connectivity Test" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Test network connectivity
Write-Host "Testing network connectivity to $Server..." -ForegroundColor Yellow
$ping = Test-Connection -ComputerName $Server -Count 2 -Quiet
if ($ping) {
    Write-Host "✅ Server is reachable (ping successful)" -ForegroundColor Green
} else {
    Write-Host "❌ Server is NOT reachable (ping failed)" -ForegroundColor Red
    Write-Host "   → Check network connectivity, firewall, or server status" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Testing SQL Server port 1433..." -ForegroundColor Yellow
$tcpTest = Test-NetConnection -ComputerName $Server -Port 1433 -WarningAction SilentlyContinue
if ($tcpTest.TcpTestSucceeded) {
    Write-Host "✅ Port 1433 is open and accessible" -ForegroundColor Green
} else {
    Write-Host "❌ Port 1433 is NOT accessible" -ForegroundColor Red
    Write-Host "   → SQL Server may not be configured for remote connections" -ForegroundColor Yellow
    Write-Host "   → Firewall may be blocking the port" -ForegroundColor Yellow
    Write-Host "   → SQL Server may be using a different port" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If all tests failed, possible issues:" -ForegroundColor Yellow
Write-Host "1. SQL Server is not configured to allow remote connections" -ForegroundColor White
Write-Host "2. SQL Server Browser service is not running (for named instances)" -ForegroundColor White
Write-Host "3. Windows Firewall is blocking SQL Server ports" -ForegroundColor White
Write-Host "4. SQL Server is using a non-default port" -ForegroundColor White
Write-Host "5. SQL Server authentication is disabled (SQL auth vs Windows auth)" -ForegroundColor White
Write-Host "6. The server IP or credentials are incorrect" -ForegroundColor White

