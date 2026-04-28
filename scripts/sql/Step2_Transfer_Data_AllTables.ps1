# Transfer all data from NS_CIS instance to MSSQLSERVER using linked server

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

# Continues past errors intentionally: iterates every table across multiple DBs invoking linked-server INSERT; per-table errors are tallied and report must complete.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Data Transfer: NS_CIS -> MSSQLSERVER" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Gray
    
    # Get list of tables
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tablesFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    $tableCount = ($tables | Measure-Object -Line).Lines
    Remove-Item $tablesFile -Force
    
    Write-Host "  Found $tableCount tables to transfer" -ForegroundColor Gray
    Write-Host ""
    
    $successCount = 0
    $errorCount = 0
    $currentTable = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0]
            $table = $parts[1]
            $fullTableName = "[$schema].[$table]"
            $currentTable++
            
            Write-Host "  [$currentTable/$tableCount] Transferring $fullTableName..." -ForegroundColor Cyan -NoNewline
            
            # Check if table has identity column
            $identityCheck = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$db].$fullTableName')"
            $identityFile = [System.IO.Path]::GetTempFileName()
            sqlcmd -S $TargetInstance -E -d $db -Q $identityCheck -W -h -1 -o $identityFile | Out-Null
            $hasIdentity = (Get-Content $identityFile | Where-Object { $_ -match '^\s*[1-9]' }) -ne $null
            Remove-Item $identityFile -Force
            
            # Create transfer SQL script with IDENTITY_INSERT if needed
            if ($hasIdentity) {
                $transferSQL = "SET NOCOUNT ON;`n" +
                              "SET IDENTITY_INSERT [$db].$fullTableName ON;`n" +
                              "ALTER TABLE [$db].$fullTableName NOCHECK CONSTRAINT ALL;`n" +
                              "GO`n" +
                              "INSERT INTO [$db].$fullTableName SELECT * FROM [NS_CIS_SOURCE].[$db].$fullTableName;`n" +
                              "GO`n" +
                              "SET IDENTITY_INSERT [$db].$fullTableName OFF;`n" +
                              "ALTER TABLE [$db].$fullTableName CHECK CONSTRAINT ALL;`n" +
                              "GO"
            } else {
                $transferSQL = "SET NOCOUNT ON;`n" +
                              "ALTER TABLE [$db].$fullTableName NOCHECK CONSTRAINT ALL;`n" +
                              "GO`n" +
                              "INSERT INTO [$db].$fullTableName SELECT * FROM [NS_CIS_SOURCE].[$db].$fullTableName;`n" +
                              "GO`n" +
                              "ALTER TABLE [$db].$fullTableName CHECK CONSTRAINT ALL;`n" +
                              "GO"
            }
            
            $transferFile = [System.IO.Path]::GetTempFileName() + ".sql"
            $transferSQL | Out-File -FilePath $transferFile -Encoding UTF8
            
            $result = sqlcmd -S $TargetInstance -E -d $db -i $transferFile -b 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host " ✓" -ForegroundColor Green
                $successCount++
            } else {
                Write-Host " ✗" -ForegroundColor Red
                Write-Host "    Error: $($result -join ' ')" -ForegroundColor Red
                $errorCount++
            }
            
            Remove-Item $transferFile -Force
        }
    }
    
    Write-Host ""
    Write-Host "  Summary: $successCount successful, $errorCount errors" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Yellow" })
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Data Transfer Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

