# ================================================
# Replicate NS_CIS Instance to MSSQLSERVER Instance
# Complete Database Replication Script
# ================================================

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$BackupPath = "C:\Temp\DB_Backups",
    [switch]$VerifyOnly = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Replication Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Source Instance: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target Instance: $TargetInstance" -ForegroundColor Yellow
Write-Host "Databases: $($Databases -join ', ')" -ForegroundColor Yellow
Write-Host ""

# Create backup directory
if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Host "Created backup directory: $BackupPath" -ForegroundColor Green
}

# Function to execute SQL command and return output
function Invoke-SqlCommand {
    param(
        [string]$Instance,
        [string]$Database = "master",
        [string]$Query
    )
    
    $outputFile = [System.IO.Path]::GetTempFileName()
    $result = sqlcmd -S $Instance -d $Database -E -Q $Query -W -h -1 -o $outputFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        $errorContent = Get-Content $outputFile -Raw
        Remove-Item $outputFile -Force
        throw "SQL Error on $Instance`: $errorContent"
    }
    
    $content = Get-Content $outputFile -Raw
    Remove-Item $outputFile -Force
    return $content
}

# Function to get database logical file names
function Get-DatabaseFiles {
    param(
        [string]$Instance,
        [string]$DatabaseName
    )
    
    $query = @"
USE [$DatabaseName];
SELECT 
    name AS LogicalName,
    physical_name AS PhysicalName,
    type_desc AS FileType
FROM sys.database_files
ORDER BY type_desc, file_id;
"@
    
    $result = Invoke-SqlCommand -Instance $Instance -Database "master" -Query $query
    return $result
}

# Function to get default data and log paths
function Get-DefaultPaths {
    param(
        [string]$Instance
    )
    
    $dataQuery = "EXEC xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'DefaultData'"
    $logQuery = "EXEC xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'DefaultLog'"
    
    try {
        $dataPath = Invoke-SqlCommand -Instance $Instance -Database "master" -Query $dataQuery
        $logPath = Invoke-SqlCommand -Instance $Instance -Database "master" -Query $logQuery
        
        # Extract paths from registry read output
        $dataPath = ($dataPath -split "`n" | Where-Object { $_ -match 'Data' -or $_ -match 'REG_SZ' } | Select-Object -First 1) -replace '.*REG_SZ\s+', '' -replace '\s+', ''
        $logPath = ($logPath -split "`n" | Where-Object { $_ -match 'Log' -or $_ -match 'REG_SZ' } | Select-Object -First 1) -replace '.*REG_SZ\s+', '' -replace '\s+', ''
        
        # Fallback to default paths if registry read fails
        if ([string]::IsNullOrWhiteSpace($dataPath)) {
            if ($Instance -match "NS_CIS") {
                $dataPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.NS_CIS\MSSQL\DATA"
            } else {
                $dataPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA"
            }
        }
        
        if ([string]::IsNullOrWhiteSpace($logPath)) {
            $logPath = $dataPath
        }
        
        return @{
            DataPath = $dataPath
            LogPath = $logPath
        }
    } catch {
        # Use default paths
        if ($Instance -match "NS_CIS") {
            return @{
                DataPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.NS_CIS\MSSQL\DATA"
                LogPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.NS_CIS\MSSQL\DATA"
            }
        } else {
            return @{
                DataPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA"
                LogPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA"
            }
        }
    }
}

# Function to backup and restore database
function Backup-RestoreDatabase {
    param(
        [string]$SourceInstance,
        [string]$TargetInstance,
        [string]$DatabaseName
    )
    
    $backupFile = Join-Path $BackupPath "$DatabaseName.bak"
    
    Write-Host "  → Backing up $DatabaseName from $SourceInstance..." -ForegroundColor Cyan
    
    # Backup database
    $backupQuery = @"
BACKUP DATABASE [$DatabaseName] 
TO DISK = '$backupFile'
WITH FORMAT, INIT, COMPRESSION, STATS = 10;
"@
    
    try {
        Invoke-SqlCommand -Instance $SourceInstance -Database "master" -Query $backupQuery | Out-Null
        Write-Host "  ✓ Backup completed: $backupFile" -ForegroundColor Green
    } catch {
        Write-Error "Backup failed for $DatabaseName`: $_"
        return $false
    }
    
    Write-Host "  → Getting file information..." -ForegroundColor Cyan
    
    # Get source database files
    $sourceFiles = Get-DatabaseFiles -Instance $SourceInstance -DatabaseName $DatabaseName
    $targetPaths = Get-DefaultPaths -Instance $TargetInstance
    
    # Parse file information
    $fileMoves = @()
    $lines = $sourceFiles -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch '^---' -and $_ -notmatch 'rows affected' }
    
    foreach ($line in $lines) {
        if ($line -match '^\s*(\S+)\s+(\S+)\s+(ROWS|LOG)') {
            $logicalName = $matches[1]
            $fileType = $matches[3]
            
            if ($fileType -eq "ROWS") {
                $targetFile = Join-Path $targetPaths.DataPath "$DatabaseName.mdf"
            } else {
                $targetFile = Join-Path $targetPaths.LogPath "$DatabaseName.ldf"
            }
            
            $fileMoves += "MOVE '$logicalName' TO '$targetFile'"
        }
    }
    
    # If we couldn't parse files, use simple approach
    if ($fileMoves.Count -eq 0) {
        $fileMoves = @(
            "MOVE '${DatabaseName}' TO '$($targetPaths.DataPath)\${DatabaseName}.mdf'",
            "MOVE '${DatabaseName}_Log' TO '$($targetPaths.LogPath)\${DatabaseName}.ldf'"
        )
    }
    
    Write-Host "  → Restoring $DatabaseName to $TargetInstance..." -ForegroundColor Cyan
    
    # Check if database exists on target, drop if needed
    $checkQuery = "IF EXISTS (SELECT name FROM sys.databases WHERE name = '$DatabaseName') SELECT 'EXISTS' ELSE SELECT 'NOT_EXISTS'"
    $exists = Invoke-SqlCommand -Instance $TargetInstance -Database "master" -Query $checkQuery
    
    if ($exists -match "EXISTS") {
        Write-Host "  → Dropping existing database $DatabaseName on target..." -ForegroundColor Yellow
        $dropQuery = @"
ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$DatabaseName];
"@
        try {
            Invoke-SqlCommand -Instance $TargetInstance -Database "master" -Query $dropQuery | Out-Null
        } catch {
            Write-Warning "Could not drop database (may not exist): $_"
        }
    }
    
    # Restore database
    $moveClause = $fileMoves -join ",`n"
    $restoreQuery = @"
