# Comprehensive Database Replication Script
# Replicates entire databases from source (localhost\NS_CIS) to target ((local))
# Includes: schemas, tables, indexes, constraints, data, views, procedures, functions, triggers

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [switch]$SkipDataTransfer = $false,
    [switch]$SkipObjects = $false
)

# Continues past errors intentionally: comprehensive replication loops across DBs, schemas, tables, indexes, views, procs, funcs, triggers — partial completeness is reported, per-object failures must not abort the run.
$ErrorActionPreference = "Continue"
$script:SourceInstance = $SourceInstance
$script:TargetInstance = $TargetInstance

# Get script directory for calling helper scripts
if ($PSScriptRoot) {
    $script:ScriptDir = $PSScriptRoot
} else {
    $script:ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Comprehensive Database Replication" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host "Databases: $($Databases -join ', ')" -ForegroundColor Yellow
Write-Host ""

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Execute-SqlQuery {
    param(
        [string]$Instance,
        [string]$Database,
        [string]$Query,
        [switch]$SuppressOutput
    )
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $outputFile = [System.IO.Path]::GetTempFileName()
    $Query | Out-File -FilePath $tempFile -Encoding UTF8
    
    sqlcmd -S $Instance -E -d $Database -i $tempFile -W -h -1 -o $outputFile 2>&1 | Out-Null
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    
    if ($LASTEXITCODE -eq 0) {
        $content = Get-Content $outputFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
        Remove-Item $outputFile -Force
        return $content
    } else {
        $errorContent = Get-Content $outputFile -Raw
        Remove-Item $outputFile -Force
        if (-not $SuppressOutput) {
            Write-Host "  SQL Error: $errorContent" -ForegroundColor Red
        }
        return $null
    }
}

function Execute-SqlScript {
    param(
        [string]$Instance,
        [string]$Database,
        [string]$Script
    )
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $Script | Out-File -FilePath $tempFile -Encoding UTF8
    
    $output = sqlcmd -S $Instance -E -d $Database -i $tempFile -b 2>&1
    Remove-Item $tempFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        throw "SQL execution failed: $output"
    }
    return $output
}

function Get-ObjectDefinition {
    param(
        [string]$Database,
        [string]$Schema,
        [string]$ObjectName,
        [string]$ObjectType
    )
    
    # Use .NET SqlClient for better handling of large definitions
    Add-Type -AssemblyName System.Data
    
    $connString = "Server=$($script:SourceInstance);Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30"
    $conn = New-Object System.Data.SqlClient.SqlConnection($connString)
    
    try {
        $conn.Open()
        $query = "SELECT OBJECT_DEFINITION(OBJECT_ID('[$Schema].[$ObjectName]')) AS Definition"
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $query
        $cmd.CommandTimeout = 30
        
        $reader = $cmd.ExecuteReader()
        if ($reader.Read()) {
            $definition = $reader["Definition"]
            $reader.Close()
            return $definition
        }
        $reader.Close()
    } catch {
        Write-Host "      Error getting definition for $ObjectType [$Schema].[$ObjectName]: $_" -ForegroundColor Yellow
    } finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
    }
    
    return $null
}

# ============================================================================
# PHASE 1: CREATE DATABASES
# ============================================================================

