# Get-IncompleteFeaturesProgress.ps1
# Script to track progress on incomplete features implementation

param(
    [switch] $Summary,
    [string] $Priority = "all"
)

$todoFile = Join-Path (Split-Path $PSScriptRoot -Parent) ".todos-incomplete-features.json"

if (-not (Test-Path $todoFile)) {
    Write-Host "❌ TODO file not found: $todoFile" -ForegroundColor Red
    exit 1
}

$todos = Get-Content $todoFile -Raw | ConvertFrom-Json

if ($Summary) {
    Write-Host "=== Incomplete Features Progress Summary ===" -ForegroundColor Cyan
    Write-Host ""
    
    $stats = $todos.statistics
    Write-Host "Total Features: $($todos.totalFeatures)" -ForegroundColor White
    Write-Host ""
    
    Write-Host "By Priority:" -ForegroundColor Yellow
    Write-Host "  🔴 Critical: $($stats.byPriority.critical) features" -ForegroundColor Red
    Write-Host "  🟡 High:     $($stats.byPriority.high) features" -ForegroundColor Yellow
    Write-Host "  🟢 Medium:   $($stats.byPriority.medium) features" -ForegroundColor Green
    Write-Host "  🔵 Low:      $($stats.byPriority.low) features" -ForegroundColor Blue
    Write-Host ""
    
    Write-Host "By Status:" -ForegroundColor Yellow
    Write-Host "  ⏳ Pending:    $($stats.byStatus.pending)" -ForegroundColor Gray
    Write-Host "  🔄 In Progress: $($stats.byStatus.in_progress)" -ForegroundColor Cyan
    Write-Host "  ✅ Completed:  $($stats.byStatus.completed)" -ForegroundColor Green
    Write-Host "  ❌ Cancelled:  $($stats.byStatus.cancelled)" -ForegroundColor Red
    Write-Host ""
    
    Write-Host "Time Estimates:" -ForegroundColor Yellow
    Write-Host "  Minimum: $($stats.totalEstimatedHours.min) hours" -ForegroundColor White
    Write-Host "  Maximum: $($stats.totalEstimatedHours.max) hours" -ForegroundColor White
    Write-Host ""
    
    if ($stats.byStatus.completed -gt 0) {
        $completionRate = [math]::Round(($stats.byStatus.completed / $todos.totalFeatures) * 100, 1)
        Write-Host "Completion Rate: $completionRate%" -ForegroundColor $(if ($completionRate -ge 80) { "Green" } elseif ($completionRate -ge 50) { "Yellow" } else { "Red" })
        Write-Host ""
    }
    
    Write-Host "Quick Wins Available:" -ForegroundColor Yellow
    foreach ($quickWinId in $todos.quickWins) {
        $feature = $todos.categories | ForEach-Object { $_.todos } | Where-Object { $_.id -eq $quickWinId } | Select-Object -First 1
        if ($feature) {
            Write-Host "  - $($feature.title) ($($feature.estimatedHours) hours)" -ForegroundColor Green
        }
    }
    
    exit 0
}

$filteredCategories = switch ($Priority.ToLower()) {
    "critical" { $todos.categories | Where-Object { $_.id -eq "p0-critical" } }
    "high" { $todos.categories | Where-Object { $_.id -eq "p1-high" } }
    "medium" { $todos.categories | Where-Object { $_.id -eq "p2-medium" } }
    "low" { $todos.categories | Where-Object { $_.id -eq "p3-low" } }
    default { $todos.categories }
}

Write-Host "=== Incomplete Features TODO List ===" -ForegroundColor Cyan
Write-Host ""

foreach ($category in $filteredCategories) {
    $priorityColor = switch ($category.id) {
        "p0-critical" { "Red" }
        "p1-high" { "Yellow" }
        "p2-medium" { "Green" }
        "p3-low" { "Blue" }
        default { "White" }
    }
    
    Write-Host "$($category.name) ($($category.count) features, $($category.timeEstimate))" -ForegroundColor $priorityColor
    Write-Host ("=" * 80) -ForegroundColor Gray
    Write-Host ""
    
    foreach ($todo in $category.todos) {
        $statusIcon = switch ($todo.status) {
            "pending" { "⏳" }
            "in_progress" { "🔄" }
            "completed" { "✅" }
            "cancelled" { "❌" }
            default { "⏳" }
        }
        
        Write-Host "$statusIcon [$($todo.id)] $($todo.title)" -ForegroundColor White
        Write-Host "   Priority: $($todo.priority) | Status: $($todo.status) | Time: $($todo.estimatedHours) hours" -ForegroundColor Gray
        Write-Host "   Description: $($todo.description)" -ForegroundColor DarkGray
        Write-Host ""
    }
    
    Write-Host ""
}

Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Run with -Summary switch for detailed statistics" -ForegroundColor Gray
Write-Host "Run with -Priority <critical|high|medium|low> to filter by priority" -ForegroundColor Gray

