# Check recent error logs for December 25 fixes
# This script analyzes recent API logs for the errors that were fixed

param(
    [string]$LogDirectory = "logs",
    [int]$DaysBack = 7,
    [int]$MaxLines = 1000
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Recent Error Log Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Checking logs from last $DaysBack days..." -ForegroundColor Yellow
Write-Host ""

# Find recent log files
$logFiles = Get-ChildItem -Path $LogDirectory -Filter "*.txt" -ErrorAction SilentlyContinue | 
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-$DaysBack) } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 10

if (-not $logFiles) {
    Write-Host "[!] No recent log files found in $LogDirectory" -ForegroundColor Yellow
    Write-Host "Looking for log files in current directory..." -ForegroundColor Yellow
    
    $logFiles = Get-ChildItem -Path "." -Filter "*log*.txt" -Recurse -ErrorAction SilentlyContinue | 
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-$DaysBack) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 10
}

if (-not $logFiles) {
    Write-Host "[X] No log files found" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($logFiles.Count) recent log file(s):" -ForegroundColor Green
$logFiles | ForEach-Object { Write-Host "  - $($_.Name) ($($_.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" -ForegroundColor Cyan }
Write-Host ""

# Error patterns to check (from December 25 fixes)
$errorPatterns = @{
    "SQL Error 207" = @(
        "Invalid column name 'LastAccessedAtUtc'",
        "Error Number: 207",
        "LastAccessedAtUtc column not found"
    )
    "ASE DLL Not Found" = @(
        "ASE DLL not found",
        "Failed to convert ASE image",
        "Ase.Image.dll"
    )
    "Health Check Errors" = @(
        "Web API health check failed",
        "connection refused",
        "10.0.1.254:5299"
    )
    "Invalid Assignments" = @(
        "Invalid assignment",
        "Lease expired",
        "does not exist"
    )
}

$results = @{}

foreach ($patternName in $errorPatterns.Keys) {
    $results[$patternName] = @{
        Count = 0
        RecentOccurrences = @()
    }
}

# Analyze each log file
foreach ($logFile in $logFiles) {
    Write-Host "Analyzing: $($logFile.Name)..." -ForegroundColor Yellow
    
    try {
        $content = Get-Content $logFile.FullName -Tail $MaxLines -ErrorAction SilentlyContinue
        
        foreach ($patternName in $errorPatterns.Keys) {
            $patterns = $errorPatterns[$patternName]
            
            foreach ($line in $content) {
                foreach ($pattern in $patterns) {
                    if ($line -match $pattern) {
                        $results[$patternName].Count++
                        
                        # Store recent occurrence (last 10)
                        if ($results[$patternName].RecentOccurrences.Count -lt 10) {
                            $timestamp = if ($line -match '(\d{4}-\d{2}-\d{2}[\sT]\d{2}:\d{2}:\d{2})') {
                                $matches[1]
                            } else {
                                $logFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
                            }
                            $results[$patternName].RecentOccurrences += @{
                                File = $logFile.Name
                                Timestamp = $timestamp
                                Line = $line.Trim()
                            }
                        }
                        break
                    }
                }
            }
        }
    }
    catch {
        Write-Host "  [!] Error reading file: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Display results
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Error Analysis Results" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($patternName in $errorPatterns.Keys) {
    $count = $results[$patternName].Count
    if ($count -eq 0) {
        $status = "[OK] NONE"
        $statusColor = "Green"
    } elseif ($count -lt 10) {
        $status = "[!] LOW ($count)"
        $statusColor = "Yellow"
    } else {
        $status = "[X] HIGH ($count)"
        $statusColor = "Red"
    }
    
    Write-Host "$patternName : " -NoNewline -ForegroundColor Cyan
    Write-Host $status -ForegroundColor $statusColor
    
    if ($count -gt 0 -and $results[$patternName].RecentOccurrences.Count -gt 0) {
        Write-Host "  Recent occurrences:" -ForegroundColor Gray
        $results[$patternName].RecentOccurrences | Select-Object -First 3 | ForEach-Object {
            Write-Host "    [$($_.Timestamp)] $($_.Line.Substring(0, [Math]::Min(80, $_.Line.Length)))..." -ForegroundColor Gray
        }
    }
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$totalErrors = ($results.Values | Measure-Object -Property Count -Sum).Sum
if ($totalErrors -eq 0) {
    Write-Host "[SUCCESS] No errors found! December 25 fixes appear to be working." -ForegroundColor Green
} else {
    Write-Host "[!] Found $totalErrors total error occurrences" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Recommendations:" -ForegroundColor Yellow
    Write-Host "  1. Verify database migration has been applied" -ForegroundColor White
    Write-Host "  2. Check if API has been restarted after fixes" -ForegroundColor White
    Write-Host "  3. Review specific error patterns above" -ForegroundColor White
}

