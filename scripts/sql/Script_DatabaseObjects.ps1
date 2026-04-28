# Script Database Objects (Views, Procedures, Functions, Triggers)
# Uses OBJECT_DEFINITION to get SQL definitions

param(
    [string]$Database = "NS_CIS",
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$ObjectType = "VIEW"  # VIEW, PROCEDURE, FUNCTION, TRIGGER
)

# Continues past errors intentionally: scripts views/procs/functions/triggers from a DB; an inaccessible object must not abort scripting of the rest.
$ErrorActionPreference = "Continue"

function Get-ViewDefinitions {
    param([string]$Db, [string]$Instance)
    
    $query = @"
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME))) AS Definition
FROM INFORMATION_SCHEMA.VIEWS
WHERE TABLE_NAME != 'sysdiagrams'
ORDER BY TABLE_SCHEMA, TABLE_NAME
"@
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $outputFile = [System.IO.Path]::GetTempFileName()
    $query | Out-File -FilePath $tempFile -Encoding UTF8
    
    sqlcmd -S $Instance -E -d $Db -i $tempFile -W -h -1 -o $outputFile 2>&1 | Out-Null
    Remove-Item $tempFile -Force
    
    $results = @()
    if ($LASTEXITCODE -eq 0) {
        $content = Get-Content $outputFile -Raw
        # Parse results (simplified - OBJECT_DEFINITION returns full CREATE VIEW statement)
        # For now, return raw content for processing by caller
        $results = $content
    }
    
    Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
    return $results
}

function Get-ProcedureDefinitions {
    param([string]$Db, [string]$Instance)
    
    $query = @"
SELECT 
    ROUTINE_SCHEMA,
    ROUTINE_NAME,
    OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(ROUTINE_SCHEMA) + '.' + QUOTENAME(ROUTINE_NAME))) AS Definition
FROM INFORMATION_SCHEMA.ROUTINES
WHERE ROUTINE_TYPE = 'PROCEDURE'
ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME
"@
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $outputFile = [System.IO.Path]::GetTempFileName()
    $query | Out-File -FilePath $tempFile -Encoding UTF8
    
    sqlcmd -S $Instance -E -d $Db -i $tempFile -W -h -1 -o $outputFile 2>&1 | Out-Null
    Remove-Item $tempFile -Force
    
    $results = @()
    if ($LASTEXITCODE -eq 0) {
        $content = Get-Content $outputFile -Raw
        $results = $content
    }
    
    Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
    return $results
}

function Get-FunctionDefinitions {
    param([string]$Db, [string]$Instance)
    
    $query = @"
SELECT 
    ROUTINE_SCHEMA,
    ROUTINE_NAME,
    OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(ROUTINE_SCHEMA) + '.' + QUOTENAME(ROUTINE_NAME))) AS Definition
FROM INFORMATION_SCHEMA.ROUTINES
WHERE ROUTINE_TYPE = 'FUNCTION'
ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME
"@
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $outputFile = [System.IO.Path]::GetTempFileName()
    $query | Out-File -FilePath $tempFile -Encoding UTF8
    
    sqlcmd -S $Instance -E -d $Db -i $tempFile -W -h -1 -o $outputFile 2>&1 | Out-Null
    Remove-Item $tempFile -Force
    
    $results = @()
    if ($LASTEXITCODE -eq 0) {
        $content = Get-Content $outputFile -Raw
        $results = $content
    }
    
    Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
    return $results
}

function Get-TriggerDefinitions {
    param([string]$Db, [string]$Instance)
    
    $query = @"
SELECT 
    s.name AS SchemaName,
    t.name AS TableName,
    tr.name AS TriggerName,
    OBJECT_DEFINITION(tr.object_id) AS Definition
FROM sys.triggers tr
INNER JOIN sys.tables t ON tr.parent_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE tr.parent_class = 1
ORDER BY s.name, t.name, tr.name
"@
    
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $outputFile = [System.IO.Path]::GetTempFileName()
    $query | Out-File -FilePath $tempFile -Encoding UTF8
    
    sqlcmd -S $Instance -E -d $Db -i $tempFile -W -h -1 -o $outputFile 2>&1 | Out-Null
    Remove-Item $tempFile -Force
    
    $results = @()
    if ($LASTEXITCODE -eq 0) {
        $content = Get-Content $outputFile -Raw
        $results = $content
    }
    
    Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
    return $results
}

# Main execution
switch ($ObjectType.ToUpper()) {
    "VIEW" { Get-ViewDefinitions -Db $Database -Instance $SourceInstance }
    "PROCEDURE" { Get-ProcedureDefinitions -Db $Database -Instance $SourceInstance }
    "FUNCTION" { Get-FunctionDefinitions -Db $Database -Instance $SourceInstance }
    "TRIGGER" { Get-TriggerDefinitions -Db $Database -Instance $SourceInstance }
    default { Write-Error "Invalid ObjectType. Must be VIEW, PROCEDURE, FUNCTION, or TRIGGER" }
}

