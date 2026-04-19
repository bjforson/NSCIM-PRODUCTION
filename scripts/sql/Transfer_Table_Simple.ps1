# Simple table transfer script - transfers one table at a time
# Uses SqlBulkCopy for reliable data transfer with direct connections

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS"
)

$ErrorActionPreference = "Stop"

Write-Host "Transferring: [$Schema].[$TableName]" -ForegroundColor Cyan

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
    
    # Use .NET SqlBulkCopy for reliable data transfer
    Write-Host "  Transferring data using SqlBulkCopy..." -NoNewline -ForegroundColor Gray
    
    Add-Type -AssemblyName System.Data
    
    # Increased connection timeout for large binary data transfers (5 minutes)
    $sourceConnString = "Server=$SourceInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=300"
    $targetConnString = "Server=$TargetInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=300"
    
    $sourceConn = New-Object System.Data.SqlClient.SqlConnection($sourceConnString)
    $targetConn = New-Object System.Data.SqlClient.SqlConnection($targetConnString)
    $reader = $null
    
    try {
        $sourceConn.Open()
        $targetConn.Open()
        
        # Read data from source
        $sourceCmd = $sourceConn.CreateCommand()
        $sourceCmd.CommandText = "SELECT $columnList FROM [$Schema].[$TableName]"
        # Increased timeout for large binary data (30 minutes)
        $sourceCmd.CommandTimeout = 1800
        
        $reader = $sourceCmd.ExecuteReader()
        
        # Bulk copy to target
        $bulkCopy = New-Object System.Data.SqlClient.SqlBulkCopy($targetConn)
        $bulkCopy.DestinationTableName = "[$Schema].[$TableName]"
        # Smaller batch size for large binary data to avoid memory issues
        $bulkCopy.BatchSize = 1000
        # Increased timeout for large binary data (30 minutes)
        $bulkCopy.BulkCopyTimeout = 1800
        $bulkCopy.NotifyAfter = 10000
        
        # Map columns
        for ($i = 0; $i -lt $reader.FieldCount; $i++) {
            $columnName = $reader.GetName($i)
            $bulkCopy.ColumnMappings.Add($columnName, $columnName)
        }
        
        # Copy data
        $bulkCopy.WriteToServer($reader)
        $reader.Close()
        $reader = $null
        
        Write-Host " Done" -ForegroundColor Green
        
    } catch {
        if ($reader) { 
            $reader.Close() 
            $reader = $null
        }
        throw "SqlBulkCopy failed: $_"
    } finally {
        if ($sourceConn.State -eq 'Open') { $sourceConn.Close() }
        if ($targetConn.State -eq 'Open') { $targetConn.Close() }
    }
    
    # Finalize target table
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
    Write-Host "  Rows transferred: $count" -ForegroundColor Green
}
