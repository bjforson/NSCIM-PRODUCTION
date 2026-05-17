<#
.SYNOPSIS
Builds a telemetry-gated readiness report for retiring deprecated route families.

.DESCRIPTION
Reads the production EndpointUsageLog table and groups deprecated/phase route
usage into route families. The script is read-only. It is intended to be run
before each endpoint-retirement batch so compatibility aliases are removed only
after the configured zero-usage window has elapsed.

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-EndpointRetirementReadiness.ps1

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-EndpointRetirementReadiness.ps1 -DaysWithZeroUsage 45 -ObservationDays 45 -AsJson
#>

#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1",
    [ValidateRange(1, 365)]
    [int]$DaysWithZeroUsage = 30,
    [ValidateRange(1, 365)]
    [int]$ObservationDays = 30,
    [ValidateRange(1, 1000)]
    [int]$EndpointDetailLimit = 50,
    [switch]$Detailed,
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory "..\.."))
$npgsqlHelper = Join-Path $repoRoot "scripts\postgres\_NpgsqlHelper.ps1"

if (-not (Test-Path -LiteralPath $npgsqlHelper)) {
    throw "Npgsql helper not found: $npgsqlHelper"
}

. $npgsqlHelper

if (-not $env:NICKSCAN_DB_PASSWORD) {
    $env:NICKSCAN_DB_PASSWORD = [Environment]::GetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "Machine")
}

function ConvertTo-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)

    return '"' + ($Value -replace '"', '""') + '"'
}

function Get-RequiredColumnRef {
    param(
        [Parameter(Mandatory = $true)][object[]]$Columns,
        [Parameter(Mandatory = $true)][string]$LogicalName
    )

    $column = $Columns | Where-Object { $_.column_name.ToString().ToLowerInvariant() -eq $LogicalName.ToLowerInvariant() } | Select-Object -First 1
    if (-not $column) {
        throw "Endpoint usage table is missing required column '$LogicalName'."
    }

    return ConvertTo-SqlIdentifier -Value $column.column_name.ToString()
}

function Normalize-EndpointPath {
    param([AllowNull()][string]$Endpoint)

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        return ""
    }

    $path = $Endpoint.Trim().Split("?")[0].Trim()
    $path = [regex]::Replace($path, "&(?:exp|uid|sig)=[^/]*.*$", "", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $path.StartsWith("/")) {
        $path = "/$path"
    }

    while ($path.Length -gt 1 -and $path.EndsWith("/")) {
        $path = $path.Substring(0, $path.Length - 1)
    }

    return $path.ToLowerInvariant()
}

function Get-RouteFamily {
    param([AllowNull()][string]$Endpoint)

    $path = Normalize-EndpointPath -Endpoint $Endpoint
    if ([string]::IsNullOrWhiteSpace($path)) {
        return "(unknown)"
    }

    if ($path.StartsWith("/api/imageprocessing/image/", [System.StringComparison]::Ordinal)) {
        return "/api/imageprocessing/image/*"
    }

    if ($path.StartsWith("/api/imageprocessing/container/", [System.StringComparison]::Ordinal)) {
        return "/api/imageprocessing/container/*"
    }

    if ($path -eq "/api/image" -or $path.StartsWith("/api/image/", [System.StringComparison]::Ordinal)) {
        return "/api/image/*"
    }

    if ($path -eq "/api/image-analysis-management" -or $path.StartsWith("/api/image-analysis-management/", [System.StringComparison]::Ordinal)) {
        return "/api/image-analysis-management/*"
    }

    $segments = $path.Trim("/").Split("/", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -ge 2 -and $segments[0] -eq "api") {
        return "/api/$($segments[1])/*"
    }

    if ($segments.Count -ge 2) {
        return "/$($segments[0])/$($segments[1])/*"
    }

    return $path
}

function ConvertTo-Int64Value {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return [int64]0 }
    return [int64]$Value
}

function ConvertTo-DateTimeValue {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    return [DateTime]$Value
}

