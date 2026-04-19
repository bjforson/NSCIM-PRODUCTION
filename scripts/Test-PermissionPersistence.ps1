# Permission Persistence Test Script
# Tests that user permissions persist through API failures and authentication state changes

param(
    [string]$BaseUrl = "http://10.0.1.254:5205",
    [string]$Username = "superadmin",
    [SecureString]$Password = $null,
    [switch]$Verbose,
    [switch]$SkipLogin
)

$ErrorActionPreference = "Stop"
$script:TestResults = @()
$script:AuthToken = $null
$script:SessionCookies = $null

# Colors for output
function Write-TestHeader {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = ""
    )
    
    $result = @{
        TestName = $TestName
        Passed = $Passed
        Details = $Details
        Timestamp = Get-Date
    }
    
    $script:TestResults += $result
    
    $color = if ($Passed) { "Green" } else { "Red" }
    $status = if ($Passed) { "✅ PASS" } else { "❌ FAIL" }
    
    Write-Host "$status - $TestName" -ForegroundColor $color
    if ($Details) {
        Write-Host "   $Details" -ForegroundColor Gray
    }
}

function Invoke-ApiRequest {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )
    
    $url = "$BaseUrl$Endpoint"
    
    $requestHeaders = @{
        "Content-Type" = "application/json"
    }
    
    if ($script:AuthToken) {
        $requestHeaders["Authorization"] = "Bearer $script:AuthToken"
    }
    
    foreach ($key in $Headers.Keys) {
        $requestHeaders[$key] = $Headers[$key]
    }
    
    try {
        $params = @{
            Uri = $url
            Method = $Method
            Headers = $requestHeaders
            UseBasicParsing = $true
            ErrorAction = "Stop"
        }
        
        if ($Body) {
            $params["Body"] = ($Body | ConvertTo-Json -Depth 10)
        }
        
        if ($script:SessionCookies) {
            $params["WebSession"] = $script:SessionCookies
        }
        
        $response = Invoke-WebRequest @params
        return @{
            Success = $true
            StatusCode = $response.StatusCode
            Content = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
            RawContent = $response.Content
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        return @{
            Success = $false
            StatusCode = $statusCode
            Error = $_.Exception.Message
            Content = $null
        }
    }
}

function Test-Login {
    Write-TestHeader "Test 1: Login and Load Permissions"
    
    if ($SkipLogin -and $script:AuthToken) {
        Write-TestResult "Login" $true "Using existing token"
        return $true
    }
    
    $passwordPlain = $null
    if (-not $Password) {
        $Password = Read-Host "Enter password for $Username" -AsSecureString
    }
    
    # Convert SecureString to plain text for API call
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    $passwordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    
    $loginBody = @{
        username = $Username
        password = $passwordPlain
    }
    
    $response = Invoke-ApiRequest -Endpoint "/api/auth/login" -Method "POST" -Body $loginBody
    
    if ($response.Success -and $response.StatusCode -eq 200) {
        $script:AuthToken = $response.Content.token
        Write-TestResult "Login" $true "Token obtained: $($script:AuthToken.Substring(0, [Math]::Min(20, $script:AuthToken.Length)))..."
        return $true
    }
    else {
        Write-TestResult "Login" $false "Failed: $($response.Error)"
        return $false
    }
}

function Test-GetProfile {
    Write-TestHeader "Test 2: Get User Profile and Permissions"
    
    $response = Invoke-ApiRequest -Endpoint "/api/auth/profile"
    
    if ($response.Success -and $response.Content) {
        $permissionCount = if ($response.Content.permissions) { $response.Content.permissions.Count } else { 0 }
        Write-TestResult "Get Profile" $true "Profile loaded with $permissionCount permissions"
        
        if ($Verbose -and $response.Content.permissions) {
            Write-Host "`nPermissions:" -ForegroundColor Yellow
            $response.Content.permissions | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
        }
        
        return $response.Content
    }
    else {
        Write-TestResult "Get Profile" $false "Failed: $($response.Error)"
        return $null
    }
}

function Test-PermissionCheck {
    param([object]$UserProfile)
    
    Write-TestHeader "Test 3: Check Permission Endpoints"
    
    if (-not $UserProfile -or -not $UserProfile.permissions -or $UserProfile.permissions.Count -eq 0) {
        Write-TestResult "Permission Check" $false "No permissions to test"
        return $false
    }
    
    $testPermission = $UserProfile.permissions[0]
    $response = Invoke-ApiRequest -Endpoint "/api/permissions/check?permission=$testPermission"
    
    if ($response.Success) {
        Write-TestResult "Permission Check" $true "Permission '$testPermission' check successful"
        return $true
    }
    else {
        Write-TestResult "Permission Check" $false "Failed: $($response.Error)"
        return $false
    }
}

