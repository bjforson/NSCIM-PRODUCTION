# Targeted assignment/queue snapshot for a workbench "no assignment" report.
# Read-only. Uses the shared Postgres helper and tenant context.

[CmdletBinding()]
param(
    [string]$GroupIdentifier = "40426305424_W1",
    [string]$Username = "pimage",
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1"
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId

try {
    Write-Host "Assignment / queue snapshot" -ForegroundColor Cyan
    Write-Host "  GroupIdentifier: $GroupIdentifier"
    Write-Host "  Username       : $Username"
    Write-Host ""

    Write-Host "Active and recent assignment rows" -ForegroundColor Yellow
    Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.id,
    aa.assignedto,
    aa.role,
    aa.state,
    aa.leaseuntilutc,
    aa.createdatutc,
    aa.updatedatutc,
    aa.lastaccessedatutc,
    ag.id AS groupid,
    ag.groupidentifier,
    ag.status AS groupstatus,
    ag.scannertype
FROM analysisassignments aa
JOIN analysisgroups ag ON ag.id = aa.groupid
WHERE lower(aa.assignedto) = lower(@username)
   OR ag.groupidentifier = @groupidentifier
ORDER BY aa.id DESC
LIMIT 20
"@ -Parameters @{ username = $Username; groupidentifier = $GroupIdentifier } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Materialized queue entries" -ForegroundColor Yellow
    Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    assignmentid,
    assignedto,
    role,
    groupid,
    groupidentifier,
    groupstatus,
    scannertype,
    leaseuntilutc,
    queuedatutc,
    lastrefreshedatutc
FROM analysisqueueentries
WHERE lower(assignedto) = lower(@username)
   OR groupidentifier = @groupidentifier
ORDER BY assignmentid DESC
LIMIT 20
"@ -Parameters @{ username = $Username; groupidentifier = $GroupIdentifier } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Rows that /api/image-analysis/my-assignments should consider active" -ForegroundColor Yellow
    Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.id AS assignmentid,
    aa.assignedto,
    aa.role,
    aa.state,
    aa.leaseuntilutc,
    ag.groupidentifier,
    ag.status AS groupstatus,
    aqe.assignmentid IS NOT NULL AS hasqueueentry
FROM analysisassignments aa
JOIN analysisgroups ag ON ag.id = aa.groupid
LEFT JOIN analysisqueueentries aqe ON aqe.assignmentid = aa.id
WHERE lower(aa.assignedto) = lower(@username)
  AND aa.role = 'Analyst'
  AND aa.state = 'Active'
  AND (aa.leaseuntilutc IS NULL OR aa.leaseuntilutc > NOW() AT TIME ZONE 'UTC')
ORDER BY aa.id DESC
"@ -Parameters @{ username = $Username } |
        Format-Table -AutoSize
}
finally {
    Close-NscimConnection -Handle $h
}