function New-FamilySummary {
    param(
        [Parameter(Mandatory = $true)][string]$Family,
        [Parameter(Mandatory = $true)][object[]]$EndpointRows,
        [Parameter(Mandatory = $true)][DateTime]$NowUtc,
        [Parameter(Mandatory = $true)][int]$SafeWindowDays
    )

    $totalCalls = ($EndpointRows | ForEach-Object { ConvertTo-Int64Value $_.TotalCalls } | Measure-Object -Sum).Sum
    $recentCalls = ($EndpointRows | ForEach-Object { ConvertTo-Int64Value $_.RecentCalls } | Measure-Object -Sum).Sum
    $errorCalls = ($EndpointRows | ForEach-Object { ConvertTo-Int64Value $_.ErrorCalls } | Measure-Object -Sum).Sum
    $uniqueCallers = ($EndpointRows | ForEach-Object { ConvertTo-Int64Value $_.UniqueCallers } | Measure-Object -Sum).Sum
    $lastCall = $EndpointRows |
        ForEach-Object { ConvertTo-DateTimeValue $_.LastCallUtc } |
        Where-Object { $null -ne $_ } |
        Sort-Object -Descending |
        Select-Object -First 1
    $firstCall = $EndpointRows |
        ForEach-Object { ConvertTo-DateTimeValue $_.FirstCallUtc } |
        Where-Object { $null -ne $_ } |
        Sort-Object |
        Select-Object -First 1
    $daysSinceLastCall = if ($lastCall) { [math]::Floor(($NowUtc - $lastCall).TotalDays) } else { $null }
    $removeAfterUtc = if ($lastCall) { $lastCall.AddDays($SafeWindowDays) } else { $null }

    $status = if ($null -eq $lastCall) {
        "COLLECTING_BASELINE"
    } elseif ($recentCalls -gt 0) {
        "BLOCKED_RECENT_USAGE"
    } elseif (($NowUtc - $lastCall).TotalDays -ge $SafeWindowDays) {
        "READY_FOR_BATCH_REVIEW"
    } else {
        "WAITING_FOR_ZERO_USAGE_WINDOW"
    }

    [pscustomobject]@{
        Family = $Family
        Status = $status
        EndpointCount = @($EndpointRows | Select-Object -ExpandProperty Endpoint -Unique).Count
        Methods = (@($EndpointRows | Select-Object -ExpandProperty Method -Unique | Sort-Object) -join ",")
        TotalCalls = [int64]$totalCalls
        RecentCalls = [int64]$recentCalls
        ErrorCalls = [int64]$errorCalls
        UniqueCallersApprox = [int64]$uniqueCallers
        FirstCallUtc = $firstCall
        LastCallUtc = $lastCall
        DaysSinceLastCall = $daysSinceLastCall
        RemoveAfterUtc = $removeAfterUtc
    }
}

$nowUtc = [DateTime]::UtcNow
$observationCutoffUtc = $nowUtc.AddDays(-1 * $ObservationDays)
$zeroUsageCutoffUtc = $nowUtc.AddDays(-1 * $DaysWithZeroUsage)

$handle = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId

try {
    $tableInfo = Invoke-NscimQuery -Handle $handle -Sql @"
SELECT table_schema, table_name
FROM information_schema.tables
WHERE lower(table_name) = 'endpointusagelog'
ORDER BY CASE WHEN table_schema = 'public' THEN 0 ELSE 1 END, table_schema
LIMIT 1;
"@

    if ($tableInfo.Count -eq 0) {
        throw "Could not find EndpointUsageLog table in database '$Database'."
    }

    $tableSchema = $tableInfo[0].table_schema.ToString()
    $tableName = $tableInfo[0].table_name.ToString()
    $tableRef = "$(ConvertTo-SqlIdentifier -Value $tableSchema).$(ConvertTo-SqlIdentifier -Value $tableName)"

    $columns = Invoke-NscimQuery -Handle $handle -Sql @"
SELECT column_name
FROM information_schema.columns
WHERE table_schema = @tableSchema
  AND table_name = @tableName;
"@ -Parameters @{
        tableSchema = $tableSchema
        tableName = $tableName
    }

    $endpoint = Get-RequiredColumnRef -Columns $columns -LogicalName "endpoint"
    $method = Get-RequiredColumnRef -Columns $columns -LogicalName "method"
    $statusCode = Get-RequiredColumnRef -Columns $columns -LogicalName "statuscode"
    $responseTimeMs = Get-RequiredColumnRef -Columns $columns -LogicalName "responsetimems"
    $ipAddress = Get-RequiredColumnRef -Columns $columns -LogicalName "ipaddress"
    $timestamp = Get-RequiredColumnRef -Columns $columns -LogicalName "timestamp"
    $isDeprecated = Get-RequiredColumnRef -Columns $columns -LogicalName "isdeprecated"
    $isPhase3Route = Get-RequiredColumnRef -Columns $columns -LogicalName "isphase3route"

    $overall = Invoke-NscimQuery -Handle $handle -Sql @"
SELECT
    COUNT(*)::bigint AS total_rows,
    MIN($timestamp) AS first_seen_utc,
    MAX($timestamp) AS last_seen_utc