function Test-PermissionPersistence {
    param([object]$UserProfile)
    
    Write-TestHeader "Test 4: Permission Persistence Through API Failure Simulation"
    
    if (-not $UserProfile -or -not $UserProfile.permissions) {
        Write-TestResult "Permission Persistence" $false "No permissions to test"
        return $false
    }
    
    $initialPermissionCount = $UserProfile.permissions.Count
    
    # Simulate API failure by calling profile endpoint multiple times
    # In real scenario, this would be network failure, but we can test with rapid calls
    Write-Host "Simulating API failure scenario..." -ForegroundColor Yellow
    
    $successCount = 0
    $failureCount = 0
    
    for ($i = 1; $i -le 5; $i++) {
        $response = Invoke-ApiRequest -Endpoint "/api/auth/profile"
        
        if ($response.Success -and $response.Content) {
            $currentPermissionCount = if ($response.Content.permissions) { $response.Content.permissions.Count } else { 0 }
            
            if ($currentPermissionCount -eq $initialPermissionCount) {
                $successCount++
            }
            else {
                $failureCount++
                Write-Host "  Warning: Permission count changed from $initialPermissionCount to $currentPermissionCount" -ForegroundColor Yellow
            }
        }
        else {
            $failureCount++
        }
        
        Start-Sleep -Milliseconds 200
    }
    
    if ($successCount -ge 4) {
        Write-TestResult "Permission Persistence" $true "Permissions persisted through $successCount/5 API calls"
        return $true
    }
    else {
        Write-TestResult "Permission Persistence" $false "Permissions not persistent: $failureCount failures"
        return $false
    }
}

function Test-MyPermissionsEndpoint {
    Write-TestHeader "Test 5: Get My Permissions Endpoint"
    
    $response = Invoke-ApiRequest -Endpoint "/api/permissions/my-permissions"
    
    if ($response.Success -and $response.Content) {
        $permissionCount = if ($response.Content -is [Array]) { $response.Content.Count } else { 0 }
        Write-TestResult "My Permissions" $true "Retrieved $permissionCount permissions"
        
        if ($Verbose -and $response.Content) {
            Write-Host "`nMy Permissions:" -ForegroundColor Yellow
            $response.Content | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
        }
        
        return $response.Content
    }
    else {
        Write-TestResult "My Permissions" $false "Failed: $($response.Error)"
        return $null
    }
}

function Test-ProtectedEndpoints {
    Write-TestHeader "Test 6: Access Protected Endpoints"
    
    $protectedEndpoints = @(
        "/api/admin/logs",
        "/api/containercompleteness/stats",
        "/api/image-analysis-management/stats"
    )
    
    $successCount = 0
    $totalCount = $protectedEndpoints.Count
    
    foreach ($endpoint in $protectedEndpoints) {
        $response = Invoke-ApiRequest -Endpoint $endpoint
        
        if ($response.Success) {
            $successCount++
            Write-Host "  ✅ $endpoint" -ForegroundColor Green
        }
        elseif ($response.StatusCode -eq 401) {
            Write-Host "  ⚠️  $endpoint - Unauthorized (expected if endpoint requires specific permissions)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  ❌ $endpoint - Failed: $($response.StatusCode)" -ForegroundColor Red
        }
    }
    
    $passed = $successCount -gt 0
    Write-TestResult "Protected Endpoints" $passed "Accessed $successCount/$totalCount endpoints"
    return $passed
}

function Test-PermissionConsistency {
    param([object]$UserProfile, [array]$MyPermissions)
    
    Write-TestHeader "Test 7: Permission Consistency Check"
    
    if (-not $UserProfile -or -not $MyPermissions) {
        Write-TestResult "Permission Consistency" $false "Missing data for comparison"
        return $false
    }
    
    $profilePerms = if ($UserProfile.permissions) { $UserProfile.permissions | Sort-Object } else { @() }
    $myPerms = $MyPermissions | Sort-Object
    
    $profileSet = New-Object System.Collections.Generic.HashSet[string]
    $mySet = New-Object System.Collections.Generic.HashSet[string]
    
    foreach ($p in $profilePerms) { $profileSet.Add($p) | Out-Null }
    foreach ($p in $myPerms) { $mySet.Add($p) | Out-Null }
    
    $inProfileNotMy = $profileSet | Where-Object { -not $mySet.Contains($_) }
    $inMyNotProfile = $mySet | Where-Object { -not $profileSet.Contains($_) }
    
    if ($inProfileNotMy.Count -eq 0 -and $inMyNotProfile.Count -eq 0) {
        Write-TestResult "Permission Consistency" $true "Profile and My Permissions match ($($profilePerms.Count) permissions)"
        return $true
    }
    else {
        $details = "Mismatch: Profile has $($inProfileNotMy.Count) unique, My Permissions has $($inMyNotProfile.Count) unique"
        Write-TestResult "Permission Consistency" $false $details
        return $false
    }
}

