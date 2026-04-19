# Script Foreign Keys for a table
# Generates ALTER TABLE statements to create foreign key constraints

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$SourceInstance = "localhost\NS_CIS"
)

$ErrorActionPreference = "Stop"

$query = @"
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
            FOR XML PATH('')
        ), 1, 2, '')
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
            FOR XML PATH('')
        ), 1, 2, '')
    ) AS ReferencedColumns,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction
FROM sys.foreign_keys fk
WHERE fk.parent_object_id = OBJECT_ID('[$Schema].[$TableName]')
ORDER BY fk.name
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
$outputFile = [System.IO.Path]::GetTempFileName()
$query | Out-File -FilePath $tempFile -Encoding UTF8

$result = sqlcmd -S $SourceInstance -E -d $Database -i $tempFile -W -h -1 -o $outputFile 2>&1
Remove-Item $tempFile -Force

if ($LASTEXITCODE -ne 0) {
    Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
    return @()
}

$lines = Get-Content $outputFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
Remove-Item $outputFile -Force

$fkScripts = @()
foreach ($line in $lines) {
    $parts = $line -split '\s+', 8
    if ($parts.Length -ge 8) {
        $fkName = $parts[0]
        $parentSchema = $parts[1]
        $parentTable = $parts[2]
        $parentCols = $parts[3]
        $refSchema = $parts[4]
        $refTable = $parts[5]
        $refCols = $parts[6]
        $deleteAction = $parts[7]
        $updateAction = if ($parts.Length -ge 9) { $parts[8] } else { "NO_ACTION" }
        
        $deleteClause = switch ($deleteAction) {
            "CASCADE" { "ON DELETE CASCADE" }
            "SET_NULL" { "ON DELETE SET NULL" }
            "SET_DEFAULT" { "ON DELETE SET DEFAULT" }
            default { "" }
        }
        
        $updateClause = switch ($updateAction) {
            "CASCADE" { "ON UPDATE CASCADE" }
            "SET_NULL" { "ON UPDATE SET NULL" }
            "SET_DEFAULT" { "ON UPDATE SET DEFAULT" }
            default { "" }
        }
        
        $actionClause = if ($deleteClause -or $updateClause) {
            "$deleteClause $updateClause".Trim()
        } else {
            ""
        }
        
        $fkScript = "ALTER TABLE [$parentSchema].[$parentTable] ADD CONSTRAINT [$fkName] FOREIGN KEY ($parentCols) REFERENCES [$refSchema].[$refTable] ($refCols)"
        if ($actionClause) {
            $fkScript += " $actionClause"
        }
        
        $fkScripts += $fkScript
    }
}

return $fkScripts

