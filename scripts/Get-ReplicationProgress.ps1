# Replication Progress Summary Script
# Shows completion status of all replication tasks

param(
    [string]$TodoFilePath = ".todos.json"
)

$separator = "========================================"

Write-Host $separator -ForegroundColor Cyan
Write-Host "Database Replication Progress Report" -ForegroundColor Cyan
Write-Host $separator -ForegroundColor Cyan
Write-Host "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ""

# Try to read TODO file if it exists
$todos = @()
if (Test-Path $TodoFilePath) {
    try {
        $todos = Get-Content $TodoFilePath | ConvertFrom-Json
    } catch {
        Write-Host "Warning: Could not read TODO file: $_" -ForegroundColor Yellow
    }
}

if ($todos.Count -eq 0) {
    Write-Host "No TODO items found. Progress tracking not available." -ForegroundColor Yellow
    Write-Host "TODO file expected at: $TodoFilePath" -ForegroundColor Gray
    exit 0
}

# Calculate statistics
$total = $todos.Count
$completed = ($todos | Where-Object { $_.status -eq "completed" }).Count
$inProgress = ($todos | Where-Object { $_.status -eq "in_progress" }).Count
$pending = ($todos | Where-Object { $_.status -eq "pending" }).Count
$cancelled = ($todos | Where-Object { $_.status -eq "cancelled" }).Count

$completionPercent = if ($total -gt 0) { [math]::Round(($completed / $total) * 100, 1) } else { 0 }

# Overall Summary
Write-Host "OVERALL PROGRESS" -ForegroundColor Cyan
Write-Host ("=" * 50) -ForegroundColor Gray
Write-Host "Total Tasks:        $total" -ForegroundColor White
Write-Host "Completed:          $completed" -ForegroundColor Green
Write-Host "In Progress:        $inProgress" -ForegroundColor Yellow
Write-Host "Pending:            $pending" -ForegroundColor Gray
Write-Host "Cancelled:          $cancelled" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Completion:         $completionPercent%" -ForegroundColor $(if ($completionPercent -eq 100) { "Green" } elseif ($completionPercent -ge 50) { "Yellow" } else { "Red" })
Write-Host ""

# Progress Bar
$barLength = 50
$filled = [math]::Round(($completionPercent / 100) * $barLength)
$bar = "#" * $filled + "-" * ($barLength - $filled)
Write-Host "[$bar] $completionPercent%" -ForegroundColor $(if ($completionPercent -eq 100) { "Green" } elseif ($completionPercent -ge 50) { "Yellow" } else { "Red" })
Write-Host ""

# Phase Breakdown
Write-Host "PHASE BREAKDOWN" -ForegroundColor Cyan
Write-Host ("=" * 50) -ForegroundColor Gray

$phases = @(
    @{ Name = "Phase 1: Pre-Replication Checks"; Pattern = "phase1" },
    @{ Name = "Phase 2: Database Creation"; Pattern = "phase2" },
    @{ Name = "Phase 3: Schema Replication"; Pattern = "phase3" },
    @{ Name = "Phase 4: Data Transfer"; Pattern = "phase4" },
    @{ Name = "Phase 5: Objects Replication"; Pattern = "phase5" },
    @{ Name = "Phase 6: Resync Incomplete Tables"; Pattern = "phase6" },
    @{ Name = "Phase 7: Final Verification"; Pattern = "phase7" },
    @{ Name = "Phase 8: Post-Replication Tasks"; Pattern = "phase8" }
)

foreach ($phase in $phases) {
    $phaseTodos = $todos | Where-Object { $_.id -like "$($phase.Pattern)*" }
    if ($phaseTodos.Count -gt 0) {
        $phaseCompleted = ($phaseTodos | Where-Object { $_.status -eq "completed" }).Count
        $phaseTotal = $phaseTodos.Count
        $phasePercent = if ($phaseTotal -gt 0) { [math]::Round(($phaseCompleted / $phaseTotal) * 100, 1) } else { 0 }
        
        $color = if ($phasePercent -eq 100) { "Green" } elseif ($phasePercent -ge 50) { "Yellow" } else { "Red" }
        $format = "{0,-40} {1,3}/{2,-3} ({3,5}%)"
        Write-Host ($format -f $phase.Name, $phaseCompleted, $phaseTotal, "$phasePercent%") -ForegroundColor $color
    }
}

Write-Host ""

# Currently In Progress
$currentTasks = $todos | Where-Object { $_.status -eq "in_progress" }
if ($currentTasks.Count -gt 0) {
    Write-Host "CURRENTLY IN PROGRESS" -ForegroundColor Cyan
    Write-Host ("=" * 50) -ForegroundColor Gray
    foreach ($task in $currentTasks) {
        Write-Host "  - $($task.content)" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Recent Completions (last 5)
$recentCompleted = $todos | Where-Object { $_.status -eq "completed" } | Select-Object -Last 5
if ($recentCompleted.Count -gt 0) {
    Write-Host "RECENTLY COMPLETED" -ForegroundColor Cyan
    Write-Host ("=" * 50) -ForegroundColor Gray
    foreach ($task in $recentCompleted) {
        Write-Host "  + $($task.content)" -ForegroundColor Green
    }
    Write-Host ""
}

# Next Pending Tasks (top 5)
$nextTasks = $todos | Where-Object { $_.status -eq "pending" } | Select-Object -First 5
if ($nextTasks.Count -gt 0) {
    Write-Host "NEXT PENDING TASKS" -ForegroundColor Cyan
    Write-Host ("=" * 50) -ForegroundColor Gray
    foreach ($task in $nextTasks) {
        Write-Host "  o $($task.content)" -ForegroundColor Gray
    }
    Write-Host ""
}

# Estimated Time Remaining (if time estimates are in content)
$estimatedMinutes = 0
$pendingWithTime = $todos | Where-Object { 
    $_.status -eq "pending" -and 
    $_.content -match '\((\d+)\s*min\)'
}
foreach ($task in $pendingWithTime) {
    if ($task.content -match '\((\d+)\s*min\)') {
        $estimatedMinutes += [int]$matches[1]
    }
}

if ($estimatedMinutes -gt 0) {
    $estimatedHours = [math]::Round($estimatedMinutes / 60, 1)
    Write-Host "ESTIMATED TIME REMAINING" -ForegroundColor Cyan
    Write-Host ("=" * 50) -ForegroundColor Gray
    Write-Host "Estimated: $estimatedMinutes minutes (~$estimatedHours hours)" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host $separator -ForegroundColor Cyan
Write-Host ""
