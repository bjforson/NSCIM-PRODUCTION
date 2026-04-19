# Enhanced table transfer script with batching, progress tracking, and direct connections
# Transfers data from source to target using direct connections (no linked server)
# Handles large tables with batching to prevent timeouts

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS",
    [int]$BatchSize = 10000,
    [int]$CommandTimeout = 300,
    [switch]$Resume,
    [string]$ProgressLogFile = "transfer_progress.log"
)

$ErrorActionPreference = "Continue"

$fullTableName = "[$Schema].[$TableName]"
Write-Host "Transferring: [$Database].$fullTableName" -ForegroundColor Cyan
Write-Host "  Source: $SourceInstance" -ForegroundColor Gray
Write-Host "  Target: $TargetInstance" -ForegroundColor Gray
Write-Host "  Batch Size: $BatchSize rows" -ForegroundColor Gray

# Log progress
function Write-ProgressLog {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] $Message"
    Add-Content -Path $ProgressLogFile -Value $logMessage
    Write-Host $Message
}

# Get column list from target (table should already exist)
function Get-ColumnList {
    param([string]$Instance, [string]$Db, [string]$Sch, [string]$Tbl)
    
    $query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$Sch' AND TABLE_NAME = '$Tbl' ORDER BY ORDINAL_POSITION"
    $columnsFile = [System.IO.Path]::GetTempFileName()
    
    $result = sqlcmd -S $Instance -E -d $Db -Q $query -W -h -1 -o $columnsFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $columnsFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get column list: $result"
    }
    
    $columns = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $columnsFile -Force
    
    if (-not $columns) {
        throw "No columns found for table $Sch.$Tbl"
    }
    
    return $columns
}

# Check if table has identity column
function Test-IdentityColumn {
    param([string]$Instance, [string]$Db, [string]$Sch, [string]$Tbl)
    
    $query = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Db].[$Sch].[$Tbl]')"
    $resultFile = [System.IO.Path]::GetTempFileName()
    
    sqlcmd -S $Instance -E -d $Db -Q $query -W -h -1 -o $resultFile 2>&1 | Out-Null
    
    $result = Get-Content $resultFile | Where-Object { $_ -match '^\s*[1-9]' }
    Remove-Item $resultFile -Force
    
    return ($result -ne $null)
}

# Get row count from source
function Get-SourceRowCount {
    param([string]$Instance, [string]$Db, [string]$Sch, [string]$Tbl)
    
    $query = "SELECT COUNT(*) FROM [$Sch].[$Tbl]"
    $countFile = [System.IO.Path]::GetTempFileName()
    
    $result = sqlcmd -S $Instance -E -d $Db -Q $query -W -h -1 -o $countFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $countFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get source row count: $result"
    }
    
    $countStr = (Get-Content $countFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
    Remove-Item $countFile -Force
    
    if (-not $countStr) {
        return 0
    }
    
    return [long]$countStr.Trim()
}

# Get current row count from target
function Get-TargetRowCount {
    param([string]$Instance, [string]$Db, [string]$Sch, [string]$Tbl)
    
    $query = "SELECT COUNT(*) FROM [$Sch].[$Tbl]"
    $countFile = [System.IO.Path]::GetTempFileName()
    
    $result = sqlcmd -S $Instance -E -d $Db -Q $query -W -h -1 -o $countFile 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $countFile -Force -ErrorAction SilentlyContinue
        return 0
    }
    
    $countStr = (Get-Content $countFile | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
    Remove-Item $countFile -Force
    
    if (-not $countStr) {
        return 0
    }
    
    return [long]$countStr.Trim()
}

