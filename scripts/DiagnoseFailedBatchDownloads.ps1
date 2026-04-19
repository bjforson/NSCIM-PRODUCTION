# Diagnostic script to investigate failed batch downloads
# This script analyzes the DownloadedFiles table to understand why files are failing

param(
    [string]$ConnectionStringName = "ICUMS_Downloads_Connection"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FAILED BATCH DOWNLOADS DIAGNOSTIC" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "ERROR: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.$ConnectionStringName

if (-not $connectionString) {
    Write-Host "ERROR: Connection string '$ConnectionStringName' not found in appsettings.json" -ForegroundColor Red
    exit 1
}

Write-Host "Connecting to database..." -ForegroundColor Yellow
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "OK: Connected to database" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "ERROR: Could not connect to database: $_" -ForegroundColor Red
    exit 1
}

# Query 1: Overall Statistics
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SECTION 1: OVERALL STATISTICS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query1 = "SELECT ProcessingStatus, COUNT(*) AS FileCount, SUM(RecordCount) AS TotalRecords, MIN(DownloadDate) AS EarliestDownload, MAX(DownloadDate) AS LatestDownload FROM DownloadedFiles GROUP BY ProcessingStatus ORDER BY FileCount DESC"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query1, $connection)
    $reader = $command.ExecuteReader()
    
    while ($reader.Read()) {
        $status = $reader["ProcessingStatus"]
        $count = $reader["FileCount"]
        $records = if ($reader["TotalRecords"] -ne [DBNull]::Value) { $reader["TotalRecords"] } else { 0 }
        $earliest = if ($reader["EarliestDownload"] -ne [DBNull]::Value) { $reader["EarliestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        $latest = if ($reader["LatestDownload"] -ne [DBNull]::Value) { $reader["LatestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        
        $color = switch ($status) {
            "Failed" { "Red" }
            "Completed" { "Green" }
            "Processing" { "Yellow" }
            "Pending" { "Cyan" }
            default { "White" }
        }
        
        Write-Host "Status: $status" -ForegroundColor $color -NoNewline
        Write-Host " | Files: $count" -ForegroundColor White -NoNewline
        Write-Host " | Records: $records" -ForegroundColor White -NoNewline
        Write-Host " | Range: $earliest to $latest" -ForegroundColor Gray
    }
    $reader.Close()
}
catch {
    Write-Host "ERROR executing query 1: $_" -ForegroundColor Red
}

Write-Host ""

# Query 2: Top 20 Error Messages
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SECTION 2: TOP 20 ERROR MESSAGES" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query2 = "SELECT TOP 20 ErrorMessage, COUNT(*) AS ErrorCount, MIN(DownloadDate) AS FirstOccurrence, MAX(DownloadDate) AS LastOccurrence FROM DownloadedFiles WHERE ProcessingStatus = 'Failed' AND ErrorMessage IS NOT NULL GROUP BY ErrorMessage ORDER BY ErrorCount DESC"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query2, $connection)
    $reader = $command.ExecuteReader()
    
    if (-not $reader.HasRows) {
        Write-Host "WARNING: No error messages found in failed files" -ForegroundColor Yellow
    }
    else {
        $index = 1
        while ($reader.Read()) {
            $errorMsg = $reader["ErrorMessage"]
            $count = $reader["ErrorCount"]
            $first = if ($reader["FirstOccurrence"] -ne [DBNull]::Value) { $reader["FirstOccurrence"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
            $last = if ($reader["LastOccurrence"] -ne [DBNull]::Value) { $reader["LastOccurrence"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
            
            Write-Host "#$index" -ForegroundColor Yellow -NoNewline
            Write-Host " | Count: $count" -ForegroundColor White -NoNewline
            Write-Host " | First: $first" -ForegroundColor Gray -NoNewline
            Write-Host " | Last: $last" -ForegroundColor Gray
            Write-Host "   Error: $errorMsg" -ForegroundColor Red
            Write-Host ""
            
            $index++
        }
    }
    $reader.Close()
}
catch {
    Write-Host "ERROR executing query 2: $_" -ForegroundColor Red
}

Write-Host ""

# Query 3: Failed Files by File Name Pattern
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SECTION 3: FAILED FILES BY PATTERN" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query3 = "SELECT CASE WHEN FileName LIKE 'BatchData_%' THEN 'BatchData' WHEN FileName LIKE 'Queue_%' THEN 'Queue' WHEN FileName LIKE 'OnDemand_%' THEN 'OnDemand' WHEN FileName LIKE 'ContainerData_%' THEN 'ContainerData' ELSE 'Other' END AS FilePattern, COUNT(*) AS FileCount, MIN(DownloadDate) AS EarliestDownload, MAX(DownloadDate) AS LatestDownload FROM DownloadedFiles WHERE ProcessingStatus = 'Failed' GROUP BY CASE WHEN FileName LIKE 'BatchData_%' THEN 'BatchData' WHEN FileName LIKE 'Queue_%' THEN 'Queue' WHEN FileName LIKE 'OnDemand_%' THEN 'OnDemand' WHEN FileName LIKE 'ContainerData_%' THEN 'ContainerData' ELSE 'Other' END ORDER BY FileCount DESC"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query3, $connection)
    $reader = $command.ExecuteReader()
    
    while ($reader.Read()) {
        $pattern = $reader["FilePattern"]
        $count = $reader["FileCount"]
        $earliest = if ($reader["EarliestDownload"] -ne [DBNull]::Value) { $reader["EarliestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        $latest = if ($reader["LatestDownload"] -ne [DBNull]::Value) { $reader["LatestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        
        Write-Host "Pattern: $pattern" -ForegroundColor Yellow -NoNewline
        Write-Host " | Files: $count" -ForegroundColor White -NoNewline
        Write-Host " | Range: $earliest to $latest" -ForegroundColor Gray
    }
    $reader.Close()
}
catch {
    Write-Host "ERROR executing query 3: $_" -ForegroundColor Red
}

Write-Host ""

# Query 4: File Not Found Analysis
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SECTION 4: FILE NOT FOUND ANALYSIS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query4 = "SELECT COUNT(*) AS FileNotFoundCount, COUNT(DISTINCT FilePath) AS UniquePaths, MIN(DownloadDate) AS EarliestDownload, MAX(DownloadDate) AS LatestDownload FROM DownloadedFiles WHERE ProcessingStatus = 'Failed' AND (ErrorMessage LIKE '%File not found%' OR ErrorMessage LIKE '%does not exist%' OR ErrorMessage LIKE '%FileNotFoundException%' OR FilePath LIKE 'Queue/%' OR FilePath LIKE 'OnDemand/%')"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query4, $connection)
    $reader = $command.ExecuteReader()
    
    if ($reader.Read()) {
        $count = $reader["FileNotFoundCount"]
        $uniquePaths = $reader["UniquePaths"]
        $earliest = if ($reader["EarliestDownload"] -ne [DBNull]::Value) { $reader["EarliestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        $latest = if ($reader["LatestDownload"] -ne [DBNull]::Value) { $reader["LatestDownload"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
        
        Write-Host "Files with 'File Not Found' errors: $count" -ForegroundColor Red
        Write-Host "Unique file paths: $uniquePaths" -ForegroundColor White
        Write-Host "Range: $earliest to $latest" -ForegroundColor Gray
        
        if ($count -gt 0) {
            Write-Host ""
            Write-Host "WARNING: These are likely virtual paths (Queue/, OnDemand/) that don't have physical files" -ForegroundColor Yellow
        }
    }
    $reader.Close()
}
catch {
    Write-Host "ERROR executing query 4: $_" -ForegroundColor Red
}

Write-Host ""

# Query 5: Recent Failures
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SECTION 5: RECENT FAILURES (Last 24h)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query5 = "SELECT TOP 10 Id, FileName, FilePath, ProcessingStatus, ErrorMessage, DownloadDate, RecordCount FROM DownloadedFiles WHERE ProcessingStatus = 'Failed' AND DownloadDate >= DATEADD(HOUR, -24, GETUTCDATE()) ORDER BY DownloadDate DESC"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query5, $connection)
    $reader = $command.ExecuteReader()
    
    if (-not $reader.HasRows) {
        Write-Host "OK: No failures in the last 24 hours" -ForegroundColor Green
    }
    else {
        while ($reader.Read()) {
            $id = $reader["Id"]
            $fileName = $reader["FileName"]
            $error = if ($reader["ErrorMessage"] -ne [DBNull]::Value) { 
                $err = $reader["ErrorMessage"].ToString()
                if ($err.Length -gt 80) { $err.Substring(0, 80) + "..." } else { $err }
            } else { "No error message" }
            $date = if ($reader["DownloadDate"] -ne [DBNull]::Value) { $reader["DownloadDate"].ToString("yyyy-MM-dd HH:mm") } else { "N/A" }
            
            Write-Host "ID: $id" -ForegroundColor Yellow -NoNewline
            Write-Host " | $fileName" -ForegroundColor White -NoNewline
            Write-Host " | $date" -ForegroundColor Gray
            Write-Host "   Error: $error" -ForegroundColor Red
            Write-Host ""
        }
    }
    $reader.Close()
}
catch {
    Write-Host "ERROR executing query 5: $_" -ForegroundColor Red
}

$connection.Close()

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DIAGNOSTIC COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