FROM $tableRef;
"@

    $usageRows = Invoke-NscimQuery -Handle $handle -Sql @"
SELECT
    lower($endpoint) AS endpoint,
    upper($method) AS method,
    COUNT(*)::bigint AS total_calls,
    SUM(CASE WHEN $timestamp >= @observationCutoffUtc THEN 1 ELSE 0 END)::bigint AS recent_calls,
    MIN($timestamp) AS first_call_utc,
    MAX($timestamp) AS last_call_utc,
    AVG($responseTimeMs)::double precision AS average_response_time_ms,
    SUM(CASE WHEN $statusCode >= 400 THEN 1 ELSE 0 END)::bigint AS error_calls,
    COUNT(DISTINCT NULLIF($ipAddress, ''))::bigint AS unique_callers,
    BOOL_OR($isDeprecated) AS is_deprecated,
    BOOL_OR($isPhase3Route) AS is_phase3_route
FROM $tableRef
WHERE $isDeprecated = TRUE
   OR $isPhase3Route = TRUE
GROUP BY lower($endpoint), upper($method)
ORDER BY BOOL_OR($isDeprecated) DESC, COUNT(*) DESC, lower($endpoint), upper($method);
"@ -Parameters @{
        observationCutoffUtc = $observationCutoffUtc
    }

    $rawEndpointSummaries = @(
        foreach ($row in $usageRows) {
            $normalizedEndpoint = Normalize-EndpointPath -Endpoint $row.endpoint
            $lastCall = ConvertTo-DateTimeValue $row.last_call_utc
            $recentCalls = ConvertTo-Int64Value $row.recent_calls
            $daysSinceLastCall = if ($lastCall) { [math]::Floor(($nowUtc - $lastCall).TotalDays) } else { $null }
            $status = if ([bool]$row.is_deprecated -and $recentCalls -gt 0) {
                "BLOCKED_RECENT_USAGE"
            } elseif ([bool]$row.is_deprecated -and $lastCall -and (($nowUtc - $lastCall).TotalDays -ge $DaysWithZeroUsage)) {
                "READY_FOR_BATCH_REVIEW"
            } elseif ([bool]$row.is_deprecated) {
                "WAITING_FOR_ZERO_USAGE_WINDOW"
            } else {
                "OBSERVE_ONLY"
            }

            [pscustomobject]@{
                Endpoint = $normalizedEndpoint
                Method = $row.method
                Family = Get-RouteFamily -Endpoint $normalizedEndpoint
                Status = $status
                TotalCalls = ConvertTo-Int64Value $row.total_calls
                RecentCalls = $recentCalls
                ErrorCalls = ConvertTo-Int64Value $row.error_calls
                UniqueCallers = ConvertTo-Int64Value $row.unique_callers
                AverageResponseTimeMs = [math]::Round([double]$row.average_response_time_ms, 2)
                FirstCallUtc = ConvertTo-DateTimeValue $row.first_call_utc
                LastCallUtc = $lastCall
                DaysSinceLastCall = $daysSinceLastCall
                RemoveAfterUtc = if ($lastCall) { $lastCall.AddDays($DaysWithZeroUsage) } else { $null }
                IsDeprecated = [bool]$row.is_deprecated
                IsPhase3Route = [bool]$row.is_phase3_route
            }
        }
    )

    $endpointSummaries = @(
        $rawEndpointSummaries |
            Group-Object Endpoint, Method, Family, IsDeprecated, IsPhase3Route |
            ForEach-Object {
                $group = @($_.Group)
                $firstCall = $group |
                    ForEach-Object { $_.FirstCallUtc } |
                    Where-Object { $null -ne $_ } |
                    Sort-Object |
                    Select-Object -First 1
                $lastCall = $group |
                    ForEach-Object { $_.LastCallUtc } |
                    Where-Object { $null -ne $_ } |
                    Sort-Object -Descending |
                    Select-Object -First 1
                $totalCalls = ($group | ForEach-Object { ConvertTo-Int64Value $_.TotalCalls } | Measure-Object -Sum).Sum
                $recentCalls = ($group | ForEach-Object { ConvertTo-Int64Value $_.RecentCalls } | Measure-Object -Sum).Sum
                $errorCalls = ($group | ForEach-Object { ConvertTo-Int64Value $_.ErrorCalls } | Measure-Object -Sum).Sum
                $uniqueCallers = ($group | ForEach-Object { ConvertTo-Int64Value $_.UniqueCallers } | Measure-Object -Sum).Sum
                $weightedResponseTotal = ($group | ForEach-Object { [double]$_.AverageResponseTimeMs * [double]$_.TotalCalls } | Measure-Object -Sum).Sum
                $averageResponse = if ($totalCalls -gt 0) { [math]::Round([double]($weightedResponseTotal / $totalCalls), 2) } else { 0 }
                $isDeprecatedSummary = [bool]($group | Select-Object -First 1).IsDeprecated
                $isPhase3Summary = [bool]($group | Select-Object -First 1).IsPhase3Route
                $daysSinceLastCall = if ($lastCall) { [math]::Floor(($nowUtc - $lastCall).TotalDays) } else { $null }
                $status = if ($isDeprecatedSummary -and $recentCalls -gt 0) {
                    "BLOCKED_RECENT_USAGE"
                } elseif ($isDeprecatedSummary -and $lastCall -and (($nowUtc - $lastCall).TotalDays -ge $DaysWithZeroUsage)) {
                    "READY_FOR_BATCH_REVIEW"
                } elseif ($isDeprecatedSummary) {
                    "WAITING_FOR_ZERO_USAGE_WINDOW"
                } else {
                    "OBSERVE_ONLY"
                }

                [pscustomobject]@{
                    Endpoint = ($group | Select-Object -First 1).Endpoint
                    Method = ($group | Select-Object -First 1).Method
                    Family = ($group | Select-Object -First 1).Family
                    Status = $status
                    TotalCalls = [int64]$totalCalls
                    RecentCalls = [int64]$recentCalls
                    ErrorCalls = [int64]$errorCalls
                    UniqueCallers = [int64]$uniqueCallers
                    AverageResponseTimeMs = $averageResponse
                    FirstCallUtc = $firstCall
                    LastCallUtc = $lastCall
                    DaysSinceLastCall = $daysSinceLastCall
                    RemoveAfterUtc = if ($lastCall) { $lastCall.AddDays($DaysWithZeroUsage) } else { $null }
                    IsDeprecated = $isDeprecatedSummary
                    IsPhase3Route = $isPhase3Summary
                }
            } |
            Sort-Object Family, Endpoint, Method
    )

    $deprecatedEndpointRows = @($endpointSummaries | Where-Object { $_.IsDeprecated })
    $phaseEndpointRows = @($endpointSummaries | Where-Object { $_.IsPhase3Route })

    $deprecatedFamilies = @(
        $deprecatedEndpointRows |
            Group-Object Family |
            ForEach-Object { New-FamilySummary -Family $_.Name -EndpointRows @($_.Group) -NowUtc $nowUtc -SafeWindowDays $DaysWithZeroUsage } |
            Sort-Object @{ Expression = { $_.Status -ne "READY_FOR_BATCH_REVIEW" } }, @{ Expression = { $_.RecentCalls }; Descending = $true }, Family
    )

    $phaseFamilies = @(
        $phaseEndpointRows |
            Group-Object Family |
            ForEach-Object { New-FamilySummary -Family $_.Name -EndpointRows @($_.Group) -NowUtc $nowUtc -SafeWindowDays $DaysWithZeroUsage } |
            Sort-Object @{ Expression = { $_.RecentCalls }; Descending = $true }, Family
    )

    $readyFamilies = @($deprecatedFamilies | Where-Object { $_.Status -eq "READY_FOR_BATCH_REVIEW" })
    $blockedFamilies = @($deprecatedFamilies | Where-Object { $_.Status -ne "READY_FOR_BATCH_REVIEW" })

    $result = [pscustomobject]@{
        GeneratedAtUtc = $nowUtc
        Database = $Database
        TenantId = $TenantId
        EndpointUsageTable = "$tableSchema.$tableName"
        DaysWithZeroUsage = $DaysWithZeroUsage
        ObservationDays = $ObservationDays
        ObservationCutoffUtc = $observationCutoffUtc
        ZeroUsageCutoffUtc = $zeroUsageCutoffUtc
        TotalTelemetryRows = if ($overall.Count -gt 0) { ConvertTo-Int64Value $overall[0].total_rows } else { 0 }
        FirstTelemetryUtc = if ($overall.Count -gt 0) { ConvertTo-DateTimeValue $overall[0].first_seen_utc } else { $null }
        LastTelemetryUtc = if ($overall.Count -gt 0) { ConvertTo-DateTimeValue $overall[0].last_seen_utc } else { $null }
        ReadyDeprecatedFamilies = $readyFamilies
        BlockedDeprecatedFamilies = $blockedFamilies
        DeprecatedEndpointDetails = $deprecatedEndpointRows
        PhaseRouteFamilies = $phaseFamilies
        PhaseRouteEndpointDetails = $phaseEndpointRows
        RetirementGuidance = @(
            "Do not remove routes unless the entire family is READY_FOR_BATCH_REVIEW.",
            "Remove only one family per batch.",
            "Before removal: run static route inventory, create publish rollback backup, build API/WebApp surfaces touched.",
            "After removal: deploy, smoke health and affected UI/API workflow, then monitor deprecated usage and service events.",
            "If any caller appears during the zero-usage window, keep the compatibility alias and rewire that caller first."
        )
    }

    if ($AsJson) {
        $result | ConvertTo-Json -Depth 8
        return
    }

    Write-Host "Endpoint Retirement Readiness" -ForegroundColor Cyan
    Write-Host "=============================" -ForegroundColor Cyan
    Write-Host ("Generated UTC: {0:u}" -f $result.GeneratedAtUtc)
    Write-Host ("Database/Tenant: {0} / {1}" -f $result.Database, $result.TenantId)
    Write-Host ("Telemetry table: {0}" -f $result.EndpointUsageTable)
    Write-Host ("Observation window: last {0} day(s), since {1:u}" -f $ObservationDays, $observationCutoffUtc)
    Write-Host ("Retirement gate: zero usage for {0} day(s), since {1:u}" -f $DaysWithZeroUsage, $zeroUsageCutoffUtc)
    Write-Host ("Telemetry rows: {0}; first={1:u}; last={2:u}" -f $result.TotalTelemetryRows, $result.FirstTelemetryUtc, $result.LastTelemetryUtc)
    Write-Host ""

    Write-Host "Deprecated Route Families" -ForegroundColor Yellow
    Write-Host "-------------------------" -ForegroundColor Yellow
    if ($deprecatedFamilies.Count -eq 0) {
        Write-Host "No deprecated route telemetry rows were found. Keep collecting telemetry before route deletion." -ForegroundColor Yellow
    } else {
        $deprecatedFamilies |
            Select-Object Family, Status, EndpointCount, Methods, TotalCalls, RecentCalls, ErrorCalls, UniqueCallersApprox, LastCallUtc, DaysSinceLastCall, RemoveAfterUtc |
            Format-Table -AutoSize
    }

    Write-Host ""
    Write-Host "Deprecated Endpoint Details" -ForegroundColor Yellow
    Write-Host "---------------------------" -ForegroundColor Yellow
    if ($deprecatedEndpointRows.Count -eq 0) {
        Write-Host "No deprecated endpoint detail rows." -ForegroundColor Yellow
    } else {
        $endpointRowsToDisplay = if ($Detailed) { $deprecatedEndpointRows } else { @($deprecatedEndpointRows | Select-Object -First $EndpointDetailLimit) }
        $endpointRowsToDisplay |
            Select-Object Family, Endpoint, Method, Status, TotalCalls, RecentCalls, ErrorCalls, UniqueCallers, LastCallUtc, RemoveAfterUtc |
            Format-Table -AutoSize
        if (-not $Detailed -and $deprecatedEndpointRows.Count -gt $EndpointDetailLimit) {
            Write-Host ("Showing {0} of {1} deprecated endpoint rows. Use -Detailed for all rows or -AsJson for structured output." -f $EndpointDetailLimit, $deprecatedEndpointRows.Count) -ForegroundColor DarkYellow
        }
    }

    Write-Host ""
    Write-Host "Phase Route Families (observe only)" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow
    if ($phaseFamilies.Count -eq 0) {
        Write-Host "No phase-route telemetry rows were found." -ForegroundColor Yellow
    } else {
        $phaseFamilies |
            Select-Object Family, EndpointCount, Methods, TotalCalls, RecentCalls, ErrorCalls, UniqueCallersApprox, LastCallUtc |
            Format-Table -AutoSize
    }

    Write-Host ""
    if ($readyFamilies.Count -gt 0) {
        Write-Host "Ready for batch review:" -ForegroundColor Green
        $readyFamilies | ForEach-Object { Write-Host ("  - {0}" -f $_.Family) -ForegroundColor Green }
    } else {
        Write-Host "No deprecated route family is ready for removal yet." -ForegroundColor Yellow
    }

    if ($blockedFamilies.Count -gt 0) {
        Write-Host "Blocked or still collecting:" -ForegroundColor Yellow
        $blockedFamilies | ForEach-Object { Write-Host ("  - {0}: {1}" -f $_.Family, $_.Status) -ForegroundColor Yellow }
    }

    Write-Host ""
    Write-Host "Batch rule: one route family at a time; build, deploy, smoke, monitor, and retain rollback backup for every removal batch." -ForegroundColor Cyan
}
finally {
    Close-NscimConnection -Handle $handle
}
