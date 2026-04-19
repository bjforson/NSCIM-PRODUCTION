# Simple table schema copy - uses SQL Server's built-in scripting
# More reliable than manual CREATE TABLE generation

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS"
)

$ErrorActionPreference = "Stop"

Write-Host "Copying schema: [$Database].[$Schema].[$TableName]" -ForegroundColor Cyan

try {
    # Check if table exists on target
    $checkQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName'"
    $checkFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $TargetInstance -E -d $Database -Q $checkQuery -W -h -1 -o $checkFile 2>&1 | Out-Null
    
    $exists = $false
    if ($LASTEXITCODE -eq 0) {
        $result = Get-Content $checkFile | Where-Object { $_ -match '^\s*[1-9]' }
        $exists = ($result -ne $null)
    }
    Remove-Item $checkFile -Force -ErrorAction SilentlyContinue
    
    if ($exists) {
        Write-Host "  Table already exists - skipping" -ForegroundColor Yellow
        return
    }
    
    # Use SQL Server's SELECT INTO to create table structure (no data)
    Write-Host "  Creating table structure..." -NoNewline -ForegroundColor Gray
    
    # First, get a sample query that will create the structure
    # We'll use a WHERE 1=0 trick to get structure only
    $createQuery = @"
SELECT TOP 0 * INTO [$Database].[$Schema].[$TableName] 
FROM [$SourceInstance].[$Database].[$Schema].[$TableName]
"@
    
    # Actually, we can't use cross-server SELECT INTO directly
    # Instead, use OPENROWSET or generate CREATE TABLE from source metadata
    
    # Better approach: Generate CREATE TABLE from INFORMATION_SCHEMA with identity support
    $metaQuery = @"
DECLARE @sql NVARCHAR(MAX) = 'CREATE TABLE [$Database].[$Schema].[$TableName] (';
SELECT @sql += CHAR(13) + CHAR(10) + '    [' + c.COLUMN_NAME + '] ' + 
    CASE 
        WHEN c.DATA_TYPE IN ('varchar', 'nvarchar', 'char', 'nchar') 
            THEN c.DATA_TYPE + '(' + 
                CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' 
                     ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) 
                END + ')'
        WHEN c.DATA_TYPE IN ('decimal', 'numeric')
            THEN c.DATA_TYPE + '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR) + ')'
        WHEN c.DATA_TYPE IN ('float', 'real')
            THEN c.DATA_TYPE + '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ')'
        ELSE c.DATA_TYPE
    END +
    CASE WHEN ic.COLUMN_NAME IS NOT NULL 
        THEN ' IDENTITY(' + CAST(ic.seed_value AS VARCHAR) + ',' + CAST(ic.increment_value AS VARCHAR) + ')' 
        ELSE '' 
    END +
    CASE WHEN c.IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE ' NULL' END +
    CASE WHEN c.COLUMN_DEFAULT IS NOT NULL THEN ' DEFAULT ' + c.COLUMN_DEFAULT ELSE '' END + ','
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]') AND ic.name = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = '$Schema' AND c.TABLE_NAME = '$TableName'
ORDER BY c.ORDINAL_POSITION;

-- Remove trailing comma
SET @sql = LEFT(@sql, LEN(@sql) - 1) + CHAR(13) + CHAR(10) + ');';

SELECT @sql AS CreateScript;
"@
    
    # Execute on source to get CREATE script
    $scriptFile = [System.IO.Path]::GetTempFileName() + ".sql"
    sqlcmd -S $SourceInstance -E -d $Database -Q $metaQuery -W -h -1 -o $scriptFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate CREATE TABLE script"
    }
    
    $createScript = Get-Content $scriptFile -Raw
    Remove-Item $scriptFile -Force
    
    # Clean up the script
    $createScript = ($createScript -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' -and $_ -notmatch '^$' }) -join "`n"
    
    if ([string]::IsNullOrWhiteSpace($createScript) -or $createScript -notmatch 'CREATE TABLE') {
        throw "Failed to generate valid CREATE TABLE script"
    }
    
    # Execute CREATE TABLE on target
    $createFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $createScript | Out-File -FilePath $createFile -Encoding UTF8
    
    $result = sqlcmd -S $TargetInstance -E -d $Database -i $createFile -b 2>&1
    Remove-Item $createFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create table: $result"
    }
    
    Write-Host " Done" -ForegroundColor Green
    Write-Host "  Success! Table schema copied" -ForegroundColor Green
    
} catch {
    Write-Host "  Failed: $_" -ForegroundColor Red
    if ($_.Exception.Message) {
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    }
    exit 1
}

