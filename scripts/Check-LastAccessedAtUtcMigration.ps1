# Check if LastAccessedAtUtc migration has been applied
# This script checks both the migration history and the actual column existence

param(
    [string]$ServerName = "localhost",
    [string]$DatabaseName = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  LastAccessedAtUtc Migration Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$connectionString = "Server=$ServerName;Database=$DatabaseName;Trusted_Connection=true;TrustServerCertificate=true;"

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "[OK] Connected to database: $DatabaseName" -ForegroundColor Green
    Write-Host ""

    # Check 1: Migration History
    Write-Host "1. Checking Migration History..." -ForegroundColor Yellow
    $migrationQuery = "IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory') BEGIN SELECT CASE WHEN EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251225205651_AddLastAccessedAtUtcToAnalysisAssignments') THEN 'FOUND' ELSE 'NOT_FOUND' END AS MigrationStatus END ELSE BEGIN SELECT 'TABLE_NOT_EXISTS' AS MigrationStatus END"
    
    $migrationCommand = $connection.CreateCommand()
    $migrationCommand.CommandText = $migrationQuery
    $migrationStatus = $migrationCommand.ExecuteScalar()
    
    if ($migrationStatus -eq 'FOUND') {
        Write-Host "   [OK] Migration found in __EFMigrationsHistory" -ForegroundColor Green
    } elseif ($migrationStatus -eq 'NOT_FOUND') {
        Write-Host "   [X] Migration NOT found in __EFMigrationsHistory" -ForegroundColor Red
    } else {
        Write-Host "   [!] Migration history table not found" -ForegroundColor Yellow
    }
    Write-Host ""

    # Check 2: Column Existence
    Write-Host "2. Checking Column Existence..." -ForegroundColor Yellow
    $columnQuery = "IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AnalysisAssignments]') AND name = 'LastAccessedAtUtc') SELECT 'EXISTS' AS ColumnStatus ELSE SELECT 'NOT_EXISTS' AS ColumnStatus"
    
    $columnCommand = $connection.CreateCommand()
    $columnCommand.CommandText = $columnQuery
    $columnStatus = $columnCommand.ExecuteScalar()
    
    if ($columnStatus -eq 'EXISTS') {
        Write-Host "   [OK] LastAccessedAtUtc column EXISTS in AnalysisAssignments table" -ForegroundColor Green
    } else {
        Write-Host "   [X] LastAccessedAtUtc column NOT FOUND in AnalysisAssignments table" -ForegroundColor Red
    }
    Write-Host ""

    # Check 3: Sample Data (if column exists)
    if ($columnStatus -eq 'EXISTS') {
        Write-Host "3. Checking Sample Data..." -ForegroundColor Yellow
        $dataQuery = "SELECT COUNT(*) AS TotalAssignments, COUNT(LastAccessedAtUtc) AS AssignmentsWithLastAccessed, MAX(LastAccessedAtUtc) AS MostRecentAccess FROM AnalysisAssignments"
        
        $dataCommand = $connection.CreateCommand()
        $dataCommand.CommandText = $dataQuery
        $dataReader = $dataCommand.ExecuteReader()
        
        if ($dataReader.Read()) {
            $total = $dataReader["TotalAssignments"]
            $withAccess = $dataReader["AssignmentsWithLastAccessed"]
            $mostRecent = $dataReader["MostRecentAccess"]
            
            Write-Host "   Total Assignments: $total" -ForegroundColor Cyan
            Write-Host "   Assignments with LastAccessedAtUtc: $withAccess" -ForegroundColor Cyan
            if ($mostRecent -ne [DBNull]::Value) {
                Write-Host "   Most Recent Access: $mostRecent" -ForegroundColor Cyan
            } else {
                Write-Host "   Most Recent Access: (none)" -ForegroundColor Gray
            }
        }
        $dataReader.Close()
        Write-Host ""
    }

    # Summary
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Summary" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    if ($migrationStatus -eq 'FOUND' -and $columnStatus -eq 'EXISTS') {
        Write-Host "[OK] Migration Status: APPLIED" -ForegroundColor Green
        Write-Host "[OK] Column Status: EXISTS" -ForegroundColor Green
        Write-Host ""
        Write-Host "[SUCCESS] Migration is fully applied and working!" -ForegroundColor Green
    } elseif ($columnStatus -eq 'EXISTS' -and $migrationStatus -eq 'NOT_FOUND') {
        Write-Host "[!] Column exists but migration not in history" -ForegroundColor Yellow
        Write-Host "  (Column may have been added manually)" -ForegroundColor Yellow
    } elseif ($migrationStatus -eq 'FOUND' -and $columnStatus -eq 'NOT_EXISTS') {
        Write-Host "[X] Migration in history but column missing" -ForegroundColor Red
        Write-Host "  (Migration may have failed or been rolled back)" -ForegroundColor Red
    } else {
        Write-Host "[X] Migration NOT applied" -ForegroundColor Red
        Write-Host ""
        Write-Host "To apply the migration, run:" -ForegroundColor Yellow
        Write-Host "  dotnet ef database update --project src/NickScanCentralImagingPortal.Infrastructure/NickScanCentralImagingPortal.Infrastructure.csproj --startup-project src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj --context ApplicationDbContext" -ForegroundColor White
        Write-Host ""
        Write-Host "Or apply manually:" -ForegroundColor Yellow
        Write-Host "  ALTER TABLE AnalysisAssignments ADD LastAccessedAtUtc DATETIME2 NULL;" -ForegroundColor White
    }
    
    $connection.Close()
}
catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

