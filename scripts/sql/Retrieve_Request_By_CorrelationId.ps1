# PowerShell script to retrieve request by Correlation ID
# Request ID: d9efc524-411d-44af-92e3-dc58fefc51bc

param(
    [string]$CorrelationId = "d9efc524-411d-44af-92e3-dc58fefc51bc",
    [string]$Database = "NS_CIS",
    [string]$Instance = "(local)"
)

$ErrorActionPreference = "Stop"

Write-Host "Retrieving request with Correlation ID: $CorrelationId" -ForegroundColor Cyan
Write-Host "Database: $Database" -ForegroundColor Cyan
Write-Host "Instance: $Instance" -ForegroundColor Cyan
Write-Host ""

# Query 1: Get all endpoint usage logs
Write-Host "=== All Request Records ===" -ForegroundColor Yellow
$query1 = @"
SELECT 
    Id,
    Endpoint,
    Method,
    StatusCode,
    ResponseTimeMs,
    IpAddress,
    UserAgent,
    Timestamp,
    IsDeprecated,
    IsPhase3Route,
    CorrelationId
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = '$CorrelationId'
ORDER BY 
    Timestamp ASC
"@

$result1 = sqlcmd -S $Instance -E -d $Database -Q $query1 -W -h -1
if ($result1) {
    $result1 | ForEach-Object { Write-Host $_ }
} else {
    Write-Host "  No records found with this Correlation ID" -ForegroundColor Red
}

Write-Host ""

# Query 2: Get summary statistics
Write-Host "=== Request Summary ===" -ForegroundColor Yellow
$query2 = @"
SELECT 
    COUNT(*) AS TotalRequests,
    MIN(Timestamp) AS FirstRequest,
    MAX(Timestamp) AS LastRequest,
    AVG(ResponseTimeMs) AS AvgResponseTimeMs,
    SUM(CASE WHEN StatusCode >= 200 AND StatusCode < 300 THEN 1 ELSE 0 END) AS SuccessCount,
    SUM(CASE WHEN StatusCode >= 400 AND StatusCode < 500 THEN 1 ELSE 0 END) AS ClientErrorCount,
    SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS ServerErrorCount
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = '$CorrelationId'
"@

$result2 = sqlcmd -S $Instance -E -d $Database -Q $query2 -W -h -1
if ($result2) {
    $result2 | ForEach-Object { Write-Host $_ }
} else {
    Write-Host "  No summary data available" -ForegroundColor Red
}

Write-Host ""

# Query 3: Get detailed breakdown by endpoint
Write-Host "=== Endpoint Breakdown ===" -ForegroundColor Yellow
$query3 = @"
SELECT 
    Endpoint,
    Method,
    StatusCode,
    COUNT(*) AS RequestCount,
    AVG(ResponseTimeMs) AS AvgResponseTimeMs,
    MIN(ResponseTimeMs) AS MinResponseTimeMs,
    MAX(ResponseTimeMs) AS MaxResponseTimeMs
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = '$CorrelationId'
GROUP BY 
    Endpoint, Method, StatusCode
ORDER BY 
    Endpoint, Method, StatusCode
"@

$result3 = sqlcmd -S $Instance -E -d $Database -Q $query3 -W -h -1
if ($result3) {
    $result3 | ForEach-Object { Write-Host $_ }
} else {
    Write-Host "  No endpoint breakdown available" -ForegroundColor Red
}

Write-Host ""
Write-Host "Query completed!" -ForegroundColor Green


