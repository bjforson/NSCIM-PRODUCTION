<#
.SYNOPSIS
    Verify whether CMR containers have record completeness and image-analysis intake rows.

.EXAMPLE
    pwsh scripts/postgres/Verify-CmrImageReadiness.ps1 -ContainerNumber TEMU2527526,TIIU2732427
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$ContainerNumber,
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$TenantId = "1"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_NpgsqlHelper.ps1"

if (-not $env:NICKSCAN_DB_PASSWORD) {
    $env:NICKSCAN_DB_PASSWORD = [Environment]::GetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "Machine")
}

function Normalize-ContainerNumber {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return [regex]::Replace($Value.Trim().ToUpperInvariant(), "[^A-Z0-9]", "")
}

$normalized = @(
    $ContainerNumber |
        ForEach-Object { $_ -split "," } |
        ForEach-Object { Normalize-ContainerNumber $_ } |
        Where-Object { $_ } |
        Select-Object -Unique
)

if ($normalized.Count -eq 0) {
    throw "No valid container numbers supplied."
}

$params = @{}
$names = @()
for ($i = 0; $i -lt $normalized.Count; $i++) {
    $name = "container$i"
    $params[$name] = $normalized[$i]
    $names += "@$name"
}

$sql = @"
SELECT
    ccs.containernumber,
    ccs.scannertype,
    ccs.groupidentifier,
    ccs.status AS ccs_status,
    ccs.workflowstage AS ccs_stage,
    ccs.hasicumsdata,
    ccs.hasimagedata,
    ccs.scanimageassetid,
    rcs.id AS record_id,
    rcs.status AS record_status,
    rcs.workflowstage AS record_stage,
    rec.id AS child_id,
    rec.status AS child_status,
    rec.scanimageassetid AS child_asset,
    link.id AS source_link_id,
    link.recordexpectedcontainerid AS linked_child_id,
    ag.id AS analysis_group_id,
    ag.status AS analysis_group_status,
    ar.id AS analysis_record_id,
    ar.status AS analysis_record_status
FROM containercompletenessstatuses ccs
LEFT JOIN recordcompletenessstatuses rcs
  ON rcs.declarationnumber = ccs.groupidentifier
LEFT JOIN recordexpectedcontainers rec
  ON rec.recordid = rcs.id
 AND upper(regexp_replace(coalesce(rec.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g'))
LEFT JOIN sourcescancontainerlinks link
  ON link.scanimageassetid = ccs.scanimageassetid
 AND link.normalizedcontainernumber = upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g'))
LEFT JOIN analysisgroups ag
  ON ag.groupidentifier = ccs.groupidentifier
 AND upper(coalesce(ag.scannertype, '')) = upper(coalesce(ccs.scannertype, ''))
LEFT JOIN analysisrecords ar
  ON ar.groupid = ag.id
 AND upper(regexp_replace(coalesce(ar.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g'))
WHERE upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) IN ($($names -join ', '))
ORDER BY ccs.containernumber, ag.createdatutc DESC NULLS LAST, ar.createdatutc DESC NULLS LAST;
"@

$handle = Open-NscimConnection -PgHost $PgHost -Port $Port -TenantId $TenantId
try {
    $rows = Invoke-NscimQuery -Handle $handle -Sql $sql -Parameters $params
    Write-Output "Rows: $($rows.Count)"
    $rows | Format-List
} finally {
    Close-NscimConnection -Handle $handle
}
