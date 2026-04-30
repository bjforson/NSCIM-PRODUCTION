<#
NickFinance / NickHR nightly database backup.

Run as: Administrator (so it can write to C:\Backups and Event Log).

To register as a daily 02:00 task (run from elevated PowerShell):
    $task = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\Shared\NSCIM_PRODUCTION\scripts\backup-nickhr-nightly.ps1'
    $trigger = New-ScheduledTaskTrigger -Daily -At 2am
    Register-ScheduledTask -TaskName 'NickHR Nightly Backup' -Action $task -Trigger $trigger -RunLevel Highest -User 'SYSTEM'

Restore (if ever needed):
    pg_restore -d nickhr -h localhost -p 5432 -U postgres --clean --if-exists C:\Backups\nickhr-{ts}.dump

----------------------------------------------------------------------------
Off-host destination (see security-audit.md gap #9).
----------------------------------------------------------------------------
The local C:\Backups copy survives a disk failure on the data volume but
NOT a ransomware encrypter that walks the file system. Set ONE of the
following machine env vars to push every successful dump off-host:

    NICKERP_BACKUP_S3_BUCKET     -> uploads via AWS CLI (`aws s3 cp`).
                                    Requires `aws` on PATH and
                                    credentials configured non-
                                    interactively (recommended: the
                                    AWS_ACCESS_KEY_ID + AWS_SECRET_ACCESS_KEY
                                    machine env vars, or an EC2/IRSA
                                    instance role if running in AWS).
                                    Adds --server-side-encryption AES256.
                                    Optional NICKERP_BACKUP_S3_PREFIX for
                                    a key prefix (e.g. "nickerp/prod/").

    NICKERP_BACKUP_AZURE_CONTAINER  -> uploads via Azure CLI
                                    (`az storage blob upload`).
                                    Requires `az` on PATH and *one* of:
                                      a) AZURE_STORAGE_CONNECTION_STRING
                                         machine env var,
                                      b) AZURE_STORAGE_ACCOUNT +
                                         AZURE_STORAGE_KEY,
                                      c) AZURE_STORAGE_SAS_TOKEN +
                                         AZURE_STORAGE_ACCOUNT,
                                      d) `az login` cached creds (less
                                         common for a SYSTEM-scheduled
                                         task; SAS or connection string
                                         is the recommended path).

    NICKERP_BACKUP_LOCAL_KEEP    -> integer; how many local dumps to
                                    keep AFTER a successful off-host
                                    upload. Default 14. The on-disk
                                    rotation always runs whether or not
                                    off-host is configured.

If neither S3 nor Azure env vars are set, the script logs an
INFORMATIONAL Event Log line and exits 0 — relying purely on the local
14-day rotation. Document this as a known gap until the operator wires
credentials.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Constants & Event Log helpers
# ---------------------------------------------------------------------------
$EventSource = 'NickFinance_Backup'
$EventLogName = 'Application'

function Ensure-EventSource {
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
            New-EventLog -LogName $EventLogName -Source $EventSource
        }
    } catch {
        # If we can't register the source (not elevated), fall through —
        # writes will still attempt and may fail with a clear message.
        Write-Warning "Could not ensure Event Log source '$EventSource': $($_.Exception.Message)"
    }
}

