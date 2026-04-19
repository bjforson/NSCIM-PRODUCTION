# Test Multi-Container Auto-Progression
# This script helps test the auto-progression feature by finding multi-container groups

param(
    [string]$ServerName = "localhost",
    [string]$DatabaseName = "NS_CIS",
    [string]$ApiUrl = "http://localhost:5205"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Multi-Container Auto-Progression Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find multi-container groups
Write-Host "1. Finding Multi-Container Groups..." -ForegroundColor Yellow

$connectionString = "Server=$ServerName;Database=$DatabaseName;Trusted_Connection=true;TrustServerCertificate=true;"

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Find groups with multiple containers
    $query = @"
SELECT TOP 10
    ag.GroupIdentifier,
    ag.Status,
    COUNT(DISTINCT ar.ContainerNumber) AS ContainerCount,
    STRING_AGG(DISTINCT ar.ContainerNumber, ', ') AS Containers,
    COUNT(DISTINCT iad.ContainerNumber) AS DecidedCount
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
LEFT JOIN ImageAnalysisDecisions iad ON iad.GroupIdentifier = ag.GroupIdentifier 
    AND (iad.Decision = 'Normal' OR iad.Decision = 'Abnormal')
WHERE ag.Status IN ('Ready', 'AnalystAssigned')
GROUP BY ag.GroupIdentifier, ag.Status
HAVING COUNT(DISTINCT ar.ContainerNumber) > 1
ORDER BY ContainerCount DESC, ag.GroupIdentifier
"@
    
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()
    
    $groups = @()
    while ($reader.Read()) {
        $groups += [PSCustomObject]@{
            GroupIdentifier = $reader["GroupIdentifier"]
            Status = $reader["Status"]
            ContainerCount = $reader["ContainerCount"]
            Containers = $reader["Containers"]
            DecidedCount = $reader["DecidedCount"]
        }
    }
    $reader.Close()
    $connection.Close()
    
    if ($groups.Count -eq 0) {
        Write-Host "   [!] No multi-container groups found in Ready/AnalystAssigned status" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "   Looking for any multi-container groups..." -ForegroundColor Yellow
        
        $connection.Open()
        $query2 = @"
SELECT TOP 5
    ag.GroupIdentifier,
    ag.Status,
    COUNT(DISTINCT ar.ContainerNumber) AS ContainerCount,
    STRING_AGG(DISTINCT ar.ContainerNumber, ', ') AS Containers
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
GROUP BY ag.GroupIdentifier, ag.Status
HAVING COUNT(DISTINCT ar.ContainerNumber) > 1
ORDER BY ContainerCount DESC
"@
        $command2 = $connection.CreateCommand()
        $command2.CommandText = $query2
        $reader2 = $command2.ExecuteReader()
        
        while ($reader2.Read()) {
            $groups += [PSCustomObject]@{
                GroupIdentifier = $reader2["GroupIdentifier"]
                Status = $reader2["Status"]
                ContainerCount = $reader2["ContainerCount"]
                Containers = $reader2["Containers"]
                DecidedCount = 0
            }
        }
        $reader2.Close()
        $connection.Close()
    }
    
    if ($groups.Count -eq 0) {
        Write-Host "   [X] No multi-container groups found in database" -ForegroundColor Red
        Write-Host ""
        Write-Host "   To test auto-progression, you need groups with multiple containers." -ForegroundColor Yellow
        Write-Host "   The system will create these automatically when processing multi-container cargo." -ForegroundColor Yellow
        exit 0
    }
    
        Write-Host "   [OK] Found $($groups.Count) multi-container group(s):" -ForegroundColor Green
    Write-Host ""
    
    $groups | ForEach-Object {
        $undecided = $_.ContainerCount - $_.DecidedCount
        $statusColor = if ($_.Status -eq 'Ready') { 'Green' } else { 'Yellow' }
        $decidedColor = if ($undecided -eq 0) { 'Red' } else { 'Cyan' }
        
        Write-Host "   Group: $($_.GroupIdentifier)" -ForegroundColor Cyan
        Write-Host "     Status: $($_.Status)" -ForegroundColor $statusColor
        Write-Host "     Containers: $($_.ContainerCount) total" -ForegroundColor White
        Write-Host "     Containers: $($_.Containers)" -ForegroundColor White
        Write-Host "     Decided: $($_.DecidedCount) | Undecided: $undecided" -ForegroundColor $decidedColor
        Write-Host ""
    }
    
    # Step 2: Test API endpoint
    Write-Host "2. Testing API Endpoint..." -ForegroundColor Yellow
    
    try {
        $healthResponse = Invoke-WebRequest -Uri "$ApiUrl/health" -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Host "   [OK] API is running at $ApiUrl" -ForegroundColor Green
    }
    catch {
        Write-Host "   [X] API is not responding at $ApiUrl" -ForegroundColor Red
        Write-Host "     Error: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "   Please start the API before testing auto-progression." -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host ""
    
    # Step 3: Test recommendations
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Test Recommendations" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    $testGroup = $groups | Where-Object { $_.Status -eq 'AnalystAssigned' -or $_.Status -eq 'Ready' } | Select-Object -First 1
    
    if ($testGroup) {
        Write-Host "Recommended Test Group:" -ForegroundColor Yellow
        Write-Host "  Group Identifier: $($testGroup.GroupIdentifier)" -ForegroundColor Cyan
        Write-Host "  Containers: $($testGroup.Containers)" -ForegroundColor Cyan
        Write-Host "  Status: $($testGroup.Status)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Manual Testing Steps:" -ForegroundColor Yellow
        Write-Host "  1. Open the web app and navigate to 'My Assignments'" -ForegroundColor White
        Write-Host "  2. Find and open the group: $($testGroup.GroupIdentifier)" -ForegroundColor White
        Write-Host "  3. Analyze the first container and save a decision" -ForegroundColor White
        Write-Host "  4. Verify the dialog stays open and automatically loads the next container" -ForegroundColor White
        Write-Host "  5. Continue until all containers are analyzed" -ForegroundColor White
        Write-Host "  6. Verify the dialog closes only after all containers are decided" -ForegroundColor White
        Write-Host ""
        Write-Host "Expected Behavior:" -ForegroundColor Yellow
        Write-Host "  ✓ Dialog should stay open between containers" -ForegroundColor Green
        Write-Host "  ✓ Next container should load automatically" -ForegroundColor Green
        Write-Host "  ✓ Dialog should close only when allContainersDecided=true" -ForegroundColor Green
        Write-Host ""
        Write-Host "Check API Logs For:" -ForegroundColor Yellow
        Write-Host "  - [AUTO-PROGRESSION] log messages" -ForegroundColor White
        Write-Host "  - nextContainerNumber in API responses" -ForegroundColor White
        Write-Host "  - allContainersDecided flag in API responses" -ForegroundColor White
    } else {
        Write-Host "No groups available for testing in Ready/AnalystAssigned status." -ForegroundColor Yellow
        Write-Host "Wait for the system to create assignments, or check existing groups." -ForegroundColor Yellow
    }
    
}
catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

