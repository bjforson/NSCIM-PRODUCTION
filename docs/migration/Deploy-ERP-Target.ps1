#Requires -RunAsAdministrator
<#
.SYNOPSIS
    NickERP Full Stack - Idempotent delta-sync target deployment

.DESCRIPTION
    Run on TARGET server (10.0.1.254) as Administrator.
    Y:\ must be mapped to the source share containing staged files.

    DESIGN: Every step checks current state first, then acts only on the delta.
    Safe to re-run anytime. Can be interrupted and resumed. Will converge to
    correct state over multiple runs.

    FOR EACH COMPONENT:
      1. File sync:     compare source vs target, copy only missing/newer files
      2. Database:      check each DB's table count, restore only if empty/missing
      3. Windows svc:   check service presence + binary path, fix only mismatches

    Deploys 7 Windows services:
      NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_NickComms, NSCIM_ImageSplitter,
      NickHR_API, NickHR_WebApp

    Restores 7 databases:
      nickscan_production, nickscan_downloads, nickscan_icums,
      nickscan_icums_staging, nickhr, nick_comms, nick_platform

    After run, summary shows: files added, DBs restored, services (re)registered.
#>

$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = "NickERP Delta-Sync Deploy"

# ============================================================================
# CONFIGURATION
# ============================================================================
$TargetRoot    = "C:\Shared\NSCIM_PRODUCTION"
$PgDataDir     = "C:\PostgreSQL\18\data"
$DumpTimestamp = "20260415-2104"
$SourceShare   = "C:\NICK ERP"
$ImageShare    = "\\172.16.1.1\image"

# Track what the script actually changed
$Changes = [ordered]@{
    FilesCopied     = 0
    FilesSkipped    = 0
    DbsRestored     = @()
    DbsSkipped      = @()
    ServicesCreated = @()
    ServicesUpdated = @()
    ServicesOK      = @()
    SecretsSet      = @()
    Warnings        = @()
}

# Service definitions
$ServiceDefs = @(
    @{ Name='NSCIM_API';         Exe="$TargetRoot\publish\API\NickScanCentralImagingPortal.API.exe"; Display='NSCIM Production API';     Dep=$null;        Src="$SourceShare\publish\API" }
    @{ Name='NSCIM_WebApp';      Exe="$TargetRoot\publish\WebApp\NickScanWebApp.New.exe";            Display='NSCIM Production WebApp';  Dep='NSCIM_API';  Src="$SourceShare\publish\WebApp" }
    @{ Name='NSCIM_Mobile';      Exe="$TargetRoot\publish\Mobile\NickScanWebApp.Mobile.exe";         Display='NSCIM Production Mobile';  Dep='NSCIM_API';  Src="$SourceShare\publish\Mobile" }
    @{ Name='NSCIM_NickComms';   Exe="$TargetRoot\publish\NickComms\NickComms.Gateway.exe";          Display='NSCIM NickComms Gateway';  Dep=$null;        Src="$SourceShare\publish\NickComms" }
    @{ Name='NickHR_API';        Exe="$TargetRoot\NickHR\deploy\api\NickHR.API.exe";                 Display='NickHR API';               Dep=$null;        Src="$SourceShare\NickHR\deploy\api" }
    @{ Name='NickHR_WebApp';     Exe="$TargetRoot\NickHR\deploy\webapp\NickHR.WebApp.exe";           Display='NickHR WebApp';            Dep='NickHR_API'; Src="$SourceShare\NickHR\deploy\webapp" }
)
# ImageSplitter handled separately via NSSM

# ============================================================================
# HELPERS
# ============================================================================
function Write-Step($num, $total, $msg) {
    Write-Host ""
    Write-Host "[$num/$total] $msg" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor DarkGray
}
function Write-Check($msg) { Write-Host "  CHECK: $msg" -ForegroundColor Gray }
function Write-Act($msg)   { Write-Host "  ACT:   $msg" -ForegroundColor Yellow }
function Write-Ok($msg)    { Write-Host "  OK:    $msg" -ForegroundColor Green }
function Write-Skip($msg)  { Write-Host "  SKIP:  $msg" -ForegroundColor DarkGray }
function Write-Warn($msg)  { Write-Host "  WARN:  $msg" -ForegroundColor Yellow; $Changes.Warnings += $msg }
function Write-Fail($msg)  { Write-Host "  FAIL:  $msg" -ForegroundColor Red }