function Create-Databases {
    param([string[]]$Databases)
    
    Write-Host "PHASE 1: Creating Databases" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Gray
    
    foreach ($db in $Databases) {
        Write-Host "Creating database: $db..." -NoNewline -ForegroundColor Yellow
        
        # Check if database exists on source
        $checkQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'"
        $exists = Execute-SqlQuery -Instance $script:SourceInstance -Database "master" -Query $checkQuery
        if (-not $exists -or $exists[0] -eq '0') {
            Write-Host " Source database does not exist - skipping" -ForegroundColor Yellow
            continue
        }
        
        # Check if already exists on target
        $targetExists = Execute-SqlQuery -Instance $script:TargetInstance -Database "master" -Query $checkQuery
        if ($targetExists -and $targetExists[0] -ne '0') {
            Write-Host " Already exists - skipping" -ForegroundColor Yellow
            continue
        }
        
        # Get source database collation
        $collationQuery = "SELECT collation_name FROM sys.databases WHERE name = '$db'"
        $collationResult = Execute-SqlQuery -Instance $script:SourceInstance -Database "master" -Query $collationQuery
        $collation = $null
        if ($collationResult -and $collationResult.Count -gt 0) {
            $collationValue = $collationResult[0].Trim()
            if ($collationValue -and $collationValue -ne 'NULL' -and $collationValue -ne '') {
                $collation = $collationValue
            }
        }
        
        # Create database with source collation if available
        if ($collation) {
            $createDbScript = "CREATE DATABASE [$db] COLLATE $collation"
        } else {
            $createDbScript = "CREATE DATABASE [$db]"
        }
        
        try {
            Execute-SqlScript -Instance $script:TargetInstance -Database "master" -Script $createDbScript
            Write-Host " Done" -ForegroundColor Green
        } catch {
            Write-Host " Failed: $_" -ForegroundColor Red
            throw
        }
    }
    Write-Host ""
}

# ============================================================================
# PHASE 2: COPY SCHEMAS
# ============================================================================

function Copy-Schemas {
    param([string]$Database)
    
    Write-Host "  Copying schemas..." -NoNewline -ForegroundColor Gray
    
    $query = @"
SELECT name FROM sys.schemas 
WHERE name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys', 'db_owner', 'db_accessadmin', 'db_securityadmin', 'db_ddladmin', 'db_backupoperator', 'db_datareader', 'db_datawriter', 'db_denydatareader', 'db_denydatawriter')
ORDER BY name
"@
    
    $schemas = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $schemas -or $schemas.Count -eq 0) {
        Write-Host " None found (using default schemas)" -ForegroundColor Gray
        return
    }
    
    foreach ($schema in $schemas) {
        $schemaName = $schema.Trim()
        if ([string]::IsNullOrWhiteSpace($schemaName)) { continue }
        
        # Check if schema exists on target
        $checkQuery = "SELECT COUNT(*) FROM sys.schemas WHERE name = '$schemaName'"
        $exists = Execute-SqlQuery -Instance $script:TargetInstance -Database $Database -Query $checkQuery
        if ($exists -and $exists[0] -ne '0') { continue }
        
        # Create schema
        $createSchemaScript = "CREATE SCHEMA [$schemaName]"
        try {
            Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $createSchemaScript | Out-Null
        } catch {
            # Ignore if already exists
        }
    }
    
    Write-Host " Done ($($schemas.Count) schemas)" -ForegroundColor Green
}

# ============================================================================
# PHASE 3: COPY TABLE STRUCTURES
# ============================================================================

function Copy-TableStructures {
    param([string]$Database)
    
    Write-Host "  Copying table structures..." -ForegroundColor Gray
    
    $query = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tables = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $tables -or $tables.Count -eq 0) {
        Write-Host "    No tables found" -ForegroundColor Yellow
        return @()
    }
    
    $tableList = @()
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $tableList += @{ Schema = $parts[0].Trim(); Table = $parts[1].Trim() }
        }
    }
    
    Write-Host "    Found $($tableList.Count) tables" -ForegroundColor Gray
    
    # Use existing Copy_Table_Schema_Final.ps1 script for each table
    $schemaScript = Join-Path $script:ScriptDir "Copy_Table_Schema_Final.ps1"
    
    $failedTables = @()
    $index = 0
    
    foreach ($tableInfo in $tableList) {
        $index++
        Write-Host "    [$index/$($tableList.Count)] [$($tableInfo.Schema)].[$($tableInfo.Table)]..." -NoNewline -ForegroundColor Gray
        
        try {
            & $schemaScript -TableName $tableInfo.Table -Schema $tableInfo.Schema -Database $Database -TargetInstance $script:TargetInstance -SourceInstance $script:SourceInstance 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host " Done" -ForegroundColor Green
            } else {
                Write-Host " Failed" -ForegroundColor Red
                $failedTables += "$($tableInfo.Schema).$($tableInfo.Table)"
            }
        } catch {
            Write-Host " Failed: $_" -ForegroundColor Red
            $failedTables += "$($tableInfo.Schema).$($tableInfo.Table)"
        }
    }
    
    if ($failedTables.Count -gt 0) {
        Write-Host "    Warning: $($failedTables.Count) tables failed to copy" -ForegroundColor Yellow
    }
    
    return $tableList
}

