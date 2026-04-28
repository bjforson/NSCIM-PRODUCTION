# ================================================
# Simple Database Replication Script
# Replicates NS_CIS instance to MSSQLSERVER instance
# ================================================

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$BackupPath = "C:\Temp\DB_Backups"
)

# Continues past errors intentionally: iterates DB backup/restore per-DB; one DB's restore failure must not abort the others (script uses `continue` after $LASTEXITCODE checks).
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Replication: NS_CIS -> MSSQLSERVER" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Create backup directory
if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
}

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Gray
    
    $backupFile = Join-Path $BackupPath "$db.bak"
    
    # Step 1: Backup from source
    Write-Host "  [1/3] Backing up from $SourceInstance..." -ForegroundColor Cyan
    $backupQuery = "BACKUP DATABASE [$db] TO DISK = '$backupFile' WITH FORMAT, INIT, COMPRESSION, STATS = 10"
    $backupCmd = "sqlcmd -S `"$SourceInstance`" -E -Q `"$backupQuery`""
    
    Invoke-Expression $backupCmd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ Backup failed!" -ForegroundColor Red
        continue
    }
    Write-Host "  ✓ Backup completed" -ForegroundColor Green
    
    # Step 2: Get logical file names from backup
    Write-Host "  [2/3] Getting file information..." -ForegroundColor Cyan
    $fileListOutput = [System.IO.Path]::GetTempFileName()
    $fileListQuery = "RESTORE FILELISTONLY FROM DISK = '$backupFile'"
    $fileListCmd = "sqlcmd -S `"$TargetInstance`" -E -Q `"$fileListQuery`" -W -h -1 -o `"$fileListOutput`""
    Invoke-Expression $fileListCmd | Out-Null
    
    $fileList = Get-Content $fileListOutput -Raw
    Remove-Item $fileListOutput -Force
    
    # Parse file list to build MOVE clauses
    $moveClauses = @()
    $targetDataPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA"
    $targetLogPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA"
    
    $fileLines = $fileList -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    
    foreach ($line in $fileLines) {
        $parts = $line -split '\s+', 5
        if ($parts.Length -ge 3) {
            $logicalName = $parts[0]
            $fileType = $parts[2]
            
            if ($fileType -eq "D") {
                $targetFile = Join-Path $targetDataPath "$db.mdf"
            } elseif ($fileType -eq "L") {
                $targetFile = Join-Path $targetLogPath "$db.ldf"
            } else {
                continue
            }
            
            $moveClauses += "MOVE '$logicalName' TO '$targetFile'"
        }
    }
    
    # Fallback if parsing failed
    if ($moveClauses.Count -eq 0) {
        $moveClauses = @(
            "MOVE '$db' TO '$targetDataPath\$db.mdf'",
            "MOVE '${db}_Log' TO '$targetLogPath\$db.ldf'"
        )
    }
    
    $moveClause = $moveClauses -join ",`n"
    
    # Step 3: Drop existing database on target if exists
    Write-Host "  [3/3] Restoring to $TargetInstance..." -ForegroundColor Cyan
    $dropQuery = "IF EXISTS (SELECT name FROM sys.databases WHERE name = '$db') BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END"
    $dropCmd = "sqlcmd -S `"$TargetInstance`" -E -Q `"$dropQuery`""
    Invoke-Expression $dropCmd | Out-Null
    
    # Step 4: Restore to target
    $restoreQueryFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $restoreQuery = "RESTORE DATABASE [$db]`nFROM DISK = '$backupFile'`nWITH REPLACE, STATS = 10,`n$moveClause"
    $restoreQuery | Out-File -FilePath $restoreQueryFile -Encoding UTF8
    
    $restoreCmd = "sqlcmd -S `"$TargetInstance`" -E -i `"$restoreQueryFile`""
    Invoke-Expression $restoreCmd | Out-Null
    
    Remove-Item $restoreQueryFile -Force
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Restore completed successfully!" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Restore failed!" -ForegroundColor Red
    }
    
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Replication Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Verify data integrity" -ForegroundColor White
Write-Host "2. Update connection strings if needed" -ForegroundColor White
Write-Host ""
