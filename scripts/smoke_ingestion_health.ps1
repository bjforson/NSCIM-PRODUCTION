[Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

foreach ($url in @(
    'http://localhost:5205/api/icums/batch/ingestion-health?hours=24',
    'https://localhost:5206/api/icums/batch/ingestion-health?hours=24'
)) {
    Write-Host "--- Trying $url ---" -ForegroundColor Cyan
    try {
        $r = Invoke-RestMethod -Uri $url -TimeoutSec 30 -Method Get
        $r | ConvertTo-Json -Depth 3
        Write-Host "OK from $url" -ForegroundColor Green
        break
    } catch {
        Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
