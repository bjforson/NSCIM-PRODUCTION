# Resync Incomplete Tables Script
# Resynchronizes tables that have data mismatches between source and target instances
# Uses the existing Transfer_Table_Simple.ps1 script for reliable data transfer

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "localhost",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [switch]$DryRun = $false,
    [switch]$SkipVerification = $false
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Resync Incomplete Tables" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host "Databases: $($Databases -join ', ')" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "Mode: DRY RUN (no changes will be made)" -ForegroundColor Magenta
}
Write-Host "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ""

# Get script directory
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$verifyScript = Join-Path $scriptDir "Verify-DatabaseReplication.ps1"
$transferScript = Join-Path $scriptDir "sql\Transfer_Table_Simple.ps1"

# Check if transfer script exists
if (-not (Test-Path $transferScript)) {
    Write-Host "❌ Transfer script not found: $transferScript" -ForegroundColor Red
    exit 1
}

# Step 1: Get list of incomplete tables
Write-Host "Step 1: Identifying incomplete tables..." -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Gray
Write-Host ""

# Known incomplete tables from last verification (2026-01-01)
$knownIncompleteTables = @(
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "AseScans"; FullName = "[dbo].[AseScans]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "AseSyncLogs"; FullName = "[dbo].[AseSyncLogs]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "ContainerBOERelations"; FullName = "[dbo].[ContainerBOERelations]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "ContainerCompletenessStatuses"; FullName = "[dbo].[ContainerCompletenessStatuses]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "EndpointUsageLog"; FullName = "[dbo].[EndpointUsageLog]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "FS6000Images"; FullName = "[dbo].[FS6000Images]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "FS6000Scans"; FullName = "[dbo].[FS6000Scans]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "FS6000SyncLogs"; FullName = "[dbo].[FS6000SyncLogs]" },
    @{ Database = "NS_CIS"; Schema = "dbo"; Table = "RolePermissions"; FullName = "[dbo].[RolePermissions]" },
    @{ Database = "ICUMS"; Schema = "dbo"; Table = "IcumManifestItems"; FullName = "[dbo].[IcumManifestItems]" },
    @{ Database = "ICUMS_Downloads"; Schema = "dbo"; Table = "BOEDocuments"; FullName = "[dbo].[BOEDocuments]" },
    @{ Database = "ICUMS_Downloads"; Schema = "dbo"; Table = "CMRValidationMetrics"; FullName = "[dbo].[CMRValidationMetrics]" },
    @{ Database = "ICUMS_Downloads"; Schema = "dbo"; Table = "DownloadedFiles"; FullName = "[dbo].[DownloadedFiles]" },
    @{ Database = "ICUMS_Downloads"; Schema = "dbo"; Table = "ManifestItems"; FullName = "[dbo].[ManifestItems]" },
    @{ Database = "ICUMS_Downloads"; Schema = "dbo"; Table = "VehicleImports"; FullName = "[dbo].[VehicleImports]" }
)

if (-not $SkipVerification) {
    Write-Host "Running verification to confirm incomplete tables..." -ForegroundColor Gray
    # Run verification script (ignore exit code - we know replication is incomplete)
    $verifyOutput = & powershell -ExecutionPolicy Bypass -File $verifyScript -SourceInstance $SourceInstance -TargetInstance $TargetInstance -Databases $Databases 2>&1 | Out-String
    
    # Try to parse and update the list, but use known list as fallback
    $parsedTables = @()
    $currentDatabase = ""
    $lines = $verifyOutput -split "`r?`n"
    
    foreach ($line in $lines) {
        if ($line -match 'DATABASE:\s+(\S+)') {
            $currentDatabase = $matches[1]
            continue
        }
        
        if ($line -match '\[dbo\]\.\[(\S+)\].*?(\d+)\s+(\d+)\s+(Incomplete|Schema Mismatch)') {
            $tableName = $matches[1]
            $parsedTables += @{
                Database = $currentDatabase
                Schema = "dbo"
                Table = $tableName
                FullName = "[dbo].[$tableName]"
            }
        }
    }
    
    # Use parsed tables if we found a reasonable number, otherwise use known list
    # (Verification might not capture all tables due to output formatting)
    if ($parsedTables.Count -ge 10) {
        Write-Host "Found $($parsedTables.Count) incomplete table(s) from verification" -ForegroundColor Gray
        $incompleteTables = $parsedTables
    } else {
        Write-Host "Verification found $($parsedTables.Count) tables, but we know there are $($knownIncompleteTables.Count)" -ForegroundColor Yellow
        Write-Host "Using known incomplete tables list ($($knownIncompleteTables.Count) tables)" -ForegroundColor Gray
        $incompleteTables = $knownIncompleteTables
    }
} else {
    Write-Host "Using known incomplete tables list (skip verification)" -ForegroundColor Gray
    $incompleteTables = $knownIncompleteTables
}

