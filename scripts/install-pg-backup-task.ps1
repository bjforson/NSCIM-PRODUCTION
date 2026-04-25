# Register the nightly Postgres backup with Windows Task Scheduler.
#
# Run once, as Administrator. Idempotent — re-running unregisters
# and reinstalls the task.

[CmdletBinding()]
param(
    [string] $TaskName       = 'NickERP_PgBackup_Nightly',
    [string] $TaskDescription = 'NickERP nightly pg_dump of all platform databases. See scripts/pg-backup.ps1.',
    [string] $ScriptPath     = 'C:\Shared\NSCIM_PRODUCTION\scripts\pg-backup.ps1',
    [string] $RunAt          = '02:00',
    [string] $User           = 'NT AUTHORITY\SYSTEM'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ScriptPath)) { throw "Script not found at $ScriptPath" }

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Task $TaskName already exists; unregistering before reinstall."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$action  = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""

$trigger = New-ScheduledTaskTrigger -Daily -At $RunAt
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -StartWhenAvailable `
    -DontStopIfGoingOnBatteries

# Run as SYSTEM so it doesn't depend on a logged-in user.
# SYSTEM inherits Machine env vars, including NICKSCAN_DB_PASSWORD.
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask `
    -TaskName $TaskName `
    -Description $TaskDescription `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal | Out-Null

Write-Host "Registered $TaskName -> $ScriptPath, runs daily at $RunAt as $User." -ForegroundColor Green
Write-Host "Test it manually now with:  Start-ScheduledTask -TaskName $TaskName"