function Pause-ForUser($msg) {
    Write-Host ""
    Write-Host "  >>> $msg" -ForegroundColor Yellow
    Write-Host "  >>> Press ENTER to continue, Ctrl+C to abort." -ForegroundColor Yellow
    Read-Host | Out-Null
}

# Sync a folder using robocopy with skip-if-same logic
# Returns a @{Copied=N, Skipped=N, Errors=N} object
function Sync-Folder {
    param([string]$Src, [string]$Dst, [string]$Name, [string[]]$ExcludeDirs = @())

    if (-not (Test-Path $Src)) {
        Write-Warn "$Name source missing: $Src"
        return @{ Copied = 0; Skipped = 0; Errors = 1 }
    }

    if (-not (Test-Path $Dst)) { New-Item -ItemType Directory -Force -Path $Dst | Out-Null }

    Write-Check "$Name : comparing $Src -> $Dst"

    # Robocopy default: copies only newer/different files (skip-same-time-and-size)
    # /E = subdirs including empty, /R:2 = 2 retries, /W:5 = 5s wait
    # /XD = exclude dirs, /NFL = no file list, /NDL = no dir list, /NJH /NP = quiet
    # /NS = no sizes, /NC = no class, /NJS = no job summary - cleaner output
    $robocopyArgs = @($Src, $Dst, '/E', '/R:2', '/W:5', '/MT:16', '/NFL', '/NDL', '/NP')
    if ($ExcludeDirs.Count -gt 0) {
        $robocopyArgs += '/XD'
        $robocopyArgs += $ExcludeDirs
    }

    $output = & robocopy @robocopyArgs 2>&1
    $exitCode = $LASTEXITCODE

    # Parse summary line: "Files : N N N N N N" (Total Copied Skipped Mismatch Failed Extras)
    $summaryLine = $output | Select-String -Pattern '^\s+Files :' | Select-Object -First 1
    $copied = 0; $skipped = 0; $errors = 0
    if ($summaryLine) {
        $parts = ($summaryLine.Line -split '\s+') | Where-Object { $_ -match '^\d+$' }
        if ($parts.Count -ge 5) {
            $copied  = [int]$parts[1]
            $skipped = [int]$parts[2]
            $errors  = [int]$parts[4]
        }
    }

    # Robocopy exit codes: 0 = no change, 1 = copied, 3 = copied+skipped, 8+ = errors
    if ($exitCode -ge 8) {
        Write-Warn "$Name : robocopy exit $exitCode - see log"
        $errors = [Math]::Max($errors, 1)
    }

    if ($copied -eq 0) {
        Write-Skip "$Name : already in sync ($skipped files match)"
    } else {
        Write-Act "$Name : copied $copied new/changed files ($skipped unchanged)"
    }

    $Changes.FilesCopied  += $copied
    $Changes.FilesSkipped += $skipped

    return @{ Copied = $copied; Skipped = $skipped; Errors = $errors }
}

$totalSteps = 9
$scriptStart = Get-Date

# ============================================================================
# STEP 1: Environment + safety checks
# ============================================================================
Write-Step 1 $totalSteps "Environment + safety"

Write-Check "Source share Y:\"
if (-not (Test-Path "$SourceShare\publish\API\NickScanCentralImagingPortal.API.exe")) {
    Write-Fail "Source share not accessible or binaries missing."
    Write-Fail "Check Y:\ is mapped. If share name differs, edit `$SourceShare at top."
    exit 1
}
Write-Ok "Y:\ accessible"

Write-Check "Disk space"
$freeGB = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if ($freeGB -lt 30) { Write-Fail "Only $freeGB GB free on C: (need 30+)"; exit 1 }
Write-Ok "C: has $freeGB GB free"

Write-Check "Not-running-on-source"
$maybeSource = (Get-Service NSCIM_API -ErrorAction SilentlyContinue) -and
               ((Get-Service NSCIM_API).Status -eq 'Running') -and
               ($env:COMPUTERNAME -match 'NSPORTAL$')   # source hostname
