# Test database connection strings from appsettings.json
param(
    [string[]]$ConnectionStrings = @(
        "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;Encrypt=true;TrustServerCertificate=true;Connection Timeout=10",
        "Server=127.0.0.1,1433;Database=ICUMS;Trusted_Connection=true;Encrypt=true;TrustServerCertificate=true;Connection Timeout=10",
        "Server=127.0.0.1,1433;Database=ICUMS_Downloads;Trusted_Connection=true;Encrypt=true;TrustServerCertificate=true;Connection Timeout=10"
    )
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Database Connections" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Add-Type -AssemblyName System.Data

$dbNames = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
$successCount = 0
$failCount = 0

for ($i = 0; $i -lt $ConnectionStrings.Length; $i++) {
    $connString = $ConnectionStrings[$i]
    $dbName = $dbNames[$i]
    
    Write-Host "Testing connection to $dbName..." -NoNewline -ForegroundColor Yellow
    
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($connString)
        $conn.Open()
        Write-Host " SUCCESS" -ForegroundColor Green
        $conn.Close()
        $successCount++
    } catch {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary: $successCount/$($ConnectionStrings.Length) connections successful" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Yellow" })
Write-Host "========================================" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}