try {
    # Get column list
    Write-ProgressLog "Getting column list..."
    $columns = Get-ColumnList -Instance $TargetInstance -Db $Database -Sch $Schema -Tbl $TableName
    $columnList = ($columns | ForEach-Object { "[$_]" }) -join ", "
    Write-ProgressLog "Found $($columns.Count) columns"
    
    # Check for identity column
    $hasIdentity = Test-IdentityColumn -Instance $TargetInstance -Db $Database -Sch $Schema -Tbl $TableName
    if ($hasIdentity) {
        Write-ProgressLog "Table has identity column - will use IDENTITY_INSERT"
    }
    
    # Get source row count
    Write-ProgressLog "Getting source row count..."
    $sourceCount = Get-SourceRowCount -Instance $SourceInstance -Db $Database -Sch $Schema -Tbl $TableName
    Write-ProgressLog "Source has $sourceCount rows"
    
    # Get target row count
    $targetCount = Get-TargetRowCount -Instance $TargetInstance -Db $Database -Sch $Schema -Tbl $TableName
    Write-ProgressLog "Target currently has $targetCount rows"
    
    # If resuming and target already has data, we need to handle it
    if ($Resume -and $targetCount -gt 0 -and $targetCount -lt $sourceCount) {
        Write-ProgressLog "Resuming transfer: $targetCount of $sourceCount rows already transferred"
        Write-Host "  Warning: Resume mode detected. Existing data will be preserved." -ForegroundColor Yellow
        Write-Host "  If you want to re-transfer, delete existing data first." -ForegroundColor Yellow
    }
    
    # If target has all rows, skip
    if ($targetCount -ge $sourceCount -and $sourceCount -gt 0) {
        Write-ProgressLog "Table already complete ($targetCount >= $sourceCount rows)"
        Write-Host "  ✓ Table already complete - skipping" -ForegroundColor Green
        return
    }
    
    # For small tables, do a simple transfer
    if ($sourceCount -le $BatchSize) {
        Write-ProgressLog "Small table detected - using simple transfer"
        
        # Build transfer SQL using BCP or direct query
        # Use sqlcmd with a query that reads from source and inserts to target
        # We'll use a temp approach: export to file, then import
        
        $tempDataFile = [System.IO.Path]::GetTempFileName() + ".csv"
        
        # Export from source using BCP
        Write-ProgressLog "Exporting data from source..."
        $bcpExport = "bcp `"SELECT $columnList FROM [$Database].[$Schema].[$TableName]`" queryout `"$tempDataFile`" -S `"$SourceInstance`" -T -c -t`",`" -r`"\n`" -d `"$Database`""
        
        $exportResult = Invoke-Expression $bcpExport 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "BCP export failed: $exportResult"
        }
        
        # Prepare target table
        $prepSql = @"
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [$Database].[$Schema].[$TableName] NOCHECK CONSTRAINT ALL;
"@
        
        if ($hasIdentity) {
            $prepSql += "SET IDENTITY_INSERT [$Database].[$Schema].[$TableName] ON;`n"
        }
        
        $prepFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $prepSql | Out-File -FilePath $prepFile -Encoding UTF8
        
        sqlcmd -S $TargetInstance -E -d $Database -i $prepFile -b 2>&1 | Out-Null
        Remove-Item $prepFile -Force
        
        # Import to target using BCP
        Write-ProgressLog "Importing data to target..."
        $bcpImport = "bcp `"[$Database].[$Schema].[$TableName]`" in `"$tempDataFile`" -S `"$TargetInstance`" -T -c -t`",`" -r`"\n`" -d `"$Database`" -F 2"
        
        $importResult = Invoke-Expression $bcpImport 2>&1
        $importSuccess = $LASTEXITCODE -eq 0
        
        # Cleanup
        Remove-Item $tempDataFile -Force -ErrorAction SilentlyContinue
        
        # Finalize
        $finalizeSql = @"
SET QUOTED_IDENTIFIER ON;
"@
        
        if ($hasIdentity) {
            $finalizeSql += "SET IDENTITY_INSERT [$Database].[$Schema].[$TableName] OFF;`n"
        }
        
        $finalizeSql += "ALTER TABLE [$Database].[$Schema].[$TableName] CHECK CONSTRAINT ALL;"
        
        $finalizeFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $finalizeSql | Out-File -FilePath $finalizeFile -Encoding UTF8
        
        sqlcmd -S $TargetInstance -E -d $Database -i $finalizeFile -b 2>&1 | Out-Null
        Remove-Item $finalizeFile -Force
        
        if (-not $importSuccess) {
            throw "BCP import failed: $importResult"
        }
        
        # Verify
        $finalCount = Get-TargetRowCount -Instance $TargetInstance -Db $Database -Sch $Schema -Tbl $TableName
        Write-ProgressLog "Transfer complete. Final row count: $finalCount"
        Write-Host "  ✓ Success! Transferred $finalCount rows" -ForegroundColor Green
        
    } else {
        # Large table - use batching with PowerShell .NET SqlConnection
        Write-ProgressLog "Large table detected - using batched transfer"
        Write-Host "  Using batched transfer for $sourceCount rows..." -ForegroundColor Yellow
        
        # Load SQL Server types
        Add-Type -AssemblyName System.Data
        
        # Build connection strings
        $sourceConnString = "Server=$SourceInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;Command Timeout=$CommandTimeout"
        $targetConnString = "Server=$TargetInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;Command Timeout=$CommandTimeout"
        
        $sourceConn = New-Object System.Data.SqlClient.SqlConnection($sourceConnString)
        $targetConn = New-Object System.Data.SqlClient.SqlConnection($targetConnString)
        
        try {
            $sourceConn.Open()
            $targetConn.Open()
            
            Write-ProgressLog "Connections established"
            
            # Prepare target table
            $prepCmd = $targetConn.CreateCommand()
            $prepCmd.CommandText = "ALTER TABLE [$Schema].[$TableName] NOCHECK CONSTRAINT ALL;"
            if ($hasIdentity) {
                $prepCmd.CommandText += "SET IDENTITY_INSERT [$Schema].[$TableName] ON;"
            }
            $prepCmd.ExecuteNonQuery() | Out-Null
            Write-ProgressLog "Target table prepared"
            
            # Get primary key or first column for ordering
            $orderByColumn = $columns[0]
            $pkQuery = "SELECT TOP 1 COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName' ORDER BY ORDINAL_POSITION"
            $pkFile = [System.IO.Path]::GetTempFileName()
            sqlcmd -S $TargetInstance -E -d $Database -Q $pkQuery -W -h -1 -o $pkFile 2>&1 | Out-Null
            $pkColumn = (Get-Content $pkFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' } | Select-Object -First 1)
            Remove-Item $pkFile -Force
            if ($pkColumn) {
                $orderByColumn = $pkColumn.Trim()
            }
            
            # Batch transfer
            $offset = if ($Resume) { $targetCount } else { 0 }
            $totalBatches = [Math]::Ceiling(($sourceCount - $offset) / $BatchSize)
            $batchNumber = 0
            $totalTransferred = $offset
            
            Write-ProgressLog "Starting batch transfer from offset $offset"
            
            while ($offset -lt $sourceCount) {
                $batchNumber++
                $currentBatchSize = [Math]::Min($BatchSize, $sourceCount - $offset)
                
                Write-Host "  Batch $batchNumber/$totalBatches: Transferring rows $offset to $($offset + $currentBatchSize - 1)..." -NoNewline -ForegroundColor Gray
                
                # Read batch from source
                $selectQuery = "SELECT $columnList FROM [$Schema].[$TableName] ORDER BY [$orderByColumn] OFFSET $offset ROWS FETCH NEXT $currentBatchSize ROWS ONLY"
                $sourceCmd = $sourceConn.CreateCommand()
                $sourceCmd.CommandText = $selectQuery
                $sourceCmd.CommandTimeout = $CommandTimeout
                
                $reader = $sourceCmd.ExecuteReader()
                
                # Build bulk insert
                $insertQuery = "INSERT INTO [$Schema].[$TableName] ($columnList) VALUES "
                $values = @()
                $rowCount = 0
                
                while ($reader.Read()) {
                    $rowValues = @()
                    for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                        $value = $reader.GetValue($i)
                        if ($null -eq $value -or [DBNull]::Value.Equals($value)) {
                            $rowValues += "NULL"
                        } else {
                            $sqlValue = $value.ToString().Replace("'", "''")
                            if ($reader.GetFieldType($i) -in @([DateTime], [String], [Guid])) {
                                $rowValues += "'$sqlValue'"
                            } else {
                                $rowValues += $sqlValue
                            }
                        }
                    }
                    $values += "(" + ($rowValues -join ", ") + ")"
                    $rowCount++
                    
                    # Insert in smaller chunks to avoid SQL statement size limits
                    if ($values.Count -ge 1000) {
                        $chunkQuery = $insertQuery + ($values -join ", ")
                        $targetCmd = $targetConn.CreateCommand()
                        $targetCmd.CommandText = $chunkQuery
                        $targetCmd.CommandTimeout = $CommandTimeout
                        $targetCmd.ExecuteNonQuery() | Out-Null
                        $values = @()
                    }
                }
                
                $reader.Close()
                
                # Insert remaining values
                if ($values.Count -gt 0) {
                    $chunkQuery = $insertQuery + ($values -join ", ")
                    $targetCmd = $targetConn.CreateCommand()
                    $targetCmd.CommandText = $chunkQuery
                    $targetCmd.CommandTimeout = $CommandTimeout
                    $targetCmd.ExecuteNonQuery() | Out-Null
                }
                
                $totalTransferred += $rowCount
                $offset += $currentBatchSize
                
                $percentComplete = [Math]::Round(($totalTransferred / $sourceCount) * 100, 1)
                Write-Host " ✓ ($percentComplete% complete, $totalTransferred/$sourceCount rows)" -ForegroundColor Green
                Write-ProgressLog "Batch $batchNumber complete: $totalTransferred/$sourceCount rows ($percentComplete%)"
            }
            
            # Finalize
            $finalizeCmd = $targetConn.CreateCommand()
            if ($hasIdentity) {
                $finalizeCmd.CommandText = "SET IDENTITY_INSERT [$Schema].[$TableName] OFF;"
                $finalizeCmd.ExecuteNonQuery() | Out-Null
            }
            $finalizeCmd.CommandText = "ALTER TABLE [$Schema].[$TableName] CHECK CONSTRAINT ALL;"
            $finalizeCmd.ExecuteNonQuery() | Out-Null
            
            Write-ProgressLog "Transfer complete. Total rows transferred: $totalTransferred"
            Write-Host "  ✓ Success! Transferred $totalTransferred rows" -ForegroundColor Green
            
        } finally {
            if ($sourceConn.State -eq 'Open') { $sourceConn.Close() }
            if ($targetConn.State -eq 'Open') { $targetConn.Close() }
        }
    }
    
} catch {
    Write-ProgressLog "ERROR: $_"
    Write-Host "  ✗ Failed: $_" -ForegroundColor Red
    Write-Host "  Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    exit 1
}

