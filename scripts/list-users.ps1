param(
    [string]$RoleName = "",
    [string]$UsernameLike = ""
)

$connectionString = "Server=localhost;Database=NS_CIS;Trusted_Connection=true;TrustServerCertificate=true"
$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

try {
    $connection.Open()

    $query = @"
SELECT 
    u.Id,
    u.Username,
    u.Email,
    u.FirstName,
    u.LastName,
    u.RoleId,
    r.Name AS RoleName,
    u.IsActive,
    u.CreatedAt,
    u.LastLoginAt
FROM Users u
LEFT JOIN Roles r ON u.RoleId = r.Id
"@

    $whereClauses = @()

    if (-not [string]::IsNullOrWhiteSpace($RoleName)) {
        $whereClauses += "r.Name = @RoleName"
    }

    if (-not [string]::IsNullOrWhiteSpace($UsernameLike)) {
        $whereClauses += "u.Username LIKE @UsernameLike"
    }

    if ($whereClauses.Count -gt 0) {
        $query += "`nWHERE " + ($whereClauses -join " AND ")
    }

    $query += "`nORDER BY u.Id"

    $command = $connection.CreateCommand()
    $command.CommandText = $query

    if (-not [string]::IsNullOrWhiteSpace($RoleName)) {
        $command.Parameters.AddWithValue("@RoleName", $RoleName) | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($UsernameLike)) {
        $pattern = if ($UsernameLike.Contains("%")) { $UsernameLike } else { "%$UsernameLike%" }
        $command.Parameters.AddWithValue("@UsernameLike", $pattern) | Out-Null
    }

    $reader = $command.ExecuteReader()

    if (-not $reader.HasRows) {
        if (-not [string]::IsNullOrWhiteSpace($RoleName) -and -not [string]::IsNullOrWhiteSpace($UsernameLike)) {
            Write-Host "No users found for role '$RoleName' with username like '$UsernameLike'." -ForegroundColor Yellow
        } elseif (-not [string]::IsNullOrWhiteSpace($RoleName)) {
            Write-Host "No users found for role '$RoleName'." -ForegroundColor Yellow
        } elseif (-not [string]::IsNullOrWhiteSpace($UsernameLike)) {
            Write-Host "No users found with username like '$UsernameLike'." -ForegroundColor Yellow
        } else {
            Write-Host "No users found." -ForegroundColor Yellow
        }
        return
    }

    while ($reader.Read()) {
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "ID:           $($reader['Id'])"
        Write-Host "Username:     $($reader['Username'])"
        Write-Host "Email:        $($reader['Email'])"
        Write-Host "First Name:   $($reader['FirstName'])"
        Write-Host "Last Name:    $($reader['LastName'])"
        Write-Host "Role ID:      $($reader['RoleId'])"
        Write-Host "Role Name:    $($reader['RoleName'])"
        Write-Host "Is Active:    $($reader['IsActive'])"
        Write-Host "Created At:   $($reader['CreatedAt'])"
        Write-Host "Last Login:   $($reader['LastLoginAt'])"
    }

    $reader.Close()
}
catch {
    Write-Host "Error querying users: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}

