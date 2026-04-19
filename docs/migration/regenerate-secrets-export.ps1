# Regenerates Y:\set-secrets-on-target.ps1 from current source env vars.
# Run on SOURCE server (NSPORTAL) as Administrator to export current secret values
# for copying to target. The output file contains plaintext secrets - handle
# carefully; delete after target has imported them.
#
# NEVER commit the generated .ps1 to git - values are in cleartext.

$vars = @(
    'NICKSCAN_SUPERADMIN_PASSWORD', 'NICKSCAN_SERVICE_API_KEY',
    'NICKSCAN_SETTINGS_ENCRYPTION_KEY', 'NICKSCAN_FS6000_NETWORK_PASSWORD',
    'NICKCOMMS_API_KEY_NICKHR', 'NICKCOMMS_API_KEY_NSCIS',
    'NICKSCAN_JWT_SECRET_KEY', 'NICKSCAN_ICUMS_AUTH_KEY',
    'NICKSCAN_ICUMS_DOCS_AUTH_KEY', 'NICKSCAN_ICUMS_JSON_AUTH_KEY',
    'NICKSCAN_ASE_PASSWORD', 'NICKSCAN_API_CERT_PASSWORD',
    'NICKSCAN_API_CERT_THUMBPRINT', 'NICKCOMMS_BASE_URL',
    'NICKCOMMS_ConnectionStrings__CommsDb',
    'NICKCOMMS_Email__SmtpPassword', 'NICKCOMMS_Email__SmtpUsername',
    'NICKHR_SMTP_PASSWORD'
)
$lines = @(
    '# Auto-generated on ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ' from source ' + $env:COMPUTERNAME
    '# Sets Machine-scope env vars. Run as Administrator on target.'
    '# After running, restart all NickERP services.'
    ''
    '#Requires -RunAsAdministrator'
    ''
)
foreach ($v in $vars) {
    $val = [Environment]::GetEnvironmentVariable($v, 'Machine')
    if ($val) {
        $escaped = $val.Replace("'", "''")
        $lines += "[Environment]::SetEnvironmentVariable('$v', '$escaped', 'Machine')"
        $lines += "Write-Host '  Set: $v' -ForegroundColor Green"
    } else {
        $lines += "# SKIPPED (unset on source): $v"
    }
}
$lines += ''
$lines += "Write-Host ''"
$lines += 'Write-Host ''Restart services:'' -ForegroundColor Yellow'
$lines += 'Write-Host ''  Restart-Service NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_NickComms, NickHR_API, NickHR_WebApp, NSCIM_ImageSplitter -Force'' -ForegroundColor Cyan'

$outPath = 'Y:\set-secrets-on-target.ps1'
$lines | Set-Content -Path $outPath -Encoding UTF8
Write-Host "Wrote $outPath ($($vars.Count) vars checked)"