if ($maybeSource) {
    Write-Fail "This looks like SOURCE server ('$env:COMPUTERNAME'). Refusing to run."
    exit 1
}
Write-Ok "Target confirmed ('$env:COMPUTERNAME')"

# ============================================================================
# STEP 2: .NET 8 runtime check
# ============================================================================
Write-Step 2 $totalSteps ".NET 8 runtime"

Write-Check "dotnet --list-runtimes for AspNetCore.App 8.x"
$hasAspNet8 = (dotnet --list-runtimes 2>&1 | Select-String "AspNetCore\.App 8\.").Count -gt 0
if ($hasAspNet8) {
    Write-Skip ".NET 8 already installed"
} else {
    Write-Act ".NET 8 missing - user must install"
    Write-Host "    Download: https://dotnet.microsoft.com/download/dotnet/8.0 (Hosting Bundle)" -ForegroundColor Yellow
    Pause-ForUser "Install .NET 8 ASP.NET Core Hosting Bundle, then press ENTER"
    if (-not ((dotnet --list-runtimes 2>&1 | Select-String "AspNetCore\.App 8\.").Count)) {
        Write-Fail ".NET 8 still not detected"; exit 1
    }
    Write-Ok ".NET 8 now installed"
}

# ============================================================================
# STEP 3: PostgreSQL 18
# ============================================================================
Write-Step 3 $totalSteps "PostgreSQL 18"

Write-Check "postgresql-x64-18 service"
$pgSvc = Get-Service postgresql-x64-18 -ErrorAction SilentlyContinue
$psql = @("C:\Program Files\PostgreSQL\18\bin\psql.exe", "C:\PostgreSQL\18\bin\psql.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
$pgRestore = if ($psql) { Join-Path (Split-Path $psql) "pg_restore.exe" } else { $null }

if ($pgSvc -and $pgSvc.Status -eq 'Running' -and $psql) {
    Write-Skip "PostgreSQL 18 running ($psql)"
} else {
    Write-Act "PostgreSQL 18 not running"
    $installer = Join-Path $SourceShare "postgresql-18-windows-x64.exe"
    if (Test-Path $installer) { Write-Host "    Installer: $installer" -ForegroundColor Yellow }
    Pause-ForUser "Install PostgreSQL 18 (port 5432, data dir $PgDataDir), then press ENTER"
    $pgSvc = Get-Service postgresql-x64-18 -ErrorAction SilentlyContinue
    $psql = @("C:\Program Files\PostgreSQL\18\bin\psql.exe", "C:\PostgreSQL\18\bin\psql.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    $pgRestore = if ($psql) { Join-Path (Split-Path $psql) "pg_restore.exe" } else { $null }
    if (-not ($pgSvc -and $pgSvc.Status -eq 'Running' -and $psql)) {
        Write-Fail "PostgreSQL still not available"; exit 1
    }
}

Write-Check "DB password + connection"
$dbPwd = [Environment]::GetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "Machine")
if (-not $dbPwd) {
    $secure = Read-Host "  Enter PostgreSQL 'postgres' password" -AsSecureString
    $dbPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    [Environment]::SetEnvironmentVariable("NICKSCAN_DB_PASSWORD", $dbPwd, "Machine")
    $Changes.SecretsSet += "NICKSCAN_DB_PASSWORD"
    Write-Act "NICKSCAN_DB_PASSWORD set (Machine scope)"
}
$env:PGPASSWORD = $dbPwd
$env:NICKSCAN_DB_PASSWORD = $dbPwd

$pgVer = & $psql -h localhost -U postgres -t -c "SELECT version()" 2>&1
if ($pgVer -match 'PostgreSQL') {
    Write-Ok "Connected to PostgreSQL"
} else {
    Write-Fail "Cannot connect: $pgVer"
    exit 1
}

# ============================================================================
# STEP 4: Python + NSSM for ImageSplitter
# ============================================================================
Write-Step 4 $totalSteps "Python + NSSM"

Write-Check "Python 3.12+"
$pyOk = $false
try { $py = python --version 2>&1; if ($py -match 'Python 3\.(1[2-9]|[2-9]\d)') { $pyOk = $true } } catch {}
if ($pyOk) {
    Write-Skip "Python present: $py"
} else {
    Write-Act "Python missing"
    Pause-ForUser "Install Python 3.12+ (winget install Python.Python.3.12), then press ENTER"
}

Write-Check "NSSM in $TargetRoot\tools\nssm-2.24"
$nssmTarget = "$TargetRoot\tools\nssm-2.24\win64\nssm.exe"
if (Test-Path $nssmTarget) {
    Write-Skip "NSSM present"
} else {
    $nssmSrc = "$SourceShare\tools\nssm-2.24"
    if (Test-Path $nssmSrc) {
        if (-not (Test-Path "$TargetRoot\tools")) { New-Item -ItemType Directory -Force -Path "$TargetRoot\tools" | Out-Null }
        Sync-Folder -Src $nssmSrc -Dst "$TargetRoot\tools\nssm-2.24" -Name "NSSM" | Out-Null
    } else {
        Write-Warn "NSSM not staged on Y:\ - ImageSplitter won't register"
    }
}

# ============================================================================
# STEP 5: Directory structure + file delta sync
# ============================================================================
Write-Step 5 $totalSteps "Directory tree + file delta sync"

Write-Check "Required directories"
$dirs = @(
    "$TargetRoot\publish\API", "$TargetRoot\publish\WebApp", "$TargetRoot\publish\Mobile",
    "$TargetRoot\publish\NickComms",
    "$TargetRoot\NickHR\deploy\api", "$TargetRoot\NickHR\deploy\webapp",
    "$TargetRoot\Data\Logs", "$TargetRoot\Data\ICUMS\Outbox", "$TargetRoot\Data\ICUMS\Inbox",
    "$TargetRoot\Data\ICUMS\Downloads", "$TargetRoot\Data\ICUMS\Archive",
    "$TargetRoot\Data\FS6000\Staging",
    "$TargetRoot\services\image-splitter", "$TargetRoot\tools"
)
$created = 0
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null; $created++ }
}
if ($created -gt 0) { Write-Act "Created $created missing directories" } else { Write-Skip "All directories exist" }

