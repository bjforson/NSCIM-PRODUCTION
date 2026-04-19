$body = @{ Username = 'superadmin'; Password = $env:NICKSCAN_SUPERADMIN_PASSWORD }
$json = $body | ConvertTo-Json
$headers = @{ 'Content-Type' = 'application/json' }

$response = Invoke-RestMethod -Uri 'http://10.0.1.254:5205/api/Authentication/login' -Method Post -Headers $headers -Body $json
$response.token

