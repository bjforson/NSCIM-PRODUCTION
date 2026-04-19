# Check Recent Errors from ApplicationLogs
param(
    [int]$Hours = 24
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Recent Error Check (Last $Hours hours)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "❌ Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection

if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Host "❌ Error: Connection string not found in appsettings.json" -ForegroundColor Red
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "❌ Error connecting to database: $_" -ForegroundColor Red
    exit 1
}

# Check if ApplicationLogs table exists
$tableCheckQuery = @"
SELECT COUNT(*) AS TableExists
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'ApplicationLogs'
"@

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($tableCheckQuery, $connection)
    $tableExists = [int]$command.ExecuteScalar()
    
    if ($tableExists -eq 0) {
        Write-Host "⚠️  ApplicationLogs table does not exist. Checking other error sources..." -ForegroundColor Yellow
        Write-Host ""
        
        # Check for duplicate download errors in BOEDocuments (from monitoring service)
        Write-Host "Checking for duplicate download monitoring errors..." -ForegroundColor Yellow
        $duplicateQuery = @"
        SELECT TOP 20
            Id,
            ContainerNumber,
            DeclarationNumber,
            CreatedAt,
            DownloadedFileId
        FROM BOEDocuments
        WHERE CreatedAt >= DATEADD(HOUR, -$Hours, GETUTCDATE())
        ORDER BY CreatedAt DESC
"@
        $command = New-Object System.Data.SqlClient.SqlCommand($duplicateQuery, $connection)
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
        $dataset = New-Object System.Data.DataSet
        $adapter.Fill($dataset) | Out-Null
        
        if ($dataset.Tables[0].Rows.Count -gt 0) {
            Write-Host "   Found $($dataset.Tables[0].Rows.Count) recent BOE documents" -ForegroundColor White
        } else {
            Write-Host "   No recent BOE documents found" -ForegroundColor Gray
        }
        
        $connection.Close()
        exit 0
    }
} catch {
    Write-Host "❌ Error checking for ApplicationLogs table: $_" -ForegroundColor Red
    $connection.Close()
    exit 1
}

# Query recent errors from ApplicationLogs
$errorQuery = @"
SELECT TOP 50
    Id,
    Timestamp,
    Level,
    ServiceId,
    Operation,
    Message,
    Exception,
    CreatedAt
FROM ApplicationLogs
WHERE Timestamp >= DATEADD(HOUR, -$Hours, GETUTCDATE())
  AND (Level IN ('Error', 'Critical', 'Fatal') OR Message LIKE '%error%' OR Message LIKE '%exception%' OR Message LIKE '%failed%')
ORDER BY Timestamp DESC
"@

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($errorQuery, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    if ($dataset.Tables[0].Rows.Count -gt 0) {
        Write-Host "Found $($dataset.Tables[0].Rows.Count) error(s) in the last $Hours hours:" -ForegroundColor $(if ($dataset.Tables[0].Rows.Count -gt 0) { "Red" } else { "Green" })
        Write-Host ""
        
        foreach ($row in $dataset.Tables[0].Rows) {
            $level = $row["Level"]
            $timestamp = $row["Timestamp"]
            $service = $row["ServiceId"]
            $operation = $row["Operation"]
            $message = $row["Message"]
            $exception = $row["Exception"]
            
            $color = switch ($level) {
                "Error" { "Red" }
                "Critical" { "Red" }
                "Fatal" { "Red" }
                default { "Yellow" }
            }
            
            Write-Host "[$timestamp] [$level]" -ForegroundColor $color -NoNewline
            if (![string]::IsNullOrEmpty($service)) {
                Write-Host " [$service]" -ForegroundColor Cyan -NoNewline
            }
            if (![string]::IsNullOrEmpty($operation)) {
                Write-Host " [$operation]" -ForegroundColor Gray -NoNewline
            }
            Write-Host ""
            Write-Host "  $message" -ForegroundColor White
            if (![string]::IsNullOrEmpty($exception)) {
                Write-Host "  Exception: $exception" -ForegroundColor Red
            }
            Write-Host ""
        }
    } else {
        Write-Host "✅ No errors found in ApplicationLogs for the last $Hours hours" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ Error querying ApplicationLogs: $_" -ForegroundColor Red
} finally {
    $connection.Close()
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan

