# PowerShell script to run Check-QueueItems.sql diagnostic queries
# Runs all 5 diagnostic queries against NS_CIS database

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: loops over 5 independent diagnostic queries from the .sql file; one bad query must not skip the rest.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Container Scan Queue Diagnostic Queries" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $Server" -ForegroundColor Yellow
Write-Host "Database: $Database" -ForegroundColor Yellow
Write-Host ""

$sqlFile = Join-Path $PSScriptRoot "Check-QueueItems.sql"

if (-not (Test-Path $sqlFile)) {
    Write-Host "❌ SQL file not found: $sqlFile" -ForegroundColor Red
    exit 1
}

# Read SQL file
$sqlContent = Get-Content $sqlFile -Raw

# Split by query separators (comments with =========)
$queries = $sqlContent -split '(?=-- ============================================)'

$queryNames = @(
    "Query 1: Queue items status and retry counts",
    "Query 2: Queue items with completeness status records",
    "Query 3: Queue statistics summary",
    "Query 4: Recently completed items",
    "Query 5: Count items by retry status"
)

$queryIndex = 0
foreach ($query in $queries) {
    $query = $query.Trim()
    if ([string]::IsNullOrWhiteSpace($query)) {
        continue
    }
    
    # Skip comment-only sections
    if ($query -match '^--') {
        continue
    }
    
    if ($queryIndex -lt $queryNames.Length) {
        Write-Host "=== $($queryNames[$queryIndex]) ===" -ForegroundColor Green
        Write-Host ""
    }
    
    try {
        # Use sqlcmd to execute query
        $output = sqlcmd -S $Server -E -d $Database -Q $query -W -h -1 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            # Filter out metadata lines
            $output | Where-Object { 
                $_ -notmatch '^\(\d+ rows? affected\)' -and 
                $_.Trim() -ne '' -and
                $_ -notmatch '^Changed database context' 
            } | ForEach-Object { Write-Host $_ }
        } else {
            Write-Host "❌ Query execution failed" -ForegroundColor Red
            $output | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        }
    }
    catch {
        Write-Host "❌ Error executing query: $_" -ForegroundColor Red
    }
    
    Write-Host ""
    $queryIndex++
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Diagnostic queries completed" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