# Delta-sync each folder. Robocopy auto-skips files with matching size+timestamp.
$syncs = @(
    @{ Src="$SourceShare\publish\API";               Dst="$TargetRoot\publish\API";               Name='NSCIM API bin' }
    @{ Src="$SourceShare\publish\WebApp";            Dst="$TargetRoot\publish\WebApp";            Name='NSCIM WebApp bin' }
    @{ Src="$SourceShare\publish\Mobile";            Dst="$TargetRoot\publish\Mobile";            Name='NSCIM Mobile bin' }
    @{ Src="$SourceShare\publish\NickComms";         Dst="$TargetRoot\publish\NickComms";         Name='NickComms bin' }
    @{ Src="$SourceShare\NickHR\deploy\api";         Dst="$TargetRoot\NickHR\deploy\api";         Name='NickHR API bin' }
    @{ Src="$SourceShare\NickHR\deploy\webapp";      Dst="$TargetRoot\NickHR\deploy\webapp";      Name='NickHR WebApp bin' }
    @{ Src="$SourceShare\services\image-splitter";  Dst="$TargetRoot\services\image-splitter";  Name='ImageSplitter (Python)';  Excl=@('venv','__pycache__','.pytest_cache','logs') }
    @{ Src="$SourceShare\Data";                      Dst="$TargetRoot\Data";                      Name='Runtime Data (logs etc)' }
)
foreach ($s in $syncs) {
    Sync-Folder -Src $s.Src -Dst $s.Dst -Name $s.Name -ExcludeDirs $s.Excl | Out-Null
}

# Verify all 6 .exe binaries present and log versions
Write-Host ""
Write-Check "Binary verification"
$allBinsOk = $true
foreach ($svc in $ServiceDefs) {
    if (Test-Path $svc.Exe) {
        $v = (Get-Item $svc.Exe).VersionInfo.FileVersion
        Write-Ok "$($svc.Name): v$v"
    } else {
        Write-Fail "$($svc.Name): binary MISSING ($($svc.Exe))"
        $allBinsOk = $false
    }
}
if (-not $allBinsOk) {
    Write-Fail "Binary verification failed. Resolve then re-run."
    exit 1
}

