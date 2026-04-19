# Comprehensive Database Sync Status Check
# Compares row counts between source (localhost\NS_CIS) and target ((local)) instances
# For databases: NS_CIS, ICUMS, ICUMS_Downloads

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$OutputFile = "sync_status_report.txt"
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Sync Status Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host "Databases: $($Databases -join ', ')" -ForegroundColor Yellow
Write-Host ""

# Initialize results collection
$allResults = @()

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Cyan
    Write-Host ("-" * 60) -ForegroundColor Gray
    
    # Get list of tables from source
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    
    $tablesFile = [System.IO.Path]::GetTempFileName()
    $result = sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Error: Could not connect to source database $db" -ForegroundColor Red
        Write-Host "  $result" -ForegroundColor Red
        Remove-Item $tablesFile -Force -ErrorAction SilentlyContinue
        continue
    }
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $tablesFile -Force
    
    if (-not $tables -or $tables.Count -eq 0) {
        Write-Host "  No tables found (excluding system tables)" -ForegroundColor Yellow
        Write-Host ""
        continue
    }
    
    Write-Host "  Found $($tables.Count) tables" -ForegroundColor Gray
    Write-Host ""
    
    $dbResults = @()
    $tableIndex = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0].Trim()
            $table = $parts[1].Trim()
            $fullTableName = "[$schema].[$table]"
            $tableIndex++
            
            Write-Host "  [$tableIndex/$($tables.Count)] Checking $fullTableName..." -NoNewline -ForegroundColor Gray
            
            $result = [PSCustomObject]@{
                Database = $db
                Schema = $schema
                Table = $table
                FullTableName = $fullTableName
                SourceCount = $null
                TargetCount = $null
                Status = "Unknown"
                ExistsOnSource = $false
                ExistsOnTarget = $false
                Error = $null
            }
            
            # Check source count
            try {
                $sourceQuery = "SELECT COUNT(*) FROM [$schema].[$table]"
                $sourceCountFile = [System.IO.Path]::GetTempFileName()
                $sourceResult = sqlcmd -S $SourceInstance -E -d $db -Q $sourceQuery -W -h -1 -o $sourceCountFile 2>&1
                
                if ($LASTEXITCODE -eq 0) {
                    $sourceCountStr = (Get-Content $sourceCountFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
                    if ($sourceCountStr) {
                        $result.SourceCount = [long]$sourceCountStr.Trim()
                        $result.ExistsOnSource = $true
                    }
                } else {
                    $result.Error = "Source query failed: $sourceResult"
                }
                Remove-Item $sourceCountFile -Force -ErrorAction SilentlyContinue
            } catch {
                $result.Error = "Source error: $_"
            }
            
            # Check target count
            try {
                $targetQuery = "SELECT COUNT(*) FROM [$schema].[$table]"
                $targetCountFile = [System.IO.Path]::GetTempFileName()
                $targetResult = sqlcmd -S $TargetInstance -E -d $db -Q $targetQuery -W -h -1 -o $targetCountFile 2>&1
                
                if ($LASTEXITCODE -eq 0) {
                    $targetCountStr = (Get-Content $targetCountFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
                    if ($targetCountStr) {
                        $result.TargetCount = [long]$targetCountStr.Trim()
                        $result.ExistsOnTarget = $true
                    }
                } else {
                    # Table might not exist on target
                    $result.TargetCount = $null
                    $result.ExistsOnTarget = $false
                }
                Remove-Item $targetCountFile -Force -ErrorAction SilentlyContinue
            } catch {
                $result.ExistsOnTarget = $false
            }
            
            # Determine status
            if (-not $result.ExistsOnSource) {
                $result.Status = "SourceError"
                Write-Host " Source Error" -ForegroundColor Red
            } elseif (-not $result.ExistsOnTarget) {
                $result.Status = "Missing"
                Write-Host " Missing" -ForegroundColor Red
            } elseif ($result.SourceCount -eq $null -or $result.TargetCount -eq $null) {
                $result.Status = "Error"
                Write-Host " Error" -ForegroundColor Red
            } elseif ($result.SourceCount -eq $result.TargetCount) {
                $result.Status = "Complete"
                Write-Host " Complete ($($result.SourceCount) rows)" -ForegroundColor Green
            } elseif ($result.TargetCount -eq 0) {
                $result.Status = "Empty"
                Write-Host " Empty ($($result.SourceCount) source)" -ForegroundColor Yellow
            } else {
                $result.Status = "Incomplete"
                Write-Host " Incomplete (Source: $($result.SourceCount), Target: $($result.TargetCount))" -ForegroundColor Yellow
            }
            
            $dbResults += $result
        }
    }
    
    $allResults += $dbResults
    
    # Database summary
    $complete = ($dbResults | Where-Object { $_.Status -eq "Complete" }).Count
    $missing = ($dbResults | Where-Object { $_.Status -eq "Missing" }).Count
    $incomplete = ($dbResults | Where-Object { $_.Status -eq "Incomplete" }).Count
    $empty = ($dbResults | Where-Object { $_.Status -eq "Empty" }).Count
    $errors = ($dbResults | Where-Object { $_.Status -match "Error" }).Count
    
    Write-Host ""
    Write-Host "  Summary for $db :" -ForegroundColor Yellow
    Write-Host "    Complete:   $complete" -ForegroundColor Green
    Write-Host "    Missing:    $missing" -ForegroundColor Red
    Write-Host "    Incomplete: $incomplete" -ForegroundColor Yellow
    Write-Host "    Empty:      $empty" -ForegroundColor Yellow
    Write-Host "    Errors:     $errors" -ForegroundColor Red
    Write-Host ""
}