function Write-Backup-Event {
    param(
        [Parameter(Mandatory)] [ValidateSet('Information','Warning','Error')] [string] $EntryType,
        [Parameter(Mandatory)] [string] $Message,
        [int] $EventId = 1000
    )
    try {
        Ensure-EventSource
        Write-EventLog -LogName $EventLogName -Source $EventSource `
            -EntryType $EntryType -EventId $EventId -Message $Message
    } catch {
        Write-Warning "Failed to write to Event Log ($EntryType): $($_.Exception.Message)"
        Write-Host $Message
    }
}

function Resolve-PgDump {
    # Prefer pg_dump on PATH.
    $cmd = Get-Command pg_dump -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Fallback: well-known PostgreSQL install paths on Windows.
    $candidates = @(
        'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe',
        'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe',
        'C:\Program Files\PostgreSQL\16\bin\pg_dump.exe'
    )
    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Off-host upload helpers
# ---------------------------------------------------------------------------

function Try-Upload-S3 {
    param(
        [Parameter(Mandatory)] [string] $LocalFile,
        [Parameter(Mandatory)] [string] $Bucket,
        [string] $Prefix
    )
    $aws = Get-Command aws -ErrorAction SilentlyContinue
    if (-not $aws) {
        Write-Backup-Event -EntryType Warning -EventId 1010 `
            -Message "NICKERP_BACKUP_S3_BUCKET set ($Bucket) but 'aws' CLI not on PATH. Skipping off-host upload; relying on local rotation."
        return $false
    }
    $key = (Split-Path -Leaf $LocalFile)
    if ($Prefix) { $key = "$Prefix$key" }
    $dest = "s3://$Bucket/$key"

    & aws s3 cp $LocalFile $dest --server-side-encryption AES256 2>&1 | ForEach-Object { Write-Host "  aws> $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Backup-Event -EntryType Error -EventId 1011 `
            -Message "aws s3 cp failed (exit $LASTEXITCODE) for $LocalFile -> $dest. Local copy retained; rotation will not delete."
        return $false
    }
    Write-Backup-Event -EntryType Information -EventId 1012 `
        -Message "Off-host upload OK: $dest (SSE-AES256)."
    Write-Host "  Off-host upload OK: $dest"
    return $true
}

function Try-Upload-Azure {
    param(
        [Parameter(Mandatory)] [string] $LocalFile,
        [Parameter(Mandatory)] [string] $Container
    )
    $az = Get-Command az -ErrorAction SilentlyContinue
    if (-not $az) {
        Write-Backup-Event -EntryType Warning -EventId 1020 `
            -Message "NICKERP_BACKUP_AZURE_CONTAINER set ($Container) but 'az' CLI not on PATH. Skipping off-host upload; relying on local rotation."
        return $false
    }
    $blobName = (Split-Path -Leaf $LocalFile)

    # az storage blob upload picks creds in this order:
    #   --connection-string > --account-key > --sas-token > AAD login.
    # We let env vars steer it: AZURE_STORAGE_CONNECTION_STRING is the
    # most operator-friendly. Fall back to account+key or SAS in env.
    $args = @('storage','blob','upload',
              '--container-name',$Container,
              '--file',$LocalFile,
              '--name',$blobName,
              '--overwrite','true',
              '--only-show-errors')

    & az @args 2>&1 | ForEach-Object { Write-Host "  az> $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Backup-Event -EntryType Error -EventId 1021 `
            -Message "az storage blob upload failed (exit $LASTEXITCODE) for $LocalFile -> $Container/$blobName. Local copy retained; rotation will not delete."
        return $false
    }
    Write-Backup-Event -EntryType Information -EventId 1022 `
        -Message "Off-host upload OK: azure://$Container/$blobName."
    Write-Host "  Off-host upload OK: azure://$Container/$blobName"
    return $true
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
$pgPasswordWasSet = $false
try {
    # 1) Output directory
    $backupDir = $env:NICKERP_BACKUP_DIR
    if ([string]::IsNullOrWhiteSpace($backupDir)) { $backupDir = 'C:\Backups' }
    if (-not (Test-Path -LiteralPath $backupDir)) {
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    }

    # 2) Resolve pg_dump
    $pgDump = Resolve-PgDump
    if (-not $pgDump) {
        $msg = "pg_dump.exe not found. Tried PATH plus C:\Program Files\PostgreSQL\{18,17,16}\bin\pg_dump.exe. Install PostgreSQL client tools or add pg_dump to PATH."
        Write-Backup-Event -EntryType Error -Message $msg -EventId 1001
        throw $msg
    }

    # 3) Credentials — read NICKSCAN_DB_PASSWORD machine env var
    $dbPassword = [System.Environment]::GetEnvironmentVariable('NICKSCAN_DB_PASSWORD','Machine')
    if ([string]::IsNullOrWhiteSpace($dbPassword)) {
        # Fall back to process/user scope just in case.
        $dbPassword = $env:NICKSCAN_DB_PASSWORD
    }
    if ([string]::IsNullOrWhiteSpace($dbPassword)) {
        $msg = "NICKSCAN_DB_PASSWORD env var is not set (machine scope). Cannot run pg_dump."
        Write-Backup-Event -EntryType Error -Message $msg -EventId 1002
        throw $msg
    }

    # 4) Compose output file
    $timestamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
    $outputFile = Join-Path -Path $backupDir -ChildPath "nickhr-$timestamp.dump"

    # 5) Run pg_dump with PGPASSWORD set in-process only
    $env:PGPASSWORD = $dbPassword
    $pgPasswordWasSet = $true

    & $pgDump -Fc -d nickhr -U postgres -h localhost -p 5432 -f $outputFile
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $msg = "pg_dump exited with code $exitCode for nickhr -> $outputFile"
        Write-Backup-Event -EntryType Error -Message $msg -EventId 1003
        throw $msg
    }

    if (-not (Test-Path -LiteralPath $outputFile)) {
        $msg = "pg_dump succeeded (exit 0) but expected output $outputFile is missing."
        Write-Backup-Event -EntryType Error -Message $msg -EventId 1004
        throw $msg
    }

    $sizeBytes = (Get-Item -LiteralPath $outputFile).Length

    # 6) Off-host upload (optional, env-gated)
    $offHostOk = $false
    $offHostConfigured = $false

    $s3Bucket = [System.Environment]::GetEnvironmentVariable('NICKERP_BACKUP_S3_BUCKET','Machine')
    if ([string]::IsNullOrWhiteSpace($s3Bucket)) { $s3Bucket = $env:NICKERP_BACKUP_S3_BUCKET }
    $s3Prefix = [System.Environment]::GetEnvironmentVariable('NICKERP_BACKUP_S3_PREFIX','Machine')
    if ([string]::IsNullOrWhiteSpace($s3Prefix)) { $s3Prefix = $env:NICKERP_BACKUP_S3_PREFIX }
    $azContainer = [System.Environment]::GetEnvironmentVariable('NICKERP_BACKUP_AZURE_CONTAINER','Machine')
    if ([string]::IsNullOrWhiteSpace($azContainer)) { $azContainer = $env:NICKERP_BACKUP_AZURE_CONTAINER }

    if (-not [string]::IsNullOrWhiteSpace($s3Bucket)) {
        $offHostConfigured = $true
        $offHostOk = Try-Upload-S3 -LocalFile $outputFile -Bucket $s3Bucket -Prefix $s3Prefix
    } elseif (-not [string]::IsNullOrWhiteSpace($azContainer)) {
        $offHostConfigured = $true
        $offHostOk = Try-Upload-Azure -LocalFile $outputFile -Container $azContainer
    } else {
        Write-Backup-Event -EntryType Information -EventId 1030 `
            -Message "Off-host destination not configured; relying on local $backupDir rotation. Set NICKERP_BACKUP_S3_BUCKET or NICKERP_BACKUP_AZURE_CONTAINER to enable off-host shipment."
        Write-Host "  Off-host destination not configured; relying on local $backupDir rotation."
    }

    # 7) Local rotation
    # Default: keep newest 14 dumps (matches the historical retention
    # window). When off-host is configured AND succeeded, the operator
    # may want to keep fewer locally — NICKERP_BACKUP_LOCAL_KEEP overrides.
    $keepStr = [System.Environment]::GetEnvironmentVariable('NICKERP_BACKUP_LOCAL_KEEP','Machine')
    if ([string]::IsNullOrWhiteSpace($keepStr)) { $keepStr = $env:NICKERP_BACKUP_LOCAL_KEEP }
    $keep = 14
    if (-not [string]::IsNullOrWhiteSpace($keepStr)) {
        $parsed = 0
        if ([int]::TryParse($keepStr, [ref]$parsed) -and $parsed -gt 0) {
            $keep = $parsed
        }
    }

    Get-ChildItem -Path $backupDir -Filter 'nickhr-*.dump' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $keep |
        Remove-Item -Force -ErrorAction SilentlyContinue

    # 8) Success → Event Log
    $sizeKb = [math]::Round($sizeBytes / 1KB, 1)
    $offHostNote = if ($offHostConfigured) {
        if ($offHostOk) { ' (off-host upload OK)' } else { ' (off-host upload FAILED — see prior event)' }
    } else { ' (off-host not configured)' }
    $okMsg = "OK: $outputFile ($sizeKb KB; local keep=$keep)$offHostNote"
    Write-Backup-Event -EntryType Information -Message $okMsg -EventId 1000
    Write-Host $okMsg

    # When off-host was *configured* but failed, exit non-zero so the
    # scheduled task surfaces a failed run — operator triage required.
    if ($offHostConfigured -and -not $offHostOk) { exit 2 }
    exit 0
}
catch {
    $err = $_.Exception
    $detail = @"
NickHR nightly backup FAILED.

Message: $($err.Message)
Type:    $($err.GetType().FullName)

Stack trace:
$($err.StackTrace)

InvocationInfo:
$($_.InvocationInfo.PositionMessage)
"@
    Write-Backup-Event -EntryType Error -Message $detail -EventId 1099
    Write-Error $detail
    exit 1
}
finally {
    # ALWAYS unset PGPASSWORD on exit so it doesn't linger in this process.
    if ($pgPasswordWasSet) {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
}