if ($incompleteTables.Count -eq 0) {
    Write-Host "✅ No incomplete tables found. Replication is complete!" -ForegroundColor Green
    exit 0
}

Write-Host "Found $($incompleteTables.Count) incomplete table(s) to resync:" -ForegroundColor Yellow
foreach ($tbl in $incompleteTables) {
    Write-Host "  - $($tbl.Database).[dbo].[$($tbl.Table)]" -ForegroundColor Gray
}
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN: Would resync the above tables." -ForegroundColor Magenta
    Write-Host "Run without -DryRun to perform actual resync." -ForegroundColor Magenta
    exit 0
}

# Confirm before proceeding
Write-Host "⚠️  WARNING: This will DELETE all data in the target tables and reload from source!" -ForegroundColor Yellow
Write-Host "Press Ctrl+C to cancel, or any key to continue..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host ""
Write-Host "Step 2: Resyncing tables..." -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Gray
Write-Host ""

$successCount = 0
$failCount = 0
$skippedCount = 0
$startTime = Get-Date

foreach ($tbl in $incompleteTables) {
    $index = $incompleteTables.IndexOf($tbl) + 1
    $total = $incompleteTables.Count
    
    Write-Host "[$index/$total] Resyncing $($tbl.Database).[dbo].[$($tbl.Table)]..." -ForegroundColor Cyan
    
    try {
        # Get row counts before transfer
        $sourceCountQuery = "SELECT COUNT(*) FROM [dbo].[$($tbl.Table)]"
        $sourceCountResult = sqlcmd -S $SourceInstance -E -d $tbl.Database -Q $sourceCountQuery -W -h -1 2>&1
        $sourceCount = ($sourceCountResult | Where-Object { $_ -match '^\s*(\d+)\s*$' } | ForEach-Object { $_.Trim() }) | Select-Object -First 1
        
        Write-Host "  Source rows: $sourceCount" -ForegroundColor Gray
        
        # Call transfer script
        $transferResult = & powershell -ExecutionPolicy Bypass -File $transferScript `
            -TableName $tbl.Table `
            -Schema $tbl.Schema `
            -Database $tbl.Database `
            -TargetInstance $TargetInstance `
            -SourceInstance $SourceInstance `
            2>&1
        
        if ($LASTEXITCODE -eq 0) {
            # Verify row count after transfer
            $targetCountQuery = "SELECT COUNT(*) FROM [dbo].[$($tbl.Table)]"
            $targetCountResult = sqlcmd -S $TargetInstance -E -d $tbl.Database -Q $targetCountQuery -W -h -1 2>&1
            $targetCount = ($targetCountResult | Where-Object { $_ -match '^\s*(\d+)\s*$' } | ForEach-Object { $_.Trim() }) | Select-Object -First 1
            
            if ($sourceCount -eq $targetCount) {
                Write-Host "  ✅ Success! Rows: $sourceCount → $targetCount" -ForegroundColor Green
                $successCount++
            } else {
                Write-Host "  ⚠️  Warning: Row count mismatch after transfer (Source: $sourceCount, Target: $targetCount)" -ForegroundColor Yellow
                $failCount++
            }
        } else {
            Write-Host "  ❌ Transfer failed!" -ForegroundColor Red
            Write-Host "  Error output:" -ForegroundColor Red
            $transferResult | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $failCount++
        }
    } catch {
        Write-Host "  ❌ Error: $_" -ForegroundColor Red
        $failCount++
    }
    
    Write-Host ""
}

$endTime = Get-Date
$duration = $endTime - $startTime

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RESYNC SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total tables processed: $($incompleteTables.Count)" -ForegroundColor Gray
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "Skipped: $skippedCount" -ForegroundColor Gray
Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "✅ All tables resynced successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Verifying final status..." -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File $verifyScript -SourceInstance $SourceInstance -TargetInstance $TargetInstance -Databases $Databases
    exit 0
} else {
    Write-Host "⚠️  Some tables failed to resync. Please review errors above." -ForegroundColor Yellow
    exit 1
}