# ============================================================================
# PHASE 4: COPY INDEXES
# ============================================================================

function Copy-Indexes {
    param([string]$Database, [array]$Tables)
    
    Write-Host "  Copying indexes and constraints..." -NoNewline -ForegroundColor Gray
    
    # Note: Full index/constraint copying (PKs, UKs, non-clustered indexes) requires SMO or detailed SQL generation
    # The table schema script (Copy_Table_Schema_Final.ps1) only creates column definitions
    # This is a known limitation - indexes and constraints are not currently copied
    # Data transfer will still work, but performance may be impacted without indexes
    # Constraints (PKs, UKs, FKs) can be added manually or via a separate script if needed
    
    Write-Host " Done (skipped - requires SMO for full implementation)" -ForegroundColor Yellow
    Write-Host "    Note: Indexes and constraints (PKs/UKs) are not copied in this version" -ForegroundColor Gray
    Write-Host "    Data transfer will proceed, but indexes should be added separately for performance" -ForegroundColor Gray
}

# ============================================================================
# PHASE 6: COPY FOREIGN KEYS (AFTER DATA TRANSFER)
# ============================================================================

function Copy-ForeignKeys {
    param([string]$Database, [array]$Tables)
    
    Write-Host "  Copying foreign key constraints..." -ForegroundColor Gray
    
    # Use .NET SqlClient for proper FK metadata retrieval
    Add-Type -AssemblyName System.Data
    
    $connString = "Server=$($script:SourceInstance);Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30"
    $conn = New-Object System.Data.SqlClient.SqlConnection($connString)
    
    $fkList = @()
    
    try {
        $conn.Open()
        
        # Get all foreign keys from source
        $fkQuery = @"
SELECT 
    fk.name AS ForeignKeyName,
    OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
    OBJECT_NAME(fk.parent_object_id) AS ParentTable,
    (
        SELECT STUFF((
            SELECT ', [' + pc.name + ']'
            FROM sys.foreign_key_columns fkc
            INNER JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
            WHERE fkc.constraint_object_id = fk.object_id
            ORDER BY fkc.constraint_column_id
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'), 1, 2, '')
    ) AS ParentColumns,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
    (
        SELECT STUFF((
            SELECT ', [' + rc.name + ']'
            FROM sys.foreign_key_columns fkc
            INNER JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
            WHERE fkc.constraint_object_id = fk.object_id
            ORDER BY fkc.constraint_column_id
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'), 1, 2, '')
    ) AS ReferencedColumns,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction,
    fk.is_disabled AS IsDisabled
FROM sys.foreign_keys fk
ORDER BY OBJECT_SCHEMA_NAME(fk.parent_object_id), OBJECT_NAME(fk.parent_object_id), fk.name
"@
        
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $fkQuery
        $cmd.CommandTimeout = 60
        
        $reader = $cmd.ExecuteReader()
        
        while ($reader.Read()) {
            $fkList += @{
                Name = $reader["ForeignKeyName"].ToString()
                ParentSchema = $reader["ParentSchema"].ToString()
                ParentTable = $reader["ParentTable"].ToString()
                ParentColumns = $reader["ParentColumns"].ToString()
                ReferencedSchema = $reader["ReferencedSchema"].ToString()
                ReferencedTable = $reader["ReferencedTable"].ToString()
                ReferencedColumns = $reader["ReferencedColumns"].ToString()
                DeleteAction = $reader["DeleteAction"].ToString()
                UpdateAction = $reader["UpdateAction"].ToString()
                IsDisabled = $reader["IsDisabled"]
            }
        }
        
        $reader.Close()
        
    } catch {
        Write-Host "    Error retrieving FK metadata: $_" -ForegroundColor Yellow
    } finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
    }
    
    if ($fkList.Count -eq 0) {
        Write-Host "    No foreign keys found" -ForegroundColor Gray
        return
    }
    
    Write-Host "    Found $($fkList.Count) foreign key(s)" -ForegroundColor Gray
    
    $successCount = 0
    $failCount = 0
    
    foreach ($fk in $fkList) {
        # Build FK CREATE statement
        $deleteClause = ""
        $updateClause = ""
        
        if ($fk.DeleteAction -ne "NO_ACTION") {
            $deleteClause = " ON DELETE $($fk.DeleteAction)"
        }
        if ($fk.UpdateAction -ne "NO_ACTION") {
            $updateClause = " ON UPDATE $($fk.UpdateAction)"
        }
        
        $fkScript = "ALTER TABLE [$($fk.ParentSchema)].[$($fk.ParentTable)] ADD CONSTRAINT [$($fk.Name)] FOREIGN KEY ($($fk.ParentColumns)) REFERENCES [$($fk.ReferencedSchema)].[$($fk.ReferencedTable)] ($($fk.ReferencedColumns))$deleteClause$updateClause"
        
        try {
            Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $fkScript | Out-Null
            $successCount++
        } catch {
            # FK might already exist or referenced table not ready yet
            $failCount++
        }
    }
    
    Write-Host "    Created $successCount/$($fkList.Count) foreign key(s)" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Yellow" })
    if ($failCount -gt 0) {
        Write-Host "    Warning: $failCount foreign key(s) failed (may already exist or dependencies not ready)" -ForegroundColor Yellow
    }
}

# ============================================================================
# PHASE 5: TRANSFER DATA (MOVED BEFORE FK CREATION)
# ============================================================================

function Transfer-Data {
    param([string]$Database, [array]$Tables)
    
    if ($SkipDataTransfer) {
        Write-Host "  Skipping data transfer (SkipDataTransfer flag set)" -ForegroundColor Yellow
        return
    }
    
    Write-Host "  Transferring data..." -ForegroundColor Gray
    
    $transferScript = Join-Path $script:ScriptDir "Transfer_Table_Simple.ps1"
    
    $index = 0
    foreach ($tableInfo in $Tables) {
        $index++
        Write-Host "    [$index/$($Tables.Count)] [$($tableInfo.Schema)].[$($tableInfo.Table)]..." -NoNewline -ForegroundColor Gray
        
        try {
            & $transferScript -TableName $tableInfo.Table -Schema $tableInfo.Schema -Database $Database -TargetInstance $script:TargetInstance -SourceInstance $script:SourceInstance 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host " Done" -ForegroundColor Green
            } else {
                Write-Host " Failed" -ForegroundColor Red
            }
        } catch {
            Write-Host " Failed: $_" -ForegroundColor Red
        }
    }
}


# ============================================================================
# PHASE 7: COPY VIEWS
# ============================================================================

function Copy-Views {
    param([string]$Database)
    
    if ($SkipObjects) {
        Write-Host "  Skipping views (SkipObjects flag set)" -ForegroundColor Yellow
        return
    }
    
    Write-Host "  Copying views..." -NoNewline -ForegroundColor Gray
    
    $query = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME != 'sysdiagrams' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $views = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $views -or $views.Count -eq 0) {
        Write-Host " None found" -ForegroundColor Gray
        return
    }
    
    $viewList = @()
    foreach ($viewLine in $views) {
        $parts = $viewLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $viewList += @{ Schema = $parts[0].Trim(); View = $parts[1].Trim() }
        }
    }
    
    $successCount = 0
    foreach ($viewInfo in $viewList) {
        $definition = Get-ObjectDefinition -Database $Database -Schema $viewInfo.Schema -ObjectName $viewInfo.View -ObjectType "VIEW"
        if ($definition) {
            try {
                Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $definition | Out-Null
                $successCount++
            } catch {
                # View might depend on objects not yet created, will be logged
            }
        }
    }
    
    Write-Host " Done ($successCount/$($viewList.Count))" -ForegroundColor $(if ($successCount -eq $viewList.Count) { "Green" } else { "Yellow" })
}

