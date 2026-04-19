# Post-hotpatch machine-env setup (applied 2026-04-19 during ops recovery).
#
# NSCIM_API runs as LocalSystem and cannot read the operator's user-mapped Z:\
# drive to reach the FS6000 share. We hand it the UNC path via a machine-level
# env var, which LocalSystem can read. Deploy.ps1 Phase 3.5 already sets the
# equivalent NSSM env var on NSCIM_ImageSplitter; this script covers the API
# (which reads `FS6000:NetworkSharePath` from config, matching the double-
# underscore env-var convention used by .NET configuration).
#
# Idempotent. Run on a fresh box after a cold rebuild, then restart NSCIM_API.

param(
    [string]$UncPath = '\\172.16.1.1\Image\23301FS01'
)

$ErrorActionPreference = 'Stop'

Write-Host "Setting machine env var FS6000__NetworkSharePath = $UncPath"
[System.Environment]::SetEnvironmentVariable(
    'FS6000__NetworkSharePath',
    $UncPath,
    [System.EnvironmentVariableTarget]::Machine)

# Read it back to confirm
$readBack = [System.Environment]::GetEnvironmentVariable(
    'FS6000__NetworkSharePath',
    [System.EnvironmentVariableTarget]::Machine)

if ($readBack -eq $UncPath) {
    Write-Host "  [OK] Machine env var set." -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Read-back mismatch: $readBack" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Remember to restart NSCIM_API so the new process inherits the env:"
Write-Host "  Restart-Service -Name NSCIM_API"
