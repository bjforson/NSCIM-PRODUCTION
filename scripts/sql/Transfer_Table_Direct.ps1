# Direct table transfer script - uses BCP for reliable bulk data transfer
# No linked server required - uses direct connections to both instances

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS",
    [switch]$TruncateFirst,
    [string]$ProgressLogFile = "transfer_progress.log"
)

# Continues past errors intentionally: invoked per-table by orchestrator scripts that need it to report failures via $LASTEXITCODE rather than abort the parent loop; internal try/catch handles fatal cases.
$ErrorActionPreference = "Continue"

$fullTableName = "[$Schema].[$TableName]"
Write-Host "Transferring: [$Database].$fullTableName" -ForegroundColor Cyan
Write-Host "  Source: $SourceInstance" -ForegroundColor Gray
Write-Host "  Target: $TargetInstance" -ForegroundColor Gray

# Log progress
function Write-ProgressLog {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Database].[$Schema].[$TableName] $Message"
    Add-Content -Path $ProgressLogFile -Value $logMessage -ErrorAction SilentlyContinue
}

try {
    # Get column list from target
    Write-ProgressLog "Getting column list..."
    $columnsQuery = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName' ORDER BY ORDINAL_POSITION"
    $columnsFile = [System.IO.Path]::GetTempFileName()
    
    $result = sqlcmd -S $TargetInstance -E -d $Database -Q $columnsQuery -W -h -1 -o $columnsFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $columnsFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get column list: $result"
    }
    
    $columns = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $columnsFile -Force
    
    if (-not $columns -or $columns.Count -eq 0) {
        throw "No columns found for table $Schema.$TableName"
    }
    
    $columnList = ($columns | ForEach-Object { "[$_]" }) -join ", "
    Write-ProgressLog "Found $($columns.Count) columns"
    
    # Check for identity column
    $identityQuery = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]')"
    $identityFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $TargetInstance -E -d $Database -Q $identityQuery -W -h -1 -o $identityFile 2>&1 | Out-Null
    $hasIdentity = (Get-Content $identityFile | Where-Object { $_ -match '^\s*[1-9]' }) -ne $null
    Remove-Item $identityFile -Force
    
    if ($hasIdentity) {
        Write-ProgressLog "Table has identity column"
    }
    
    # Get source row count
    Write-ProgressLog "Getting source row count..."
    $sourceCountQuery = "SELECT COUNT(*) FROM [$Schema].[$TableName]"
    $sourceCountFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $Database -Q $sourceCountQuery -W -h -1 -o $sourceCountFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $sourceCountFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get source row count"
    }
    
    $sourceCountStr = (Get-Content $sourceCountFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
    Remove-Item $sourceCountFile -Force
    $sourceCount = if ($sourceCountStr) { [long]$sourceCountStr.Trim() } else { 0 }
    
    Write-ProgressLog "Source has $sourceCount rows"
    Write-Host "  Source rows: $sourceCount" -ForegroundColor Gray
    
    # Get target row count
    $targetCountQuery = "SELECT COUNT(*) FROM [$Schema].[$TableName]"
    $targetCountFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $TargetInstance -E -d $Database -Q $targetCountQuery -W -h -1 -o $targetCountFile 2>&1 | Out-Null
    
    $targetCount = 0
    if ($LASTEXITCODE -eq 0) {
        $targetCountStr = (Get-Content $targetCountFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
        $targetCount = if ($targetCountStr) { [long]$targetCountStr.Trim() } else { 0 }
    }
    Remove-Item $targetCountFile -Force -ErrorAction SilentlyContinue
    
    Write-Host "  Target rows: $targetCount" -ForegroundColor Gray
    
    # If target already has all rows, skip
    if ($targetCount -ge $sourceCount -and $sourceCount -gt 0) {
        Write-ProgressLog "Table already complete - skipping"
        Write-Host "  ✓ Table already complete" -ForegroundColor Green
        return
    }
    
    # Prepare temp file
    $tempDataFile = Join-Path $env:TEMP "bcp_transfer_$([Guid]::NewGuid().ToString('N')).dat"
    
    try {
        # Step 1: Prepare target table
        Write-ProgressLog "Preparing target table..."
        $prepSql = @"
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [$Database].[$Schema].[$TableName] NOCHECK CONSTRAINT ALL;
"@
        
        if ($TruncateFirst -and $targetCount -gt 0) {
            $prepSql += "TRUNCATE TABLE [$Database].[$Schema].[$TableName];`n"
            Write-ProgressLog "Truncating target table first"
        }
        
        if ($hasIdentity) {
            $prepSql += "SET IDENTITY_INSERT [$Database].[$Schema].[$TableName] ON;`n"
        }
        
        $prepFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $prepSql | Out-File -FilePath $prepFile -Encoding UTF8
        
        $prepResult = sqlcmd -S $TargetInstance -E -d $Database -i $prepFile -b 2>&1
        Remove-Item $prepFile -Force
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to prepare target table: $prepResult"
        }
        
        # Step 2: Export from source using BCP
        Write-ProgressLog "Exporting data from source using BCP..."
        Write-Host "  Exporting from source..." -NoNewline -ForegroundColor Gray
        
        # Build BCP query
        $bcpQuery = "SELECT $columnList FROM [$Database].[$Schema].[$TableName]"
        
        # Use BCP to export
        $bcpExportArgs = @(
            "`"$bcpQuery`"",
            "queryout",
            "`"$tempDataFile`"",
            "-S", "`"$SourceInstance`"",
            "-T",  # Trusted connection
            "-c",  # Character format
            "-t", "`",`"",  # Field terminator
            "-r", "`"\n`"",  # Row terminator
            "-d", "`"$Database`"",
            "-b", "10000"  # Batch size for BCP
        )
        
        $bcpExportProcess = Start-Process -FilePath "bcp" -ArgumentList $bcpExportArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput ([System.IO.Path]::GetTempFileName()) -RedirectStandardError ([System.IO.Path]::GetTempFileName())
        
        if ($bcpExportProcess.ExitCode -ne 0) {
            throw "BCP export failed with exit code $($bcpExportProcess.ExitCode)"
        }
        
        $fileSize = (Get-Item $tempDataFile -ErrorAction SilentlyContinue).Length
        Write-Host " ✓ ($([Math]::Round($fileSize / 1MB, 2)) MB)" -ForegroundColor Green
        Write-ProgressLog "Export complete. File size: $fileSize bytes"
        
        # Step 3: Import to target using BCP
        Write-ProgressLog "Importing data to target using BCP..."
        Write-Host "  Importing to target..." -NoNewline -ForegroundColor Gray
        
        $bcpImportArgs = @(
            "`"[$Database].[$Schema].[$TableName]`"",
            "in",
            "`"$tempDataFile`"",
            "-S", "`"$TargetInstance`"",
            "-T",  # Trusted connection
            "-c",  # Character format
            "-t", "`",`"",  # Field terminator
            "-r", "`"\n`"",  # Row terminator
            "-d", "`"$Database`"",
            "-b", "10000",  # Batch size
            "-F", "2"  # First row to import (skip header if any)
        )
        
        $bcpImportProcess = Start-Process -FilePath "bcp" -ArgumentList $bcpImportArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput ([System.IO.Path]::GetTempFileName()) -RedirectStandardError ([System.IO.Path]::GetTempFileName())
        
        if ($bcpImportProcess.ExitCode -ne 0) {
            throw "BCP import failed with exit code $($bcpImportProcess.ExitCode)"
        }
        
        Write-Host " ✓" -ForegroundColor Green
        Write-ProgressLog "Import complete"
        
        # Step 4: Finalize target table
        Write-ProgressLog "Finalizing target table..."
        $finalizeSql = @"
SET QUOTED_IDENTIFIER ON;
"@
        
        if ($hasIdentity) {
            $finalizeSql += "SET IDENTITY_INSERT [$Database].[$Schema].[$TableName] OFF;`n"
        }
        
        $finalizeSql += "ALTER TABLE [$Database].[$Schema].[$TableName] CHECK CONSTRAINT ALL;"
        
        $finalizeFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $finalizeSql | Out-File -FilePath $finalizeFile -Encoding UTF8
        
        $finalizeResult = sqlcmd -S $TargetInstance -E -d $Database -i $finalizeFile -b 2>&1
        Remove-Item $finalizeFile -Force
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to finalize target table: $finalizeResult"
        }
        
        # Step 5: Verify
        Write-ProgressLog "Verifying transfer..."
        $verifyCountFile = [System.IO.Path]::GetTempFileName()
        sqlcmd -S $TargetInstance -E -d $Database -Q $targetCountQuery -W -h -1 -o $verifyCountFile 2>&1 | Out-Null
        
        $verifyCountStr = (Get-Content $verifyCountFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
        Remove-Item $verifyCountFile -Force
        $verifyCount = if ($verifyCountStr) { [long]$verifyCountStr.Trim() } else { 0 }
        
        Write-ProgressLog "Transfer complete. Final row count: $verifyCount"
        
        if ($verifyCount -eq $sourceCount) {
            Write-Host "  ✓ Success! Transferred $verifyCount rows" -ForegroundColor Green
        } elseif ($verifyCount -gt 0) {
            Write-Host "  ⚠ Partial: $verifyCount of $sourceCount rows transferred" -ForegroundColor Yellow
        } else {
            Write-Host "  ✗ Warning: No rows transferred" -ForegroundColor Red
        }
        
    } finally {
        # Cleanup temp file
        if (Test-Path $tempDataFile) {
            Remove-Item $tempDataFile -Force -ErrorAction SilentlyContinue
            Write-ProgressLog "Cleaned up temp file"
        }
    }
    
} catch {
    Write-ProgressLog "ERROR: $_"
    Write-Host "  ✗ Failed: $_" -ForegroundColor Red
    if ($_.Exception.Message) {
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    }
    exit 1
}

