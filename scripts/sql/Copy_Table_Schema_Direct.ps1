# Direct table schema copy - builds CREATE TABLE in PowerShell

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
    
    Write-Host "  Building CREATE TABLE statement..." -NoNewline -ForegroundColor Gray
    
    # Get column metadata from source - use file-based query to avoid quoting issues
    $queryFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $columnsQuery = "SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE, c.COLUMN_DEFAULT, CASE WHEN ic.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_IDENTITY, ic.seed_value, ic.increment_value FROM INFORMATION_SCHEMA.COLUMNS c LEFT JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]') AND ic.name = c.COLUMN_NAME WHERE c.TABLE_SCHEMA = '$Schema' AND c.TABLE_NAME = '$TableName' ORDER BY c.ORDINAL_POSITION"
    $columnsQuery | Out-File -FilePath $queryFile -Encoding UTF8
    
    $columnsFile = [System.IO.Path]::GetTempFileName()
    
    sqlcmd -S $SourceInstance -E -d $Database -i $queryFile -W -h -1 -o $columnsFile 2>&1 | Out-Null
    
    Remove-Item $queryFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $columnsFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get column metadata"
    }
    
    # Filter out header lines, error messages, and empty lines
    $columnLines = Get-Content $columnsFile | Where-Object { 
        $_ -match '\S' -and 
        $_ -notmatch 'rows affected' -and 
        $_ -notmatch '^---' -and
        $_ -notmatch '^Msg\s+\d+' -and
        $_ -notmatch '^Level\s+\d+' -and
        $_ -notmatch '^State\s+\d+' -and
        $_ -notmatch '^Server\s+' -and
        $_ -notmatch '^Line\s+\d+'
    }
    
    Remove-Item $columnsFile -Force
    
    if (-not $columnLines -or $columnLines.Count -eq 0) {
        throw "No columns found"
    }
    
    # Parse columns and build CREATE TABLE
    $columnDefs = @()
    foreach ($line in $columnLines) {
        $parts = $line -split '\s+', 11
        if ($parts.Length -ge 6) {
            $colName = $parts[0]
            $dataType = $parts[1]
            $maxLength = if ($parts.Length -ge 3 -and $parts[2] -ne 'NULL' -and [string]::IsNullOrWhiteSpace($parts[2]) -eq $false) { $parts[2] } else { $null }
            $precision = if ($parts.Length -ge 4 -and $parts[3] -ne 'NULL' -and [string]::IsNullOrWhiteSpace($parts[3]) -eq $false) { $parts[3] } else { $null }
            $scale = if ($parts.Length -ge 5 -and $parts[4] -ne 'NULL' -and [string]::IsNullOrWhiteSpace($parts[4]) -eq $false) { $parts[4] } else { $null }
            $isNullable = if ($parts.Length -ge 6) { $parts[5] } else { "YES" }
            
            # Default value might be in parts[6] or later, need to handle it carefully
            # If there are more parts, the default might be spread across multiple parts
            $default = $null
            if ($parts.Length -ge 7) {
                # Default might be in parts[6], but could be multi-word like "(getutcdate())"
                # Join from parts[6] onwards if it's not NULL and not empty
                $defaultParts = $parts[6..($parts.Length-1)]
                $defaultStr = ($defaultParts | Where-Object { $_ -ne 'NULL' -and [string]::IsNullOrWhiteSpace($_) -eq $false }) -join ' '
                if ($defaultStr -and $defaultStr -ne 'NULL') {
                    $default = $defaultStr
                }
            }
            
            # Identity info (if present)
            $isIdentity = $false
            $seed = $null
            $increment = $null
            # Identity columns would be in a separate query result, but for now assume not identity
            # We'll get identity info separately if needed
            
            # Build type definition
            $typeDef = $dataType
            if ($maxLength -and $maxLength -ne 'NULL') {
                if ($maxLength -eq '-1') {
                    $typeDef += '(MAX)'
                } else {
                    $typeDef += "($maxLength)"
                }
            } elseif ($precision -and $precision -ne 'NULL') {
                if ($scale -and $scale -ne 'NULL' -and $scale -ne '0') {
                    $typeDef += "($precision,$scale)"
                } else {
                    $typeDef += "($precision)"
                }
            }
            
            # Build column definition
            $colDef = "[$colName] $typeDef"
            
            # Add identity (will be added from separate query if needed)
            # For now, skip identity - we'll handle it separately
            
            # Add nullability
            if ($isNullable -eq 'NO') {
                $colDef += " NOT NULL"
            } else {
                $colDef += " NULL"
            }
            
            # Add default (only if it's a valid default value)
            if ($default -and $default -ne 'NULL' -and $default.Length -gt 0) {
                # If default contains parentheses or looks like a function, use as-is
                # Otherwise might need quoting for string literals
                if ($default -match '^\(.*\)$' -or $default -match '^[a-zA-Z_][a-zA-Z0-9_]*\(\)$') {
                    $colDef += " DEFAULT $default"
                } else {
                    # Might be a string literal or number - use as-is for now
                    $colDef += " DEFAULT $default"
                }
            }
            
            $columnDefs += $colDef
        }
    }
    
    # Now get identity column info separately and update column definitions
    $identityQuery = @"
SELECT 
    c.COLUMN_NAME,
    ic.seed_value,
    ic.increment_value
FROM INFORMATION_SCHEMA.COLUMNS c
INNER JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]') AND ic.name = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = '$Schema' AND c.TABLE_NAME = '$TableName'
ORDER BY c.ORDINAL_POSITION
"@
    
    $identityFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $Database -Q $identityQuery -W -h -1 -o $identityFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -eq 0) {
        $identityLines = Get-Content $identityFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
        Remove-Item $identityFile -Force
        
        # Update column definitions with identity info
        $updatedDefs = @()
        foreach ($colDef in $columnDefs) {
            if ($colDef -match '^\[(\w+)\]') {
                $colName = $Matches[1]
                $identityInfo = $identityLines | Where-Object { $_ -match "^$colName\s+" }
                
                if ($identityInfo) {
                    $idParts = $identityInfo -split '\s+', 3
                    if ($idParts.Length -ge 3) {
                        $seed = $idParts[1]
                        $increment = $idParts[2]
                        # Insert IDENTITY before NULL/NOT NULL
                        $colDef = $colDef -replace '(\s+(?:NOT\s+)?NULL)', " IDENTITY($seed,$increment)`$1"
                    }
                }
            }
            $updatedDefs += $colDef
        }
        $columnDefs = $updatedDefs
    } else {
        Remove-Item $identityFile -Force -ErrorAction SilentlyContinue
    }
    
    if ($columnDefs.Count -eq 0) {
        throw "No valid column definitions found"
    }
    
    # Build CREATE TABLE statement
    $createTable = "CREATE TABLE [$Database].[$Schema].[$TableName] (`n    " + ($columnDefs -join ",`n    ") + "`n);"
    
    Write-Host " Done" -ForegroundColor Green
    
    # Debug: Write CREATE TABLE to temp file for inspection
    $debugFile = Join-Path $env:TEMP "create_table_debug_$TableName.sql"
    $createTable | Out-File -FilePath $debugFile -Encoding UTF8
    Write-Host "  Debug: CREATE TABLE saved to $debugFile" -ForegroundColor Yellow
    
    # Execute CREATE TABLE on target
    Write-Host "  Creating table on target..." -NoNewline -ForegroundColor Gray
    
    $createFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $createTable | Out-File -FilePath $createFile -Encoding UTF8
    
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

