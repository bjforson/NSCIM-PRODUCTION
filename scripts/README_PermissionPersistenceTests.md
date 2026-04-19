# Permission Persistence Test Script

## Overview
PowerShell script to automate testing of permission persistence through API failures and authentication state changes.

## Usage

### Basic Usage
```powershell
.\Test-PermissionPersistence.ps1
```

### With Parameters
```powershell
# Specify base URL
.\Test-PermissionPersistence.ps1 -BaseUrl "http://localhost:5205"

# Specify username
.\Test-PermissionPersistence.ps1 -Username "admin"

# Verbose output
.\Test-PermissionPersistence.ps1 -Verbose

# Skip login (if already authenticated)
.\Test-PermissionPersistence.ps1 -SkipLogin
```

### Full Example
```powershell
.\Test-PermissionPersistence.ps1 `
    -BaseUrl "http://10.0.1.254:5205" `
    -Username "superadmin" `
    -Verbose
```

## Test Scenarios

### Test 1: Login and Load Permissions
- Logs in with provided credentials
- Verifies token is obtained
- **Expected**: Login successful, token received

### Test 2: Get User Profile and Permissions
- Calls `/api/auth/profile` endpoint
- Verifies profile is loaded with permissions
- **Expected**: Profile loaded, permissions present

### Test 3: Check Permission Endpoints
- Tests permission check endpoint
- Verifies permission validation works
- **Expected**: Permission check successful

### Test 4: Permission Persistence Through API Failure
- Simulates API failure by making multiple rapid calls
- Verifies permissions remain consistent
- **Expected**: Permissions persist through failures

### Test 5: Get My Permissions Endpoint
- Calls `/api/permissions/my-permissions`
- Verifies endpoint returns permissions
- **Expected**: Permissions retrieved successfully

### Test 6: Access Protected Endpoints
- Tests access to various protected endpoints
- Verifies authorization works
- **Expected**: Protected endpoints accessible

### Test 7: Permission Consistency Check
- Compares permissions from profile vs my-permissions
- Verifies consistency
- **Expected**: Permissions match between endpoints

### Test 8: Multiple Profile Calls
- Makes 10 rapid calls to profile endpoint
- Simulates frequent `GetAuthenticationStateAsync()` calls
- **Expected**: Permissions remain consistent

## Output

### Console Output
- Real-time test results with ✅/❌ indicators
- Color-coded output (Green = Pass, Red = Fail)
- Verbose details when `-Verbose` flag is used

### Generated Reports
1. **JSON Report**: `permission-persistence-test-report-YYYYMMDD-HHMMSS.json`
   - Complete test results with timestamps
   - Machine-readable format

2. **Summary Report**: `permission-persistence-test-summary-YYYYMMDD-HHMMSS.txt`
   - Human-readable summary
   - Quick overview of results

## Exit Codes
- `0`: All tests passed
- `1`: One or more tests failed, or script error occurred

## Requirements
- PowerShell 5.1 or later
- Network access to API server
- Valid user credentials
- API server running and accessible

## Troubleshooting

### Login Fails
- Verify username and password are correct
- Check API server is running
- Verify base URL is correct
- Check network connectivity

### Permission Tests Fail
- Verify user has permissions assigned
- Check API endpoints are accessible
- Review API logs for errors
- Verify authentication token is valid

### Network Errors
- Check API server is running
- Verify base URL is correct
- Check firewall settings
- Verify network connectivity

## Integration with CI/CD

```powershell
# Example for CI/CD pipeline
$exitCode = & .\Test-PermissionPersistence.ps1 `
    -BaseUrl $env:API_BASE_URL `
    -Username $env:TEST_USERNAME `
    -Password $env:TEST_PASSWORD

if ($exitCode -ne 0) {
    Write-Error "Permission persistence tests failed"
    exit 1
}
```

## Notes
- Script uses bearer token authentication
- Session cookies are maintained between requests
- Tests are designed to be non-destructive
- Script can be run multiple times safely

