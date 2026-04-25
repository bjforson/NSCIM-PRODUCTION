# Rotate the NickComms API key issued to the `nickhr` client app.
#
# Why: shipped with the placeholder string `nickhr-key-change-me-in-production`
# (commit 89e077f exposed this when HR forgot-password mail was silently
# failing). C.1.4 in ROADMAP.md says "always-on; ship now".
#
# What it does:
#   1. Mints a 32-byte URL-safe random key.
#   2. SHA-256 hashes it (matches the api_keys.key_hash format used by
#      the gateway).
#   3. Updates the row in nick_comms.api_keys with the new hash.
#   4. Updates the machine-level env var NICKCOMMS_API_KEY_NICKHR with the
#      new plaintext.
#   5. Restarts NickHR_API + NickHR_WebApp so they pick up the env var.
#   6. Probes /api/email/send with the new key to confirm 202 Accepted.
#
# Prerequisites:
#   - Run as Administrator (env var write requires elevation).
#   - Postgres password supplied via NICKSCAN_DB_PASSWORD env var.
#   - PSQL_EXE points at psql.exe (default: PG18).
#
# Rollback: keep the previous env var value somewhere offline; if the
# rotation breaks something, set it back and re-set the DB hash.

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $PsqlExe = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [string] $PgUser = 'postgres',
    [string] $PgHost = 'localhost',
    [int]    $PgPort = 5432,
    [string] $CommsDb = 'nick_comms',
    [string] $AppName = 'nickhr',
    [string] $EnvVarName = 'NICKCOMMS_API_KEY_NICKHR',
    [int]    $KeyByteLength = 32
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PsqlExe)) { throw "psql.exe not found at $PsqlExe" }
$pgPassword = $env:NICKSCAN_DB_PASSWORD
if ([string]::IsNullOrWhiteSpace($pgPassword)) {
    throw 'NICKSCAN_DB_PASSWORD env var must be set so we can connect to nick_comms.'
}

# 1. Mint a 32-byte URL-safe random key. NIST SP 800-90A compliant via RNGCryptoServiceProvider.
$bytes = New-Object byte[] $KeyByteLength
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$newKey = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
Write-Host "Minted new key (length=$($newKey.Length) chars)" -ForegroundColor Cyan

# 2. SHA-256 hash for the api_keys table.
$sha = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($newKey))
$hashHex = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
$keyPrefix = $newKey.Substring(0, [Math]::Min(8, $newKey.Length))

# 3. Update the api_keys row.
if ($PSCmdlet.ShouldProcess("nick_comms.api_keys app_name=$AppName", 'UPDATE key_hash + key_prefix')) {
    $env:PGPASSWORD = $pgPassword
    $sql = @"
UPDATE public.api_keys
   SET key_hash = '$hashHex',
       key_prefix = '$keyPrefix'
 WHERE app_name = '$AppName'
RETURNING app_name, key_prefix, last_used_at;
"@
    & $PsqlExe -h $PgHost -p $PgPort -U $PgUser -d $CommsDb -P pager=off -c $sql
    if ($LASTEXITCODE -ne 0) { throw "psql update failed (exit $LASTEXITCODE)." }
}

# 4. Set the machine env var so service workers running as LocalSystem pick it up.
if ($PSCmdlet.ShouldProcess("Machine env var $EnvVarName", 'set new key')) {
    [Environment]::SetEnvironmentVariable($EnvVarName, $newKey, 'Machine')
    Write-Host "Machine env var $EnvVarName updated." -ForegroundColor Green
}

# 5. Restart consumers so they reload the env var.
foreach ($svc in 'NickHR_WebApp', 'NickHR_API') {
    $existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Host "  $svc not present on this host; skipping restart." -ForegroundColor Yellow
        continue
    }
    if ($PSCmdlet.ShouldProcess($svc, 'Restart-Service -Force')) {
        Restart-Service -Name $svc -Force
        Start-Sleep -Seconds 5
        $status = (Get-Service -Name $svc).Status
        # `if` cannot be used as a -ForegroundColor expression directly
        # in older PowerShell parsers; using the ternary operator
        # ($status -eq 'Running' ? 'Green' : 'Red') would also fail in
        # 5.1. Stick to a pre-computed scalar so the script runs on
        # both 5.1 and 7+.
        $clr = if ($status -eq 'Running') { 'Green' } else { 'Red' }
        Write-Host "  $svc -> $status" -ForegroundColor $clr
    }
}

# 6. Smoke-test by hitting NickComms with the NEW key and probing for 202.
if ($PSCmdlet.ShouldProcess('http://localhost:5220/api/email/send', 'smoke-test new key')) {
    $body = @{
        to              = 'systems@nickscan.com'
        subject         = "NickComms key rotation smoke test ($(Get-Date -Format 's'))"
        body            = '<p>Automated probe after C.1.4 key rotation. Safe to delete.</p>'
        isHtml          = $true
        clientReference = "key-rotation-$(Get-Date -Format 'yyyyMMddHHmmss')"
    } | ConvertTo-Json -Compress
    try {
        $resp = Invoke-WebRequest `
            -Uri 'http://localhost:5220/api/email/send' `
            -Method POST `
            -ContentType 'application/json' `
            -Headers @{ 'X-Api-Key' = $newKey } `
            -Body $body `
            -UseBasicParsing `
            -ErrorAction Stop
        if ($resp.StatusCode -in 200, 201, 202) {
            Write-Host "  Smoke test OK (HTTP $($resp.StatusCode))." -ForegroundColor Green
        } else {
            Write-Host "  Smoke test returned HTTP $($resp.StatusCode); investigate." -ForegroundColor Red
        }
    } catch {
        Write-Host "  Smoke test FAILED: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  Key rotated. Save the plaintext below for one-time secure'
Write-Host '  transfer to any callers that DO NOT read the env var. Then'
Write-Host '  destroy this terminal scrollback.'
Write-Host '============================================================'
Write-Host ''
Write-Host "  $newKey" -ForegroundColor Yellow
Write-Host ''
