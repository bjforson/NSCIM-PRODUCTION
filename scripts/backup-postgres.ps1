$ErrorActionPreference = "Stop"

$pgDump = "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
$backupRoot = "D:\Backups\PostgreSQL"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$databases = @("nickscan_production", "nickscan_icums", "nickscan_downloads")
$retainDays = 30

$env:PGPASSWORD = [System.Environment]::GetEnvironmentVariable('NICKSCAN_DB_PASSWORD', 'Machine')
if (-not $env:PGPASSWORD) {
    $env:PGPASSWORD = 'Haoyunbeijing18@$%'
}

$logFile = Join-Path $backupRoot "backup_$timestamp.log"

function Write-Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $msg"
    Write-Output $line
    Add-Content -Path $logFile -Value $line
}

Write-Log "=== PostgreSQL Backup Started ==="

foreach ($db in $databases) {
    $outFile = Join-Path $backupRoot "${db}_${timestamp}.sql.gz"
    Write-Log "Backing up $db to $outFile ..."

    try {
        $tempFile = Join-Path $backupRoot "${db}_${timestamp}.sql"
        & $pgDump -h localhost -U postgres -d $db -F plain -f $tempFile 2>&1
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed for $db with exit code $LASTEXITCODE" }

        Compress-Archive -Path $tempFile -DestinationPath "$tempFile.zip" -Force
        Remove-Item $tempFile -Force
        Rename-Item "$tempFile.zip" $outFile -Force

        $sizeMB = [math]::Round((Get-Item $outFile).Length / 1MB, 2)
        Write-Log "  SUCCESS: $db backed up ($sizeMB MB)"
    }
    catch {
        Write-Log "  FAILED: $db - $_"
    }
}

Write-Log "Cleaning up backups older than $retainDays days..."
$cutoff = (Get-Date).AddDays(-$retainDays)
Get-ChildItem $backupRoot -File | Where-Object { $_.LastWriteTime -lt $cutoff } | ForEach-Object {
    Write-Log "  Removing old backup: $($_.Name)"
    Remove-Item $_.FullName -Force
}

Write-Log "=== PostgreSQL Backup Completed ==="
$env:PGPASSWORD = $null
