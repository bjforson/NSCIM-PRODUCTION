# Transfer Remaining Tables Script
# Generated: 2025-12-31 06:23:44
# This script transfers all tables that are missing, incomplete, or empty
# Based on sync status check results

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)"
)

# Continues past errors intentionally: generated runner that transfers ~21 tables one-by-one; per-table errors are tallied in $errorCount and run must complete the rest.
$ErrorActionPreference = "Continue"

# Get the script directory for relative paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transferring Remaining Tables" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Based on sync status check" -ForegroundColor Gray
Write-Host ""

$totalTables = 21
$currentTable = 0
$successCount = 0
$errorCount = 0

# Transfer: [ICUMS].[dbo].[IcumBatchLogs]
# Status: Empty | Source: 121 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS].[dbo].[IcumBatchLogs]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "IcumBatchLogs" -Schema "dbo" -Database "ICUMS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS].[dbo].[IcumContainerData]
# Status: Empty | Source: 50099 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS].[dbo].[IcumContainerData]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "IcumContainerData" -Schema "dbo" -Database "ICUMS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS].[dbo].[IcumManifestItems]
# Status: Missing | Source: 5555863 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS].[dbo].[IcumManifestItems]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "IcumManifestItems" -Schema "dbo" -Database "ICUMS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[BOEDocuments]
# Status: Empty | Source: 219989 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[BOEDocuments]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "BOEDocuments" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[CMRValidationMetrics]
# Status: Empty | Source: 1859 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[CMRValidationMetrics]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "CMRValidationMetrics" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[DownloadedFiles]
# Status: Empty | Source: 49016 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[DownloadedFiles]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "DownloadedFiles" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[ICUMSDownloadAudit]
# Status: Missing | Source: 115003 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[ICUMSDownloadAudit]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ICUMSDownloadAudit" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[ICUMSDownloadQueue]
# Status: Empty | Source: 279 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[ICUMSDownloadQueue]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ICUMSDownloadQueue" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[ICUMSDownloadQueueArchive]
# Status: Missing | Source: 10688 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[ICUMSDownloadQueueArchive]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ICUMSDownloadQueueArchive" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[ManifestItems]
# Status: Empty | Source: 10261511 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[ManifestItems]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ManifestItems" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [ICUMS_Downloads].[dbo].[VehicleImports]
# Status: Empty | Source: 8895 rows | Target: 0 rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [ICUMS_Downloads].[dbo].[VehicleImports]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "VehicleImports" -Schema "dbo" -Database "ICUMS_Downloads" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[ApplicationLogs]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[ApplicationLogs]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ApplicationLogs" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[ContainerReferences]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[ContainerReferences]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ContainerReferences" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[ErrorInvestigations]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[ErrorInvestigations]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ErrorInvestigations" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[FileSystemCaches]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[FileSystemCaches]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "FileSystemCaches" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[FixAuditLogs]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[FixAuditLogs]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "FixAuditLogs" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[FixProposals]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[FixProposals]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "FixProposals" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[ImageAnalysisDecisions_Backup_20251029]
# Status: Missing | Source: 14 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[ImageAnalysisDecisions_Backup_20251029]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ImageAnalysisDecisions_Backup_20251029" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[ServiceConfigurations]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[ServiceConfigurations]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "ServiceConfigurations" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[SystemStates]
# Status: Missing | Source: 0 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[SystemStates]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "SystemStates" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
# Transfer: [NS_CIS].[dbo].[UserReadiness]
# Status: Missing | Source: 5 rows | Target:  rows
$currentTable++
Write-Host "[$currentTable/$totalTables] Transferring [NS_CIS].[dbo].[UserReadiness]..." -ForegroundColor Cyan

$result = & "$scriptDir\Transfer_Table_Simple.ps1" -TableName "UserReadiness" -Schema "dbo" -Database "NS_CIS" -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    $successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    $errorCount++
    Write-Host $result -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transfer Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Successful: $successCount" -ForegroundColor Yellow
Write-Host "Errors: $errorCount" -ForegroundColor Red
Write-Host ""

if ($errorCount -gt 0) {
    Write-Host "Some tables failed to transfer. Please review the errors above." -ForegroundColor Yellow
    Write-Host "You can re-run this script to retry failed transfers." -ForegroundColor Yellow
}
