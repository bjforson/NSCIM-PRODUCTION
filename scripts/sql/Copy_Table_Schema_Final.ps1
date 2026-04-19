# Final table schema copy - simplified and reliable

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
    
    # Get basic column metadata
    $basicQuery = "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName' ORDER BY ORDINAL_POSITION"
    $queryFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $basicQuery | Out-File -FilePath $queryFile -Encoding UTF8
    
    $columnsFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $Database -i $queryFile -W -h -1 -o $columnsFile 2>&1 | Out-Null
    Remove-Item $queryFile -Force
    
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $columnsFile -Force -ErrorAction SilentlyContinue
        throw "Failed to get column metadata"
    }
    
    $columnLines = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $columnsFile -Force
    
    # Get identity column info separately
    $identityQuery = "SELECT name, seed_value, increment_value FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Database].[$Schema].[$TableName]')"
    $idQueryFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $identityQuery | Out-File -FilePath $idQueryFile -Encoding UTF8
    
    $identityFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $Database -i $idQueryFile -W -h -1 -o $identityFile 2>&1 | Out-Null
    Remove-Item $idQueryFile -Force
    
    $identityMap = @{}
    if ($LASTEXITCODE -eq 0) {
        $identityLines = Get-Content $identityFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
        foreach ($idLine in $identityLines) {
            $idParts = $idLine -split '\s+', 3
            if ($idParts.Length -ge 3) {
                $identityMap[$idParts[0]] = @{ Seed = $idParts[1]; Increment = $idParts[2] }
            }
        }
    }
    Remove-Item $identityFile -Force -ErrorAction SilentlyContinue
    
    # Parse columns and build CREATE TABLE
    $columnDefs = @()
    foreach ($line in $columnLines) {
        $parts = $line -split '\s+', 8
        if ($parts.Length -ge 4) {
            $colName = $parts[0]
            $dataType = $parts[1]
            $maxLength = if ($parts.Length -ge 3 -and $parts[2] -ne 'NULL' -and $parts[2] -match '^-?\d+$') { $parts[2] } else { $null }
            $precision = if ($parts.Length -ge 4 -and $parts[3] -ne 'NULL' -and $parts[3] -match '^\d+$') { $parts[3] } else { $null }
            $scale = if ($parts.Length -ge 5 -and $parts[4] -ne 'NULL' -and $parts[4] -match '^\d+$') { $parts[4] } else { $null }
            $isNullable = if ($parts.Length -ge 6) { $parts[5] } else { "YES" }
            $default = if ($parts.Length -ge 7 -and $parts[6] -ne 'NULL' -and [string]::IsNullOrWhiteSpace($parts[6]) -eq $false) { $parts[6] } else { $null }
            
            # Build type definition - only add length/precision for types that support it
            $typeDef = $dataType
            $typesWithLength = @('varchar', 'nvarchar', 'char', 'nchar', 'varbinary', 'binary')
            $typesWithPrecision = @('decimal', 'numeric', 'float', 'real')
            
            if ($typesWithLength -contains $dataType.ToLower() -and $maxLength -and $maxLength -ne 'NULL') {
                if ($maxLength -eq '-1') {
                    $typeDef += '(MAX)'
                } else {
                    $typeDef += "($maxLength)"
                }
            } elseif ($typesWithPrecision -contains $dataType.ToLower() -and $precision -and $precision -ne 'NULL') {
                if ($scale -and $scale -ne 'NULL' -and $scale -ne '0') {
                    $typeDef += "($precision,$scale)"
                } else {
                    $typeDef += "($precision)"
                }
            }
            
            # Build column definition
            $colDef = "[$colName] $typeDef"
            
            # Add identity if this column has identity
            if ($identityMap.ContainsKey($colName)) {
                $idInfo = $identityMap[$colName]
                $colDef += " IDENTITY($($idInfo.Seed),$($idInfo.Increment))"
            }
            
            # Add nullability
            if ($isNullable -eq 'NO') {
                $colDef += " NOT NULL"
            } else {
                $colDef += " NULL"
            }
            
            # Add default (handle function calls like getutcdate())
            if ($default -and $default -ne 'NULL') {
                if ($default -match '^\(.*\)$') {
                    $colDef += " DEFAULT $default"
                } else {
                    $colDef += " DEFAULT ($default)"
                }
            }
            
            $columnDefs += $colDef
        }
    }
    
    if ($columnDefs.Count -eq 0) {
        throw "No valid column definitions found"
    }
    
    # Build CREATE TABLE statement
    $createTable = "CREATE TABLE [$Database].[$Schema].[$TableName] (`n    " + ($columnDefs -join ",`n    ") + "`n);"
    
    Write-Host " Done" -ForegroundColor Green
    
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

