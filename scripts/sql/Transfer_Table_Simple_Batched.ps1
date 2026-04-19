# Batched table transfer script - transfers large tables in batches to avoid timeouts
# Uses SqlBulkCopy with OFFSET/FETCH batching for reliable data transfer
# Automatically determines batch size based on table size

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS",
    [int]$BatchSize = 5000  # Default batch size (rows per batch)
)

$ErrorActionPreference = "Stop"

Write-Host "Transferring: [$Schema].[$TableName] (Batched Mode)" -ForegroundColor Cyan

# Get column list
$columnsFile = [System.IO.Path]::GetTempFileName()
$query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName' ORDER BY ORDINAL_POSITION"
sqlcmd -S $TargetInstance -E -d $Database -Q $query -W -h -1 -o $columnsFile | Out-Null

$columns = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
Remove-Item $columnsFile -Force

$columnList = ($columns | ForEach-Object { "[$_]" }) -join ", "

# Check for identity column
$identityFile = [System.IO.Path]::GetTempFileName()
$identityQuery = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]')"
sqlcmd -S $TargetInstance -E -d $Database -Q $identityQuery -W -h -1 -o $identityFile | Out-Null
$hasIdentity = (Get-Content $identityFile | Where-Object { $_ -match '^\s*[1-9]' }) -ne $null
Remove-Item $identityFile -Force

# Get primary key column for ordering (prefer identity column, else first column)
$pkFile = [System.IO.Path]::GetTempFileName()
$pkQuery = @"
SELECT TOP 1 c.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE c ON tc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
WHERE tc.TABLE_SCHEMA = '$Schema' AND tc.TABLE_NAME = '$TableName' AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
ORDER BY c.ORDINAL_POSITION
"@
sqlcmd -S $SourceInstance -E -d $Database -Q $pkQuery -W -h -1 -o $pkFile | Out-Null
$pkColumn = (Get-Content $pkFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }) | Select-Object -First 1
Remove-Item $pkFile -Force

# If no PK found, check for identity column, else use first column
if (-not $pkColumn) {
    $identityColFile = [System.IO.Path]::GetTempFileName()
    $identityColQuery = "SELECT COLUMN_NAME FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Schema].[$TableName]')"
    sqlcmd -S $SourceInstance -E -d $Database -Q $identityColQuery -W -h -1 -o $identityColFile | Out-Null
    $pkColumn = (Get-Content $identityColFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }) | Select-Object -First 1
    Remove-Item $identityColFile -Force
}

if (-not $pkColumn) {
    $pkColumn = $columns[0]  # Use first column as fallback
}

# Get total row count
$countFile = [System.IO.Path]::GetTempFileName()
$countQuery = "SELECT COUNT(*) FROM [$Schema].[$TableName]"
sqlcmd -S $SourceInstance -E -d $Database -Q $countQuery -W -h -1 -o $countFile | Out-Null
$totalRows = [int]((Get-Content $countFile | Where-Object { $_ -match '^\s*\d+\s*$' }) | Select-Object -First 1)
Remove-Item $countFile -Force

Write-Host "  Total rows: $totalRows" -ForegroundColor Gray
Write-Host "  Batch size: $BatchSize rows" -ForegroundColor Gray
$totalBatches = [Math]::Ceiling($totalRows / $BatchSize)
Write-Host "  Total batches: $totalBatches" -ForegroundColor Gray

$transferSuccess = $false
$errorMessage = ""

