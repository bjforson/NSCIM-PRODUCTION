$connectionString = "Server=localhost;Database=NS_CIS;Trusted_Connection=true;TrustServerCertificate=true"
$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

try {
    $connection.Open()

    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT Id, Name, DisplayName, IsActive, IsSystemRole, BaseRole
FROM Roles
ORDER BY Id
"@

    $reader = $command.ExecuteReader()

    if (-not $reader.HasRows) {
        Write-Host "No roles found." -ForegroundColor Yellow
        return
    }

    while ($reader.Read()) {
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "Role ID:      $($reader['Id'])"
        Write-Host "Name:         $($reader['Name'])"
        Write-Host "Display Name: $($reader['DisplayName'])"
        Write-Host "Is Active:    $($reader['IsActive'])"
        Write-Host "System Role:  $($reader['IsSystemRole'])"
        Write-Host "Base Role:    $($reader['BaseRole'])"
    }

    $reader.Close()
}
catch {
    Write-Host "Error querying roles: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}

