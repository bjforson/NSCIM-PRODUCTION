$token = & "$PSScriptRoot/get-superadmin-token.ps1"
$headers = @{ Authorization = "Bearer $token" }
$response = Invoke-RestMethod -Uri 'http://10.0.1.254:5205/api/Roles' -Headers $headers -Verbose
$response | Select-Object Id, Name, DisplayName
$token = & "$PSScriptRoot/get-superadmin-token.ps1"
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Uri 'http://10.0.1.254:5205/api/Roles' -Headers $headers | Select-Object Id, Name, DisplayName

