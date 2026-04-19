# Query user details from database
$connectionString = "Server=localhost;Database=NS_CIS;Trusted_Connection=true;TrustServerCertificate=true"
$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT 
    Id,
    Username,
    Email,
    FirstName,
    LastName,
    Department,
    PhoneNumber,
    RoleId,
    IsActive,
    CreatedAt,
    LastLoginAt,
    UserNumber,
    CreatedBy,
    UpdatedAt,
    UpdatedBy
FROM Users 
WHERE Username = @username
"@
    
    $command.Parameters.AddWithValue("@username", "pmarmah")
    $reader = $command.ExecuteReader()
    
    if ($reader.Read()) {
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "User Details for: pmarmah" -ForegroundColor Cyan
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "ID:              $($reader['Id'])" -ForegroundColor Green
        Write-Host "Username:        $($reader['Username'])" -ForegroundColor Green
        Write-Host "Email:           $($reader['Email'])" -ForegroundColor Green
        Write-Host "First Name:      $($reader['FirstName'])" -ForegroundColor Green
        Write-Host "Last Name:       $($reader['LastName'])" -ForegroundColor Green
        Write-Host "Department:      $($reader['Department'])" -ForegroundColor Green
        Write-Host "Phone Number:    $($reader['PhoneNumber'])" -ForegroundColor Green
        Write-Host "Role ID:         $($reader['RoleId'])" -ForegroundColor Green
        Write-Host "Is Active:       $($reader['IsActive'])" -ForegroundColor Green
        Write-Host "Created At:      $($reader['CreatedAt'])" -ForegroundColor Green
        Write-Host "Last Login At:   $($reader['LastLoginAt'])" -ForegroundColor Green
        Write-Host "User Number:     $($reader['UserNumber'])" -ForegroundColor Green
        Write-Host "Created By:      $($reader['CreatedBy'])" -ForegroundColor Green
        Write-Host "Updated At:      $($reader['UpdatedAt'])" -ForegroundColor Green
        Write-Host "Updated By:      $($reader['UpdatedBy'])" -ForegroundColor Green
        
        # Get role name if RoleId exists
        $roleId = $reader['RoleId']
        $reader.Close()
        
        if (-not [string]::IsNullOrEmpty($roleId)) {
            $roleCommand = $connection.CreateCommand()
            $roleCommand.CommandText = "SELECT Name, DisplayName FROM Roles WHERE Id = @roleId"
            $roleCommand.Parameters.AddWithValue("@roleId", $roleId)
            $roleReader = $roleCommand.ExecuteReader()
            if ($roleReader.Read()) {
                Write-Host "Role Name:       $($roleReader['Name'])" -ForegroundColor Yellow
                Write-Host "Role Display:    $($roleReader['DisplayName'])" -ForegroundColor Yellow
            }
            $roleReader.Close()
        }
    } else {
        Write-Host "User 'pmarmah' not found in database." -ForegroundColor Red
        $reader.Close()
    }
} catch {
    Write-Host "Error querying database: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}