RESTORE DATABASE [$DatabaseName] 
FROM DISK = '$backupFile'
WITH REPLACE, STATS = 10,
$moveClause
"@
    
    try {
        Invoke-SqlCommand -Instance $TargetInstance -Database "master" -Query $restoreQuery | Out-Null
        Write-Host "  ✓ Restore completed" -ForegroundColor Green
        return $true
    } catch {
        Write-Error "Restore failed for $DatabaseName`: $_"
        return $false
    }
}

# Function to get table row counts for comparison
function Get-TableRowCounts {
    param(
        [string]$Instance,
        [string]$Database
    )
    
    $query = @"
USE [$Database];
SELECT 
    t.TABLE_SCHEMA,
    t.TABLE_NAME,
    SUM(p.rows) AS [RowCount]
FROM INFORMATION_SCHEMA.TABLES t
INNER JOIN sys.tables st ON st.name = t.TABLE_NAME 
    AND st.schema_id = SCHEMA_ID(t.TABLE_SCHEMA)
INNER JOIN sys.partitions p ON p.object_id = st.object_id
WHERE t.TABLE_TYPE = 'BASE TABLE' 
    AND p.index_id IN (0,1)
GROUP BY t.TABLE_SCHEMA, t.TABLE_NAME
ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME;
"@
    
    $result = Invoke-SqlCommand -Instance $Instance -Database "master" -Query $query
    return $result
}

# Main execution
Write-Host "Step 1: Verifying source databases..." -ForegroundColor Cyan
$validDatabases = @()
foreach ($db in $Databases) {
    $checkQuery = "IF EXISTS (SELECT name FROM sys.databases WHERE name = '$db') SELECT 'EXISTS' ELSE SELECT 'NOT_EXISTS'"
    try {
        $exists = Invoke-SqlCommand -Instance $SourceInstance -Database "master" -Query $checkQuery
        if ($exists -match "EXISTS") {
            Write-Host "  ✓ $db exists on source" -ForegroundColor Green
            $validDatabases += $db
        } else {
            Write-Warning "Database $db does not exist on source instance $SourceInstance"
        }
    } catch {
        Write-Warning "Could not verify database $db`: $_"
    }
}

if ($validDatabases.Count -eq 0) {
    Write-Error "No valid databases found to replicate"
    exit 1
}

if ($VerifyOnly) {
    Write-Host ""
    Write-Host "Step 2: Comparing table counts and row counts..." -ForegroundColor Cyan
    foreach ($db in $validDatabases) {
        Write-Host ""
        Write-Host "Database: $db" -ForegroundColor Yellow
        
        try {
            $sourceCounts = Get-TableRowCounts -Instance $SourceInstance -Database $db
            $targetCounts = Get-TableRowCounts -Instance $TargetInstance -Database $db
            
            $sourceLines = ($sourceCounts -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }).Count
            $targetLines = ($targetCounts -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }).Count
            
            Write-Host "  Source Tables: $sourceLines" -ForegroundColor $(if ($sourceLines -eq $targetLines) { "Green" } else { "Red" })
            Write-Host "  Target Tables: $targetLines" -ForegroundColor $(if ($sourceLines -eq $targetLines) { "Green" } else { "Red" })
        } catch {
            Write-Warning "Could not compare $db`: $_"
        }
    }
    exit 0
}

Write-Host ""
Write-Host "Step 2: Replicating databases..." -ForegroundColor Cyan
$successCount = 0
foreach ($db in $validDatabases) {
    Write-Host ""
    Write-Host "Processing: $db" -ForegroundColor Yellow
    
    $success = Backup-RestoreDatabase -SourceInstance $SourceInstance -TargetInstance $TargetInstance -DatabaseName $db
    if ($success) {
        $successCount++
    }
}

Write-Host ""
Write-Host "Step 3: Verification..." -ForegroundColor Cyan
foreach ($db in $validDatabases) {
    Write-Host ""
    Write-Host "Verifying: $db" -ForegroundColor Yellow
    
    try {
        $sourceCounts = Get-TableRowCounts -Instance $SourceInstance -Database $db
        $targetCounts = Get-TableRowCounts -Instance $TargetInstance -Database $db
        
        $sourceLines = ($sourceCounts -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }).Count
        $targetLines = ($targetCounts -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }).Count
        
        if ($sourceLines -eq $targetLines) {
            Write-Host "  ✓ Table counts match: $sourceLines tables" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ Table count mismatch: Source=$sourceLines, Target=$targetLines" -ForegroundColor Red
        }
    } catch {
        Write-Warning "Could not verify $db`: $_"
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Replication Complete!" -ForegroundColor Green
Write-Host "Successfully replicated: $successCount of $($validDatabases.Count) databases" -ForegroundColor $(if ($successCount -eq $validDatabases.Count) { "Green" } else { "Yellow" })
Write-Host "========================================" -ForegroundColor Cyan