# Overall summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Overall Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$totalTables = $allResults.Count
$totalComplete = ($allResults | Where-Object { $_.Status -eq "Complete" }).Count
$totalMissing = ($allResults | Where-Object { $_.Status -eq "Missing" }).Count
$totalIncomplete = ($allResults | Where-Object { $_.Status -eq "Incomplete" }).Count
$totalEmpty = ($allResults | Where-Object { $_.Status -eq "Empty" }).Count
$totalErrors = ($allResults | Where-Object { $_.Status -match "Error" }).Count
$totalNeedsTransfer = $totalMissing + $totalIncomplete + $totalEmpty

Write-Host "Total Tables:      $totalTables" -ForegroundColor White
Write-Host "Complete:          $totalComplete" -ForegroundColor Green
Write-Host "Needs Transfer:    $totalNeedsTransfer" -ForegroundColor $(if ($totalNeedsTransfer -eq 0) { "Green" } else { "Yellow" })
Write-Host "  - Missing:       $totalMissing" -ForegroundColor Red
Write-Host "  - Incomplete:    $totalIncomplete" -ForegroundColor Yellow
Write-Host "  - Empty:         $totalEmpty" -ForegroundColor Yellow
Write-Host "Errors:            $totalErrors" -ForegroundColor $(if ($totalErrors -eq 0) { "Green" } else { "Red" })
Write-Host ""

# Export detailed results to file
$reportContent = @"
========================================
Database Sync Status Report
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
========================================
Source Instance: $SourceInstance
Target Instance: $TargetInstance
Databases Checked: $($Databases -join ', ')

OVERALL SUMMARY
========================================
Total Tables:      $totalTables
Complete:          $totalComplete
Needs Transfer:    $totalNeedsTransfer
  - Missing:       $totalMissing
  - Incomplete:    $totalIncomplete
  - Empty:         $totalEmpty
Errors:            $totalErrors

DETAILED RESULTS
========================================

"@

foreach ($db in $Databases) {
    $dbResults = $allResults | Where-Object { $_.Database -eq $db }
    if ($dbResults.Count -eq 0) { continue }
    
    $reportContent += "`nDATABASE: $db`n"
    $reportContent += ("=" * 60) + "`n"
    
    $reportContent += "{0,-30} {1,15} {2,15} {3,-12} {4}`n" -f "Table", "Source Count", "Target Count", "Status", "Error"
    $reportContent += ("-" * 90) + "`n"
    
    foreach ($result in $dbResults) {
        $sourceStr = if ($result.SourceCount -ne $null) { $result.SourceCount.ToString() } else { "N/A" }
        $targetStr = if ($result.TargetCount -ne $null) { $result.TargetCount.ToString() } else { "N/A" }
        $errorStr = if ($result.Error) { $result.Error } else { "" }
        $reportContent += "{0,-30} {1,15} {2,15} {3,-12} {4}`n" -f $result.FullTableName, $sourceStr, $targetStr, $result.Status, $errorStr
    }
    
    $reportContent += "`n"
}

# Tables that need transfer
$needsTransfer = $allResults | Where-Object { $_.Status -in @("Missing", "Incomplete", "Empty") } | Sort-Object Database, Schema, Table

if ($needsTransfer.Count -gt 0) {
    $reportContent += "`nTABLES REQUIRING TRANSFER`n"
    $reportContent += ("=" * 60) + "`n"
    foreach ($result in $needsTransfer) {
        $reportContent += "$($result.Database).[$($result.Schema)].[$($result.Table)] - Status: $($result.Status) (Source: $($result.SourceCount), Target: $($result.TargetCount))`n"
    }
}

$reportContent | Out-File -FilePath $OutputFile -Encoding UTF8

Write-Host "Detailed report saved to: $OutputFile" -ForegroundColor Cyan
Write-Host ""

# Export results as CSV for easy processing
$csvFile = $OutputFile -replace '\.txt$', '.csv'
$allResults | Export-Csv -Path $csvFile -NoTypeInformation -Encoding UTF8
Write-Host "CSV export saved to: $csvFile" -ForegroundColor Cyan
Write-Host ""

# Return results for further processing
return $allResults