# ============================================================================
# STEP 6: Database delta restore (check-then-restore)
# ============================================================================
Write-Step 6 $totalSteps "Database delta restore"

$dumpDir = "$SourceShare\db-dumps\$DumpTimestamp"
if (-not (Test-Path $dumpDir)) {
    Write-Fail "Dump dir not found: $dumpDir"
    Get-ChildItem "$SourceShare\db-dumps" -Directory -ErrorAction SilentlyContinue |
        Select-Object -First 5 | ForEach-Object { Write-Host "    Available: $($_.Name)" -ForegroundColor Yellow }
    exit 1
}

# Copy dumps locally first (delta-sync: skips if already there)
$localDumps = "C:\db-dumps\$DumpTimestamp"
Sync-Folder -Src $dumpDir -Dst $localDumps -Name 'DB dumps' | Out-Null

$dbFiles = Get-ChildItem $localDumps -Filter *.dump | Sort-Object Name
Write-Check "$($dbFiles.Count) dump files available"

foreach ($f in $dbFiles) {
    $db = $f.BaseName
    Write-Check "$db : checking existing state"

    # Does DB exist?
    $exists = & $psql -h localhost -U postgres -t -c "SELECT 1 FROM pg_database WHERE datname = '$db'" 2>$null
    $dbExists = ($exists -match '1')

    if ($dbExists) {
        # Does it have user tables? (an empty DB = can restore over)
        $tableCountRaw = & $psql -h localhost -U postgres -d $db -t -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" 2>$null
        $tableCountStr = ($tableCountRaw -join "`n") -replace '\s+', ''
        $tableCount = if ($tableCountStr -match '^\d+$') { [int]$tableCountStr } else { 0 }
        if ($tableCount -gt 0) {
            $sz = (& $psql -h localhost -U postgres -t -c "SELECT pg_size_pretty(pg_database_size('$db'))" 2>$null).Trim()
            Write-Skip "$db : $tableCount tables already present ($sz) - skipping restore"
            $Changes.DbsSkipped += $db
            continue
        } else {
            Write-Act "$db : exists but empty (0 tables), restoring"
        }
    } else {
        Write-Act "$db : creating + restoring from $($f.Name) ($([math]::Round($f.Length/1MB, 0)) MB)"
        & $psql -h localhost -U postgres -c "CREATE DATABASE $db OWNER postgres ENCODING 'UTF8' TEMPLATE template0" 2>$null | Out-Null
    }

    # Restore (parallel jobs for bigger dumps)
    $jobs = if ($f.Length -gt 500MB) { 4 } else { 1 }
    & $pgRestore -h localhost -U postgres -d $db --no-owner --no-privileges -j $jobs $f.FullName 2>$null

    $newSize = (& $psql -h localhost -U postgres -t -c "SELECT pg_size_pretty(pg_database_size('$db'))" 2>$null).Trim()
    $newTables = (& $psql -h localhost -U postgres -d $db -t -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" 2>$null).Trim()
    Write-Ok "$db : restored ($newTables tables, $newSize)"
    $Changes.DbsRestored += $db
}

# ============================================================================
# STEP 7: Secrets + Defender + firewall (delta-sync)
# ============================================================================
Write-Step 7 $totalSteps "Secrets, Defender, firewall"

$requiredSecrets = @(
    @{ Name='NICKSCAN_DB_PASSWORD';             Desc='PostgreSQL password' }
    @{ Name='NICKSCAN_ASE_PASSWORD';            Desc='ASE scanner DB password' }
    @{ Name='NICKSCAN_ICUMS_AUTH_KEY';          Desc='ICUMS API auth key' }
    @{ Name='NICKSCAN_ICUMS_DOCS_AUTH_KEY';     Desc='ICUMS Documents API auth key' }
    @{ Name='NICKSCAN_ICUMS_JSON_AUTH_KEY';     Desc='ICUMS JSON API auth key' }
    @{ Name='NICKSCAN_JWT_SECRET_KEY';          Desc='JWT signing secret' }
    @{ Name='NICKSCAN_SUPERADMIN_PASSWORD';     Desc='Built-in superadmin password' }
    @{ Name='NICKSCAN_FS6000_NETWORK_PASSWORD'; Desc='FS6000 SMB share password' }
    @{ Name='NICKSCAN_SERVICE_API_KEY';         Desc='Internal service-to-service key' }
    @{ Name='NICKSCAN_SETTINGS_ENCRYPTION_KEY'; Desc='DB settings encryption key' }
    @{ Name='NICKCOMMS_API_KEY_NICKHR';         Desc='NickComms API key for NickHR' }
    @{ Name='NICKCOMMS_API_KEY_NSCIS';          Desc='NickComms API key for NSCIS' }
    @{ Name='NICKCOMMS_BASE_URL';               Desc='NickComms base URL'; Default="http://localhost:5220" }
    @{ Name='NICKHR_SMTP_PASSWORD';             Desc='NickHR SMTP password'; Optional=$true }
    @{ Name='NICKSCAN_API_CERT_PASSWORD';       Desc='HTTPS cert password'; Optional=$true }
    @{ Name='NICKSCAN_API_CERT_THUMBPRINT';     Desc='HTTPS cert thumbprint'; Optional=$true }
)

foreach ($s in $requiredSecrets) {
    Write-Check "env: $($s.Name)"
    $current = [Environment]::GetEnvironmentVariable($s.Name, "Machine")
    if ($current) { Write-Skip "already set"; continue }

    if ($s.Default) {
        [Environment]::SetEnvironmentVariable($s.Name, $s.Default, "Machine")
        Write-Act "set to default: $($s.Default)"
        $Changes.SecretsSet += $s.Name
        continue
    }

    if ($s.Optional) { Write-Skip "optional, not set" ; continue }

    $val = Read-Host "    Enter $($s.Name) ($($s.Desc)) [ENTER to skip]"
    if ($val) {
        [Environment]::SetEnvironmentVariable($s.Name, $val, "Machine")
        Write-Act "set"
        $Changes.SecretsSet += $s.Name
    } else {
        Write-Warn "$($s.Name) unset - features may fail"
    }
}

Write-Check "Windows Defender exclusions"
$currentExcl = (Get-MpPreference -ErrorAction SilentlyContinue).ExclusionPath
$defenderChanges = 0
foreach ($path in @($TargetRoot, $PgDataDir)) {
    if ($currentExcl -notcontains $path) {
        Add-MpPreference -ExclusionPath $path -ErrorAction SilentlyContinue
        $defenderChanges++
    }
}
foreach ($svc in $ServiceDefs) {
    $exeName = Split-Path $svc.Exe -Leaf
    Add-MpPreference -ExclusionProcess $exeName -ErrorAction SilentlyContinue
}
if ($defenderChanges -gt 0) { Write-Act "Added $defenderChanges Defender path exclusions" } else { Write-Skip "Defender exclusions already set" }

Write-Check "Firewall rules"
$ports = @(
    @{ Name='NickERP-NSCIM-API-HTTP';    Port=5205 },
    @{ Name='NickERP-NSCIM-API-HTTPS';   Port=5206 },
    @{ Name='NickERP-NickHR-API';        Port=5215 },
    @{ Name='NickERP-NickComms-Gateway'; Port=5220 },
    @{ Name='NickERP-NSCIM-Web-HTTP';    Port=5299 },
    @{ Name='NickERP-NSCIM-Web-HTTPS';   Port=5300 },
    @{ Name='NickERP-NickHR-Web-HTTP';   Port=5310 },
    @{ Name='NickERP-NickHR-Web-HTTPS';  Port=5311 },
    @{ Name='NickERP-ImageSplitter';     Port=5320 }
)
$fwAdded = 0
foreach ($fw in $ports) {
    if (-not (Get-NetFirewallRule -DisplayName $fw.Name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $fw.Name -Direction Inbound -LocalPort $fw.Port -Protocol TCP -Action Allow | Out-Null
        $fwAdded++
    }
}
if ($fwAdded -gt 0) { Write-Act "Added $fwAdded new firewall rules" } else { Write-Skip "All 9 firewall rules already present" }

Write-Check "Z:\ image share"
if (Test-Path 'Z:\') { Write-Skip "Z:\ mounted" } else {
    Write-Warn "Z:\ not mapped. Images won't load until:"
    Write-Host "    New-SmbMapping -LocalPath Z: -RemotePath '$ImageShare' -Persistent `$true -UserName <u> -Password <p>" -ForegroundColor Yellow
}

# ============================================================================
# STEP 8: Register Windows services (delta)
# ============================================================================
Write-Step 8 $totalSteps "Register Windows services (delta)"

foreach ($svc in $ServiceDefs) {
    Write-Check "service: $($svc.Name)"
    $existing = Get-Service $svc.Name -ErrorAction SilentlyContinue
    $wmi      = Get-WmiObject Win32_Service -Filter "Name='$($svc.Name)'" -ErrorAction SilentlyContinue

    if ($existing -and $wmi) {
        $currentPath = ($wmi.PathName -replace '^"|"$', '')
        if ($currentPath -eq $svc.Exe) {
            Write-Skip "$($svc.Name) already registered with correct path"
            $Changes.ServicesOK += $svc.Name
            continue
        }
        Write-Act "$($svc.Name) exists with wrong path: $currentPath -> $($svc.Exe)"
        Stop-Service $svc.Name -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe delete $svc.Name | Out-Null
        Start-Sleep -Seconds 2
        $Changes.ServicesUpdated += $svc.Name
    } else {
        Write-Act "$($svc.Name) not registered, creating"
        $Changes.ServicesCreated += $svc.Name
    }

    if (-not (Test-Path $svc.Exe)) { Write-Fail "Binary missing: $($svc.Exe)"; continue }

    $params = @{
        Name           = $svc.Name
        BinaryPathName = $svc.Exe
        DisplayName    = $svc.Display
        StartupType    = 'Automatic'
    }
    if ($svc.Dep) { $params.DependsOn = $svc.Dep }
    New-Service @params | Out-Null
    sc.exe failure $svc.Name reset= 86400 actions= "restart/5000/restart/5000/none/0" | Out-Null
    Write-Ok "$($svc.Name) registered"
}

# ImageSplitter via NSSM (Python wrapper)
Write-Check "service: NSCIM_ImageSplitter (NSSM wrapper)"
if (Test-Path $nssmTarget) {
    $existing = Get-Service NSCIM_ImageSplitter -ErrorAction SilentlyContinue
    $splitterDir = "$TargetRoot\services\image-splitter"
    $pythonExe = (Get-Command python -ErrorAction SilentlyContinue).Source
    $mainPy = "$splitterDir\main.py"

    if ($existing) {
        Write-Skip "NSCIM_ImageSplitter already registered"
        $Changes.ServicesOK += 'NSCIM_ImageSplitter'
    } elseif ($pythonExe -and (Test-Path $mainPy)) {
        Write-Act "Registering NSCIM_ImageSplitter via NSSM"
        & $nssmTarget install NSCIM_ImageSplitter $pythonExe $mainPy | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter AppDirectory $splitterDir | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter DisplayName "NSCIM Image Splitting Service" | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter Start SERVICE_AUTO_START | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter DependOnService NSCIM_API | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter AppStdout "$TargetRoot\Data\Logs\splitter.log" | Out-Null
        & $nssmTarget set NSCIM_ImageSplitter AppStderr "$TargetRoot\Data\Logs\splitter-error.log" | Out-Null
        sc.exe failure NSCIM_ImageSplitter reset= 86400 actions= "restart/5000/restart/5000/none/0" | Out-Null
        Write-Ok "NSCIM_ImageSplitter registered"
        $Changes.ServicesCreated += 'NSCIM_ImageSplitter'
    } else {
        Write-Warn "Python or main.py not found - ImageSplitter skipped"
    }
} else {
    Write-Warn "NSSM not available - ImageSplitter skipped"
}

# ============================================================================
# STEP 9: Start services + verify (only start if stopped)
# ============================================================================
Write-Step 9 $totalSteps "Start services + verify"

$startOrder = @('NSCIM_API', 'NSCIM_WebApp', 'NSCIM_Mobile', 'NSCIM_NickComms',
                'NickHR_API', 'NickHR_WebApp', 'NSCIM_ImageSplitter')

foreach ($svcName in $startOrder) {
    $s = Get-Service $svcName -ErrorAction SilentlyContinue
    if (-not $s) { continue }
    Write-Check "$svcName status"
    if ($s.Status -eq 'Running') {
        Write-Skip "already Running"
    } else {
        Write-Act "starting"
        Start-Service $svcName -ErrorAction SilentlyContinue
        # Wait for API to warm up before starting dependents
        if ($svcName -in @('NSCIM_API', 'NickHR_API')) { Start-Sleep -Seconds 8 }
    }
}

Start-Sleep -Seconds 5

# Endpoint probes
Write-Host ""
Write-Check "Endpoint probes"
function Test-Endpoint {
    param([string]$Name, [string]$Url)
    try {
        $tmp = New-TemporaryFile
        $r = curl.exe -sk --max-time 10 -w "HTTP=%{http_code} T=%{time_total}s" -o $tmp.FullName $Url 2>$null
        Remove-Item $tmp.FullName -ErrorAction SilentlyContinue
        if ($r -match 'HTTP=[23]') { Write-Ok "$Name : $r" }
        else { Write-Warn "$Name : $r" }
    } catch { Write-Warn "$Name : $_" }
}
Test-Endpoint "NSCIM WebApp" "https://localhost:5300/api/server/version"
Test-Endpoint "NSCIM API"    "https://localhost:5206/api/server/version"
Test-Endpoint "NickHR Web"   "https://localhost:5311/"
Test-Endpoint "NickHR API"   "http://localhost:5215/"
Test-Endpoint "NickComms"    "http://localhost:5220/"

# ============================================================================
# SUMMARY
# ============================================================================
$elapsed = [math]::Round(((Get-Date) - $scriptStart).TotalMinutes, 1)

Write-Host ""
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "  DEPLOYMENT RUN SUMMARY ($elapsed min)" -ForegroundColor Cyan
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host ""
Write-Host "  Files:"
Write-Host "    Copied (new/changed): $($Changes.FilesCopied)"
Write-Host "    Unchanged:            $($Changes.FilesSkipped)"
Write-Host ""
Write-Host "  Databases:"
if ($Changes.DbsRestored.Count -gt 0) { Write-Host "    Restored:  $($Changes.DbsRestored -join ', ')" -ForegroundColor Green }
if ($Changes.DbsSkipped.Count -gt 0)  { Write-Host "    Skipped:   $($Changes.DbsSkipped -join ', ')  (already populated)" -ForegroundColor DarkGray }
Write-Host ""
Write-Host "  Services:"
if ($Changes.ServicesCreated.Count -gt 0) { Write-Host "    Created:   $($Changes.ServicesCreated -join ', ')" -ForegroundColor Green }
if ($Changes.ServicesUpdated.Count -gt 0) { Write-Host "    Updated:   $($Changes.ServicesUpdated -join ', ')" -ForegroundColor Yellow }
if ($Changes.ServicesOK.Count -gt 0)      { Write-Host "    Unchanged: $($Changes.ServicesOK -join ', ')" -ForegroundColor DarkGray }
Write-Host ""
Write-Host "  Secrets set: $($Changes.SecretsSet.Count)" -ForegroundColor $(if ($Changes.SecretsSet.Count -gt 0) { 'Yellow' } else { 'DarkGray' })
if ($Changes.Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "  Warnings ($($Changes.Warnings.Count)):" -ForegroundColor Yellow
    $Changes.Warnings | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "  Service status:"
Get-Service NSCIM_*, NickHR_* -ErrorAction SilentlyContinue | ForEach-Object {
    $color = if ($_.Status -eq 'Running') { 'Green' } else { 'Red' }
    Write-Host "    $($_.Name.PadRight(22)) $($_.Status)" -ForegroundColor $color
}

Write-Host ""
Write-Host "  Access URLs:" -ForegroundColor White
Write-Host "    NSCIM:   https://$(hostname):5300" -ForegroundColor Green
Write-Host "    NickHR:  https://$(hostname):5311" -ForegroundColor Green
Write-Host ""
Write-Host "  This script is safe to re-run anytime. It will detect state drift and fix only what's needed." -ForegroundColor Cyan