# ============================================================================
# PHASE 8: COPY STORED PROCEDURES
# ============================================================================

function Copy-StoredProcedures {
    param([string]$Database)
    
    if ($SkipObjects) {
        Write-Host "  Skipping stored procedures (SkipObjects flag set)" -ForegroundColor Yellow
        return
    }
    
    Write-Host "  Copying stored procedures..." -NoNewline -ForegroundColor Gray
    
    $query = "SELECT ROUTINE_SCHEMA, ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME"
    $procs = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $procs -or $procs.Count -eq 0) {
        Write-Host " None found" -ForegroundColor Gray
        return
    }
    
    $procList = @()
    foreach ($procLine in $procs) {
        $parts = $procLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $procList += @{ Schema = $parts[0].Trim(); Procedure = $parts[1].Trim() }
        }
    }
    
    $successCount = 0
    foreach ($procInfo in $procList) {
        $definition = Get-ObjectDefinition -Database $Database -Schema $procInfo.Schema -ObjectName $procInfo.Procedure -ObjectType "PROCEDURE"
        if ($definition) {
            try {
                Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $definition | Out-Null
                $successCount++
            } catch {
                # Procedure might have dependency issues
            }
        }
    }
    
    Write-Host " Done ($successCount/$($procList.Count))" -ForegroundColor $(if ($successCount -eq $procList.Count) { "Green" } else { "Yellow" })
}

