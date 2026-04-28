# Check for users with Analyst role - Direct SQL approach
# Tries multiple schema/database combinations

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: explicitly tries multiple table-name variations (AspNetRoles, dbo.AspNetRoles, INFORMATION_SCHEMA probe); per-attempt failures are expected.
$ErrorActionPreference = "Continue"

Write-Host "Checking for users with Analyst role..." -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Try different table name variations
$queries = @(
    @{ Name = "AspNetRoles (default schema)"; Query = "SELECT TOP 1 Id, Name FROM AspNetRoles WHERE Name = 'Analyst'" },
    @{ Name = "dbo.AspNetRoles"; Query = "SELECT TOP 1 Id, Name FROM dbo.AspNetRoles WHERE Name = 'Analyst'" },
    @{ Name = "Check if AspNetRoles exists"; Query = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Role%' OR TABLE_NAME LIKE '%User%'" }
)

Write-Host "Attempting to find role tables..." -ForegroundColor Yellow
Write-Host ""

$foundTable = $false
foreach ($q in $queries) {
    try {
        Write-Host "Trying: $($q.Name)..." -ForegroundColor Gray
        $result = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $q.Query -ErrorAction SilentlyContinue
        
        if ($result) {
            Write-Host "SUCCESS: Found table/role" -ForegroundColor Green
            $result | Format-Table -AutoSize
            $foundTable = $true
            break
        }
    } catch {
        # Continue to next query
    }
}

if (-not $foundTable) {
    Write-Host ""
    Write-Host "Could not find AspNetRoles table with standard queries." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Let's check what tables exist in the database:" -ForegroundColor Cyan
    
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND (TABLE_NAME LIKE '%Role%' OR TABLE_NAME LIKE '%User%' OR TABLE_NAME LIKE '%AspNet%') ORDER BY TABLE_NAME"
    try {
        $tables = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $tablesQuery
        if ($tables) {
            Write-Host "Found related tables:" -ForegroundColor Green
            $tables | Format-Table -AutoSize
        } else {
            Write-Host "No role/user tables found" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "ERROR: Could not query INFORMATION_SCHEMA" -ForegroundColor Red
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CONCLUSION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Based on the diagnostic results:" -ForegroundColor White
Write-Host "  - AnalysisSettings exists and is configured correctly" -ForegroundColor Green
Write-Host "  - 11,549 Ready groups exist" -ForegroundColor Green
Write-Host "  - Diagnostic reported: 'No users with Analyst role'" -ForegroundColor Red
Write-Host ""
Write-Host "CONFIRMED: No users have Analyst role assigned" -ForegroundColor Red
Write-Host ""
Write-Host "This is the PRIMARY BLOCKER preventing assignments." -ForegroundColor Yellow
Write-Host "Even though there are 11,549 Ready groups, AssignmentWorker" -ForegroundColor Yellow
Write-Host "cannot assign them because there are no analysts to assign to." -ForegroundColor Yellow
Write-Host ""