try {
    # Prepare target table - disable constraints first, then clear data
    $prepSql = "SET QUOTED_IDENTIFIER ON;`nALTER TABLE [$Database].[$Schema].[$TableName] NOCHECK CONSTRAINT ALL;`n"
    if ($hasIdentity) {
        $prepSql += "SET IDENTITY_INSERT [$Database].[$Schema].[$TableName] ON;`n"
    }
    
    # Clear target table (after disabling constraints)
    Write-Host "  Clearing target table..." -NoNewline -ForegroundColor Gray
    $prepSql += "DELETE FROM [$Database].[$Schema].[$TableName];`n"
    
    $prepFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $prepSql | Out-File -FilePath $prepFile -Encoding UTF8
    $prepResult = sqlcmd -S $TargetInstance -E -d $Database -i $prepFile -b 2>&1
    Remove-Item $prepFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to prepare target table: $prepResult"
    }
    Write-Host " Done" -ForegroundColor Green
    
    # Use .NET SqlBulkCopy for batched data transfer
    Write-Host "  Transferring data in batches..." -ForegroundColor Gray
    
    Add-Type -AssemblyName System.Data
    
    # Increased connection timeout for large binary data transfers (5 minutes)
    $sourceConnString = "Server=$SourceInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=300"
    $targetConnString = "Server=$TargetInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=300"
    
    $sourceConn = New-Object System.Data.SqlClient.SqlConnection($sourceConnString)
    $targetConn = New-Object System.Data.SqlClient.SqlConnection($targetConnString)
    
    try {
        $sourceConn.Open()
        $targetConn.Open()
        
        $rowsTransferred = 0
        
        # Process in batches using OFFSET/FETCH
        for ($offset = 0; $offset -lt $totalRows; $offset += $BatchSize) {
            $batchNum = [Math]::Floor($offset / $BatchSize) + 1
            $currentBatchSize = [Math]::Min($BatchSize, $totalRows - $offset)
            
            Write-Host "    Batch $batchNum/$totalBatches (rows $($offset+1) to $([Math]::Min($offset+$BatchSize, $totalRows)))..." -NoNewline -ForegroundColor Gray
            
            $reader = $null
            try {
                # Read data from source with OFFSET/FETCH
                $sourceCmd = $sourceConn.CreateCommand()
                $sourceCmd.CommandText = "SELECT $columnList FROM [$Schema].[$TableName] ORDER BY [$pkColumn] OFFSET $offset ROWS FETCH NEXT $currentBatchSize ROWS ONLY"
                $sourceCmd.CommandTimeout = 600  # 10 minutes per batch
                
                $reader = $sourceCmd.ExecuteReader()
                
                # Bulk copy to target
                $bulkCopy = New-Object System.Data.SqlClient.SqlBulkCopy($targetConn)
                $bulkCopy.DestinationTableName = "[$Schema].[$TableName]"
                $bulkCopy.BatchSize = 1000  # Smaller internal batch size for binary data
                $bulkCopy.BulkCopyTimeout = 600  # 10 minutes per batch
                $bulkCopy.NotifyAfter = 1000
                
                # Map columns
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $columnName = $reader.GetName($i)
                    $bulkCopy.ColumnMappings.Add($columnName, $columnName)
                }
                
                # Copy data
                $bulkCopy.WriteToServer($reader)
                $reader.Close()
                $reader = $null
                
                $rowsTransferred += $currentBatchSize
                Write-Host " Done ($rowsTransferred/$totalRows rows)" -ForegroundColor Green
                
            } catch {
                if ($reader) { 
                    $reader.Close() 
                    $reader = $null
                }
                throw "Batch $batchNum failed: $_"
            }
        }
        
    } catch {
        throw "SqlBulkCopy failed: $_"
    } finally {
        if ($sourceConn.State -eq 'Open') { $sourceConn.Close() }
        if ($targetConn.State -eq 'Open') { $targetConn.Close() }
    }
    
    # Finalize target table
    Write-Host "  Finalizing..." -NoNewline -ForegroundColor Gray
    $finalizeSql = "SET QUOTED_IDENTIFIER ON;`n"
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
    Write-Host " Done" -ForegroundColor Green
    
    $transferSuccess = $true
    
} catch {
    $errorMessage = $_.Exception.Message
    Write-Host "  Failed!" -ForegroundColor Red
    Write-Host "    $errorMessage" -ForegroundColor Red
    exit 1
}

if ($transferSuccess) {
    Write-Host "  Success!" -ForegroundColor Green
    $count = sqlcmd -S $TargetInstance -E -d $Database -Q "SELECT COUNT(*) FROM [$Schema].[$TableName]" -W -h -1 | Where-Object { $_ -match '^\s*\d+\s*$' } | ForEach-Object { $_.Trim() }
    Write-Host "  Total rows transferred: $count" -ForegroundColor Green
}