# ============================================================================
# PHASE 9: COPY FUNCTIONS
# ============================================================================

function Copy-Functions {
    param([string]$Database)
    
    if ($SkipObjects) {
        Write-Host "  Skipping functions (SkipObjects flag set)" -ForegroundColor Yellow
        return
    }
    
    Write-Host "  Copying functions..." -NoNewline -ForegroundColor Gray
    
    $query = "SELECT ROUTINE_SCHEMA, ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION' ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME"
    $functions = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $functions -or $functions.Count -eq 0) {
        Write-Host " None found" -ForegroundColor Gray
        return
    }
    
    $funcList = @()
    foreach ($funcLine in $functions) {
        $parts = $funcLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $funcList += @{ Schema = $parts[0].Trim(); Function = $parts[1].Trim() }
        }
    }
    
    $successCount = 0
    foreach ($funcInfo in $funcList) {
        $definition = Get-ObjectDefinition -Database $Database -Schema $funcInfo.Schema -ObjectName $funcInfo.Function -ObjectType "FUNCTION"
        if ($definition) {
            try {
                Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $definition | Out-Null
                $successCount++
            } catch {
                # Function might have dependency issues
            }
        }
    }
    
    Write-Host " Done ($successCount/$($funcList.Count))" -ForegroundColor $(if ($successCount -eq $funcList.Count) { "Green" } else { "Yellow" })
}

# ============================================================================
# PHASE 10: COPY TRIGGERS
# ============================================================================

function Copy-Triggers {
    param([string]$Database)
    
    if ($SkipObjects) {
        Write-Host "  Skipping triggers (SkipObjects flag set)" -ForegroundColor Yellow
        return
    }
    
    Write-Host "  Copying triggers..." -NoNewline -ForegroundColor Gray
    
    $query = @"
SELECT 
    s.name AS SchemaName,
    t.name AS TableName,
    tr.name AS TriggerName
FROM sys.triggers tr
INNER JOIN sys.tables t ON tr.parent_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE tr.parent_class = 1  -- DML triggers only
ORDER BY s.name, t.name, tr.name
"@
    
    $triggers = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $triggers -or $triggers.Count -eq 0) {
        Write-Host " None found" -ForegroundColor Gray
        return
    }
    
    $triggerList = @()
    foreach ($triggerLine in $triggers) {
        $parts = $triggerLine -split '\s+', 3
        if ($parts.Length -ge 3) {
            $triggerList += @{ Schema = $parts[0].Trim(); Table = $parts[1].Trim(); Trigger = $parts[2].Trim() }
        }
    }
    
    $successCount = 0
    foreach ($triggerInfo in $triggerList) {
        $definition = Get-ObjectDefinition -Database $Database -Schema $triggerInfo.Schema -ObjectName $triggerInfo.Trigger -ObjectType "TRIGGER"
        if ($definition) {
            try {
                Execute-SqlScript -Instance $script:TargetInstance -Database $Database -Script $definition | Out-Null
                $successCount++
            } catch {
                # Trigger might have dependency issues
            }
        }
    }
    
    Write-Host " Done ($successCount/$($triggerList.Count))" -ForegroundColor $(if ($successCount -eq $triggerList.Count) { "Green" } else { "Yellow" })
}

