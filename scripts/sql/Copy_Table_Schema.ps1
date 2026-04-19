# Copy table schema from source to target
# Creates the table structure on target if it doesn't exist

param(
    [string]$TableName,
    [string]$Schema = "dbo",
    [string]$Database = "NS_CIS",
    [string]$TargetInstance = "(local)",
    [string]$SourceInstance = "localhost\NS_CIS"
)

$ErrorActionPreference = "Stop"

Write-Host "Copying schema: [$Database].[$Schema].[$TableName]" -ForegroundColor Cyan
Write-Host "  Source: $SourceInstance" -ForegroundColor Gray
Write-Host "  Target: $TargetInstance" -ForegroundColor Gray

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
        Write-Host "  Table already exists on target - skipping" -ForegroundColor Yellow
        return
    }
    
    # Generate CREATE TABLE script from source
    Write-Host "  Generating CREATE TABLE script..." -NoNewline -ForegroundColor Gray
    
    $scriptQuery = @"
SELECT 
    'CREATE TABLE [$Database].[$Schema].[$TableName] (' + CHAR(13) + CHAR(10) +
    STUFF((
        SELECT ', ' + CHAR(13) + CHAR(10) + '    [' + c.COLUMN_NAME + '] ' + 
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
            CASE WHEN c.IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE ' NULL' END +
            CASE WHEN ic.COLUMN_NAME IS NOT NULL THEN ' IDENTITY(' + CAST(ic.SEED_VALUE AS VARCHAR) + ',' + CAST(ic.INCREMENT_VALUE AS VARCHAR) + ')' ELSE '' END +
            CASE WHEN c.COLUMN_DEFAULT IS NOT NULL THEN ' DEFAULT ' + c.COLUMN_DEFAULT ELSE '' END
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]') AND ic.name = c.COLUMN_NAME
        WHERE c.TABLE_SCHEMA = '$Schema' AND c.TABLE_NAME = '$TableName'
        ORDER BY c.ORDINAL_POSITION
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') +
    CHAR(13) + CHAR(10) + ');'
"@
    
    $scriptFile = [System.IO.Path]::GetTempFileName() + ".sql"
    sqlcmd -S $SourceInstance -E -d $Database -Q $scriptQuery -W -h -1 -o $scriptFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate CREATE TABLE script from source"
    }
    
    $createScript = Get-Content $scriptFile -Raw
    Remove-Item $scriptFile -Force
    
    # Clean up the script (remove XML encoding artifacts)
    $createScript = $createScript -replace '&lt;', '<' -replace '&gt;', '>' -replace '&amp;', '&'
    $createScript = ($createScript -split "`n" | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }) -join "`n"
    
    if ([string]::IsNullOrWhiteSpace($createScript) -or $createScript -notmatch 'CREATE TABLE') {
        # Fallback: Use simpler approach with SMO or direct SQL
        Write-Host " Using alternative method..." -ForegroundColor Gray
        
        # Get column definitions
        $columnsQuery = @"
SELECT 
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE,
    c.IS_NULLABLE,
    c.COLUMN_DEFAULT,
    CASE WHEN ic.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_IDENTITY,
    ic.SEED_VALUE,
    ic.INCREMENT_VALUE
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]') AND ic.name = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = '$Schema' AND c.TABLE_NAME = '$TableName'
ORDER BY c.ORDINAL_POSITION
"@
        
        $columnsFile = [System.IO.Path]::GetTempFileName()
        sqlcmd -S $SourceInstance -E -d $Database -Q $columnsQuery -W -h -1 -o $columnsFile 2>&1 | Out-Null
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to get column definitions"
        }
        
        $columns = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
        Remove-Item $columnsFile -Force
        
        if (-not $columns -or $columns.Count -eq 0) {
            throw "No columns found for table"
        }
        
        # Build CREATE TABLE statement
        $columnDefs = @()
        foreach ($colLine in $columns) {
            $parts = $colLine -split '\s+', 11
            if ($parts.Length -ge 10) {
                $colName = $parts[0]
                $dataType = $parts[1]
                $maxLength = if ($parts[2] -eq 'NULL') { $null } else { $parts[2] }
                $precision = if ($parts[3] -eq 'NULL') { $null } else { $parts[3] }
                $scale = if ($parts[4] -eq 'NULL') { $null } else { $parts[4] }
                $isNullable = $parts[5]
                $default = if ($parts[6] -ne 'NULL' -and $parts[6] -ne '') { $parts[6] } else { $null }
                $isIdentity = if ($parts[7] -eq '1') { $true } else { $false }
                $seed = if ($parts[8] -ne 'NULL') { $parts[8] } else { $null }
                $increment = if ($parts[9] -ne 'NULL') { $parts[9] } else { $null }
                
                $typeDef = $dataType
                if ($maxLength -and $maxLength -ne 'NULL') {
                    if ($maxLength -eq '-1') {
                        $typeDef += '(MAX)'
                    } else {
                        $typeDef += "($maxLength)"
                    }
                } elseif ($precision -and $precision -ne 'NULL') {
                    if ($scale -and $scale -ne 'NULL') {
                        $typeDef += "($precision,$scale)"
                    } else {
                        $typeDef += "($precision)"
                    }
                }
                
                $colDef = "[$colName] $typeDef"
                if ($isIdentity) {
                    $colDef += " IDENTITY($seed,$increment)"
                }
                if ($isNullable -eq 'NO') {
                    $colDef += " NOT NULL"
                } else {
                    $colDef += " NULL"
                }
                if ($default) {
                    $colDef += " DEFAULT $default"
                }
                
                $columnDefs += $colDef
            }
        }
        
        $createScript = "CREATE TABLE [$Database].[$Schema].[$TableName] (`n    " + ($columnDefs -join ",`n    ") + "`n);"
    }
    
    Write-Host " ✓" -ForegroundColor Green
    
    # Execute CREATE TABLE on target
    Write-Host "  Creating table on target..." -NoNewline -ForegroundColor Gray
    
    $createFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $createScript | Out-File -FilePath $createFile -Encoding UTF8
    
    $result = sqlcmd -S $TargetInstance -E -d $Database -i $createFile -b 2>&1
    Remove-Item $createFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create table on target: $result"
    }
    
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "  Success! Table schema copied" -ForegroundColor Green
    
} catch {
    Write-Host "  ✗ Failed: $_" -ForegroundColor Red
    if ($_.Exception.Message) {
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    }
    exit 1
}