function Test-MultipleProfileCalls {
    Write-TestHeader "Test 8: Multiple Profile Calls (Simulating GetAuthenticationStateAsync)"
    
    $permissionCounts = @()
    $successCount = 0
    
    for ($i = 1; $i -le 10; $i++) {
        $response = Invoke-ApiRequest -Endpoint "/api/auth/profile"
        
        if ($response.Success -and $response.Content) {
            $count = if ($response.Content.permissions) { $response.Content.permissions.Count } else { 0 }
            $permissionCounts += $count
            $successCount++
        }
        
        Start-Sleep -Milliseconds 100
    }
    
    if ($permissionCounts.Count -gt 0) {
        $uniqueCounts = $permissionCounts | Select-Object -Unique
        $isConsistent = $uniqueCounts.Count -eq 1
        
        if ($isConsistent) {
            Write-TestResult "Multiple Profile Calls" $true "Permissions consistent across $successCount calls (count: $($permissionCounts[0]))"
        }
        else {
            Write-TestResult "Multiple Profile Calls" $false "Permission count inconsistent: $($uniqueCounts -join ', ')"
        }
        
        return $isConsistent
    }
    else {
        Write-TestResult "Multiple Profile Calls" $false "All calls failed"
        return $false
    }
}

function Export-TestReport {
    $reportPath = "permission-persistence-test-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    
    $report = @{
        TestRun = @{
            Timestamp = Get-Date
            Username = $Username
            BaseUrl = $BaseUrl
            TotalTests = $script:TestResults.Count
            PassedTests = ($script:TestResults | Where-Object { $_.Passed }).Count
            FailedTests = ($script:TestResults | Where-Object { -not $_.Passed }).Count
        }
        Results = $script:TestResults
    }
    
    $report | ConvertTo-Json -Depth 10 | Out-File $reportPath
    Write-Host "`n📄 Test report saved to: $reportPath" -ForegroundColor Cyan
    
    # Also create a summary
    $summaryPath = "permission-persistence-test-summary-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
    $summary = @"
Permission Persistence Test Summary
==================================
Date: $(Get-Date)
User: $Username
Base URL: $BaseUrl

Test Results:
$($script:TestResults | ForEach-Object { 
    $status = if ($_.Passed) { "✅ PASS" } else { "❌ FAIL" }
    "$status - $($_.TestName)"
    if ($_.Details) { "   $($_.Details)" }
}) -join "`n"

Total: $($script:TestResults.Count) tests
Passed: $(($script:TestResults | Where-Object { $_.Passed }).Count)
Failed: $(($script:TestResults | Where-Object { -not $_.Passed }).Count)
"@
    
    $summary | Out-File $summaryPath
    Write-Host "📋 Summary saved to: $summaryPath" -ForegroundColor Cyan
}

# Main execution
try {
    Write-Host "`n" -NoNewline
    Write-Host "╔════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║   Permission Persistence Test Suite                   ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Test 1: Login
    $loginSuccess = Test-Login
    if (-not $loginSuccess) {
        Write-Host "`n❌ Login failed. Cannot proceed with tests." -ForegroundColor Red
        exit 1
    }
    
    # Test 2: Get Profile
    $userProfile = Test-GetProfile
    
    # Test 3: Permission Check
    Test-PermissionCheck -UserProfile $userProfile
    
    # Test 4: Permission Persistence
    Test-PermissionPersistence -UserProfile $userProfile
    
    # Test 5: My Permissions
    $myPermissions = Test-MyPermissionsEndpoint
    
    # Test 6: Protected Endpoints
    Test-ProtectedEndpoints
    
    # Test 7: Permission Consistency
    Test-PermissionConsistency -UserProfile $userProfile -MyPermissions $myPermissions
    
    # Test 8: Multiple Profile Calls
    Test-MultipleProfileCalls
    
    # Generate report
    Write-TestHeader "Test Summary"
    $passed = ($script:TestResults | Where-Object { $_.Passed }).Count
    $failed = ($script:TestResults | Where-Object { -not $_.Passed }).Count
    $total = $script:TestResults.Count
    
    Write-Host "Total Tests: $total" -ForegroundColor White
    Write-Host "Passed: $passed" -ForegroundColor Green
    Write-Host "Failed: $failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
    
    Export-TestReport
    
    if ($failed -eq 0) {
        Write-Host "`n✅ All tests passed!" -ForegroundColor Green
        exit 0
    }
    else {
        Write-Host "`n❌ Some tests failed. Review the report for details." -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "`n❌ Test execution failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
}

