# SSL Setup Verification Script
# Quick verification of SSL certificate and environment variable configuration

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SSL Setup Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check environment variable
Write-Host "Checking Environment Variable..." -ForegroundColor Yellow
$thumbprint = [System.Environment]::GetEnvironmentVariable("NICKSCAN_API_CERT_THUMBPRINT", [System.EnvironmentVariableTarget]::Machine)

if ([string]::IsNullOrEmpty($thumbprint)) {
    Write-Host "❌ NICKSCAN_API_CERT_THUMBPRINT not set" -ForegroundColor Red
} else {
    Write-Host "✅ NICKSCAN_API_CERT_THUMBPRINT: $thumbprint" -ForegroundColor Green
    
    # Check certificate in store
    Write-Host "`nChecking Certificate Store..." -ForegroundColor Yellow
    $certificates = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $thumbprint }
    
    if ($certificates.Count -eq 0) {
        Write-Host "❌ Certificate with thumbprint $thumbprint not found in LocalMachine\My store" -ForegroundColor Red
    } else {
        $cert = $certificates[0]
        Write-Host "✅ Certificate found!" -ForegroundColor Green
        Write-Host "   Subject: $($cert.Subject)" -ForegroundColor White
        Write-Host "   Issuer: $($cert.Issuer)" -ForegroundColor White
        Write-Host "   Valid From: $($cert.NotBefore)" -ForegroundColor White
        Write-Host "   Valid To: $($cert.NotAfter)" -ForegroundColor White
        
        $daysUntilExpiry = ($cert.NotAfter - (Get-Date)).Days
        if ($daysUntilExpiry -lt 0) {
            Write-Host "   Status: ❌ EXPIRED ($([Math]::Abs($daysUntilExpiry)) days ago)" -ForegroundColor Red
        } elseif ($daysUntilExpiry -lt 30) {
            Write-Host "   Status: ⚠️ Expires in $daysUntilExpiry days (renew soon!)" -ForegroundColor Yellow
        } else {
            Write-Host "   Status: ✅ Valid (expires in $daysUntilExpiry days)" -ForegroundColor Green
        }
        
        Write-Host "   Has Private Key: $($cert.HasPrivateKey)" -ForegroundColor $(if ($cert.HasPrivateKey) { "Green" } else { "Red" })
    }
}

# Check HTTPS endpoint (if application is running)
Write-Host "`nTesting HTTPS Endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "https://localhost:5206/health" -UseBasicParsing -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
    Write-Host "✅ HTTPS endpoint is accessible" -ForegroundColor Green
    Write-Host "   Status Code: $($response.StatusCode)" -ForegroundColor White
} catch {
    Write-Host "⚠️ HTTPS endpoint not accessible (application may not be running)" -ForegroundColor Yellow
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Verification Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