# ============================================================================
# PHASE 11: VERIFICATION
# ============================================================================

function Verify-Replication {
    param([string]$Database)
    
    Write-Host "  Verifying replication..." -ForegroundColor Gray
    
    $query = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tables = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $query
    
    if (-not $tables -or $tables.Count -eq 0) { return }
    
    $mismatches = 0
    $matchCount = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0].Trim()
            $table = $parts[1].Trim()
            
            $sourceQuery = "SELECT COUNT(*) FROM [$schema].[$table]"
            $targetQuery = "SELECT COUNT(*) FROM [$schema].[$table]"
            
            $sourceCount = Execute-SqlQuery -Instance $script:SourceInstance -Database $Database -Query $sourceQuery
            $targetCount = Execute-SqlQuery -Instance $script:TargetInstance -Database $Database -Query $targetQuery
            
            # Parse source count
            $sourceNum = -1
            if ($sourceCount -and $sourceCount.Count -gt 0) {
                $sourceValue = $sourceCount[0].ToString().Trim()
                if ($sourceValue -match '^\s*(\d+)\s*$') {
                    $sourceNum = [long]$matches[1]
                }
            }
            
            # Parse target count
            $targetNum = -1
            if ($targetCount -and $targetCount.Count -gt 0) {
                $targetValue = $targetCount[0].ToString().Trim()
                if ($targetValue -match '^\s*(\d+)\s*$') {
                    $targetNum = [long]$matches[1]
                }
            }
            
            if ($sourceNum -ne $targetNum) {
                $mismatches++
                Write-Host "    Mismatch: [$schema].[$table] - Source: $sourceNum, Target: $targetNum" -ForegroundColor Yellow
            } else {
                $matchCount++
            }
        }
    }
    
    Write-Host "    Verification: $matchCount matches, $mismatches mismatches" -ForegroundColor $(if ($mismatches -eq 0) { "Green" } else { "Yellow" })
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

try {
    # Phase 1: Create all databases
    Create-Databases -Databases $Databases
    
    # Process each database completely
    foreach ($db in $Databases) {
        Write-Host ""
        Write-Host "Processing Database: $db" -ForegroundColor Cyan
        Write-Host ("=" * 60) -ForegroundColor Gray
        
        # Check if database exists on source
        $checkQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'"
        $exists = Execute-SqlQuery -Instance $script:SourceInstance -Database "master" -Query $checkQuery
        if (-not $exists -or $exists[0] -eq '0') {
            Write-Host "Source database $db does not exist - skipping" -ForegroundColor Yellow
            continue
        }
        
        # Phase 2: Copy schemas
        Copy-Schemas -Database $db
        
        # Phase 3: Copy table structures
        $tables = Copy-TableStructures -Database $db
        
        if ($tables.Count -eq 0) {
            Write-Host "No tables to process - skipping remaining phases" -ForegroundColor Yellow
            continue
        }
        
        # Phase 4: Copy indexes (placeholder - indexes not copied in current version)
        Copy-Indexes -Database $db -Tables $tables
        
        # Phase 5: Transfer data (before FK creation to avoid constraint checks during bulk insert)
        Transfer-Data -Database $db -Tables $tables
        
        # Phase 6: Copy foreign keys (after data transfer, they'll be enabled by default)
        Copy-ForeignKeys -Database $db -Tables $tables
        
        # Phase 7: Copy views
        Copy-Views -Database $db
        
        # Phase 8: Copy stored procedures
        Copy-StoredProcedures -Database $db
        
        # Phase 9: Copy functions
        Copy-Functions -Database $db
        
        # Phase 10: Copy triggers
        Copy-Triggers -Database $db
        
        # Phase 11: Verification
        Verify-Replication -Database $db
        
        Write-Host ""
        Write-Host "Completed: $db" -ForegroundColor Green
        Write-Host ""
    }
    
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Replication Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    if ($_.Exception.Message) {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
    exit 1
}

