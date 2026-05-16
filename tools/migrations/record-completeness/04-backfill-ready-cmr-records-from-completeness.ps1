<#
.SYNOPSIS
    Materialize CMR record-completeness rows from existing complete container evidence.

.DESCRIPTION
    Repairs the durable failure mode where a CMR BOE row pre-dates the
    RecordReconciliationWorker watermark, while ContainerCompletenessStatuses
    already has scanner, ICUMS, and canonical scan image evidence.

    The script is idempotent and dry-run by default. Use -Apply to write:
      - ContainerCompletenessStatuses image/stage/group repair.
      - RecordCompletenessStatuses parent row for the CMR operational key.
      - RecordExpectedContainers child row with scan image identity.
      - SourceScanContainerLinks back-link to the expected container.

.EXAMPLE
    pwsh tools/migrations/record-completeness/04-backfill-ready-cmr-records-from-completeness.ps1 -ContainerNumber TEMU2527526,TIIU2732427

.EXAMPLE
    pwsh tools/migrations/record-completeness/04-backfill-ready-cmr-records-from-completeness.ps1 -Apply -Limit 500
#>
[CmdletBinding()]
param(
    [string[]]$ContainerNumber = @(),
    [int]$Limit = 500,
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$TenantId = "1",
    [switch]$Apply,
    [switch]$UseSuperuser
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
. (Join-Path $repoRoot "scripts\postgres\_NpgsqlHelper.ps1")

if (-not $env:NICKSCAN_DB_PASSWORD) {
    $env:NICKSCAN_DB_PASSWORD = [Environment]::GetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "Machine")
}
if ($UseSuperuser -and -not $env:NICKHR_DB_PASSWORD) {
    $env:NICKHR_DB_PASSWORD = [Environment]::GetEnvironmentVariable("NICKHR_DB_PASSWORD", "Machine")
}

function ConvertTo-DbValue {
    param([object]$Value)
    if ($null -eq $Value) { return [DBNull]::Value }
    return $Value
}

function ConvertTo-DbParameters {
    param([hashtable]$Parameters)
    $converted = @{}
    if ($Parameters) {
        foreach ($key in $Parameters.Keys) {
            $converted[$key] = ConvertTo-DbValue $Parameters[$key]
        }
    }
    return $converted
}

function Invoke-DbScalar {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Handle,
        [Parameter(Mandatory = $true)][string]$Sql,
        [hashtable]$Parameters
    )

    $cmd = $Handle.Connection.CreateCommand()
    $cmd.Transaction = $Handle.Transaction
    $cmd.CommandText = $Sql
    if ($Parameters) {
        foreach ($key in $Parameters.Keys) {
            $null = $cmd.Parameters.AddWithValue($key, (ConvertTo-DbValue $Parameters[$key]))
        }
    }
    $value = $cmd.ExecuteScalar()
    if ($value -is [DBNull]) { return $null }
    return $value
}

function Invoke-DbNonQuery {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Handle,
        [Parameter(Mandatory = $true)][string]$Sql,
        [hashtable]$Parameters
    )

    Invoke-NscimNonQuery -Handle $Handle -Sql $Sql -Parameters (ConvertTo-DbParameters $Parameters)
}

function Normalize-KeyPart {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return (($Value.Trim().ToUpperInvariant() -split " ") | Where-Object { $_ }) -join " "
}

function Normalize-ContainerNumber {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return [regex]::Replace($Value.Trim().ToUpperInvariant(), "[^A-Z0-9]", "")
}

function New-CmrOperationalKey {
    param(
        [string]$RotationNumber,
        [string]$ContainerNumber,
        [string]$BlNumber
    )

    $rotation = Normalize-KeyPart $RotationNumber
    $container = Normalize-KeyPart $ContainerNumber
    $bl = Normalize-KeyPart $BlNumber
    if (-not $rotation -or -not $container -or -not $bl) { return $null }

    $hashInput = "$rotation|$container|$bl"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($hashInput)
    $hashBytes = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $hash = [Convert]::ToHexString($hashBytes).Substring(0, 20)

    [pscustomobject]@{
        OperationalKey = "CMR-$hash"
        DisplayLabel = "CMR $container / $rotation / $bl"
        RotationNumber = $rotation
        ContainerNumber = $container
        BlNumber = $bl
    }
}

$normalizedFilters = @(
    $ContainerNumber |
        ForEach-Object { $_ -split "," } |
        ForEach-Object { Normalize-ContainerNumber $_ } |
        Where-Object { $_ } |
        Select-Object -Unique
)

$candidateParams = @{ Limit = $Limit }
$containerClause = ""
if ($normalizedFilters.Count -gt 0) {
    $names = @()
    for ($i = 0; $i -lt $normalizedFilters.Count; $i++) {
        $name = "container$i"
        $candidateParams[$name] = $normalizedFilters[$i]
        $names += "@$name"
    }
    $containerClause = "AND upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) IN ($($names -join ', '))"
}

$candidateSql = @"
SELECT DISTINCT ON (ccs.id)
    ccs.id AS ccsid,
    ccs.containernumber,
    ccs.scannertype,
    ccs.inspectionid,
    ccs.scanimageassetid,
    ccs.originalscanrecordid,
    ccs.sourcecontainerlabel,
    ccs.scandate,
    ccs.boedocumentid,
    ccs.groupidentifier,
    ccs.status,
    ccs.workflowstage,
    ccs.hasicumsdata,
    ccs.hasimagedata,
    ccs.hasscannerdata,
    ccs.tenant_id
FROM containercompletenessstatuses ccs
JOIN scanimageassets asset
  ON asset.id = ccs.scanimageassetid
WHERE upper(coalesce(ccs.clearancetype, '')) = 'CMR'
  AND coalesce(ccs.hasicumsdata, false) = true
  AND coalesce(ccs.hasscannerdata, false) = true
  AND ccs.scanimageassetid IS NOT NULL
  AND ccs.boedocumentid IS NOT NULL
  AND (
    coalesce(ccs.hasimagedata, false) = false
    OR coalesce(ccs.status, '') <> 'Complete'
    OR coalesce(ccs.workflowstage, '') <> 'ImageAnalysis'
    OR nullif(trim(coalesce(ccs.groupidentifier, '')), '') IS NULL
    OR NOT EXISTS (
      SELECT 1
      FROM sourcescancontainerlinks scl
      WHERE scl.scanimageassetid = ccs.scanimageassetid
        AND scl.normalizedcontainernumber = upper(regexp_replace(coalesce(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g'))
        AND scl.recordexpectedcontainerid IS NOT NULL
    )
  )
  $containerClause
ORDER BY ccs.id, ccs.updatedat DESC
LIMIT @Limit;
"@

$app = $null
$downloads = $null
$createdRecords = 0
$updatedRecords = 0
$createdChildren = 0
$updatedChildren = 0
$updatedCompleteness = 0
$linkedSourceRows = 0
$skipped = 0

try {
    $openArgs = @{
        PgHost = $PgHost
        Port = $Port
        TenantId = $TenantId
        UseSuperuser = $UseSuperuser
    }

    $app = Open-NscimConnection @openArgs -Database "nickscan_production"
    $downloads = Open-NscimConnection @openArgs -Database "nickscan_downloads"

    $candidates = Invoke-NscimQuery -Handle $app -Sql $candidateSql -Parameters $candidateParams
    $mode = if ($Apply) { "APPLY" } else { "DRY-RUN" }
    Write-Host "[$mode] Candidate CMR completeness rows: $($candidates.Count)"

    foreach ($candidate in $candidates) {
        $boe = Invoke-NscimQuery -Handle $downloads -Sql @"
SELECT
    id,
    containernumber,
    clearancetype,
    regimecode,
    rotationnumber,
    blnumber,
    housebl,
    consigneename
FROM boedocuments
WHERE id = @BoeId
LIMIT 1;
"@ -Parameters @{ BoeId = [int]$candidate.boedocumentid }

        if ($boe.Count -eq 0) {
            Write-Warning "Skipping CCS $($candidate.ccsid) / $($candidate.containernumber): BOE $($candidate.boedocumentid) not found"
            $skipped++
            continue
        }

        $boeRow = $boe[0]
        if (($boeRow.clearancetype ?? "").Trim().ToUpperInvariant() -ne "CMR") {
            Write-Warning "Skipping CCS $($candidate.ccsid) / $($candidate.containernumber): BOE $($candidate.boedocumentid) clearance is '$($boeRow.clearancetype)'"
            $skipped++
            continue
        }

        $key = New-CmrOperationalKey -RotationNumber $boeRow.rotationnumber -ContainerNumber $boeRow.containernumber -BlNumber $boeRow.blnumber
        if ($null -eq $key) {
            Write-Warning "Skipping CCS $($candidate.ccsid) / $($candidate.containernumber): BOE $($candidate.boedocumentid) lacks CMR key parts"
            $skipped++
            continue
        }

        $normalizedContainer = Normalize-ContainerNumber $boeRow.containernumber
        $now = [DateTime]::UtcNow

        Write-Host "[$mode] $($candidate.containernumber) -> $($key.OperationalKey) asset=$($candidate.scanimageassetid) boe=$($candidate.boedocumentid)"

        if (-not $Apply) {
            continue
        }

        $recordId = Invoke-DbScalar -Handle $app -Sql @"
SELECT id
FROM recordcompletenessstatuses
WHERE declarationnumber = @OperationalKey
LIMIT 1;
"@ -Parameters @{ OperationalKey = $key.OperationalKey }

        if ($null -eq $recordId) {
            $recordId = Invoke-DbScalar -Handle $app -Sql @"
INSERT INTO recordcompletenessstatuses (
    declarationnumber,
    clearancetype,
    regimecode,
    primaryboedocumentid,
    rotationnumber,
    blnumber,
    containergroupkey,
    scannertype,
    totalexpectedcontainers,
    containersawaitingscan,
    containersscanned,
    containersready,
    containersdecided,
    containerssubmitted,
    containersnoimage,
    containersnoscan,
    status,
    workflowstage,
    firstseenutc,
    lastnewcontaineratutc,
    firstreadyatutc,
    lastcheckedatutc,
    createdatutc,
    updatedatutc,
    tenant_id
)
VALUES (
    @OperationalKey,
    'CMR',
    @RegimeCode,
    @BoeId,
    @RotationNumber,
    @BlNumber,
    NULL,
    NULL,
    1,
    0,
    0,
    1,
    0,
    0,
    0,
    0,
    'Ready',
    'ImageAnalysis',
    @NowUtc,
    @NowUtc,
    @NowUtc,
    @NowUtc,
    @NowUtc,
    @NowUtc,
    @TenantId
)
RETURNING id;
"@ -Parameters @{
                OperationalKey = $key.OperationalKey
                RegimeCode = $boeRow.regimecode
                BoeId = [int]$boeRow.id
                RotationNumber = $key.RotationNumber
                BlNumber = $key.BlNumber
                NowUtc = $now
                TenantId = [int64]$TenantId
            }
            $createdRecords++
        } else {
            $null = Invoke-DbNonQuery -Handle $app -Sql @"
UPDATE recordcompletenessstatuses
SET
    clearancetype = 'CMR',
    regimecode = @RegimeCode,
    primaryboedocumentid = COALESCE(primaryboedocumentid, @BoeId),
    rotationnumber = @RotationNumber,
    blnumber = @BlNumber,
    updatedatutc = @NowUtc,
    lastcheckedatutc = @NowUtc
WHERE id = @RecordId;
"@ -Parameters @{
                RecordId = [int]$recordId
                RegimeCode = $boeRow.regimecode
                BoeId = [int]$boeRow.id
                RotationNumber = $key.RotationNumber
                BlNumber = $key.BlNumber
                NowUtc = $now
            }
            $updatedRecords++
        }

        $childId = Invoke-DbScalar -Handle $app -Sql @"
SELECT id
FROM recordexpectedcontainers
WHERE recordid = @RecordId
  AND upper(regexp_replace(coalesce(containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = @NormalizedContainer
LIMIT 1;
"@ -Parameters @{
            RecordId = [int]$recordId
            NormalizedContainer = $normalizedContainer
        }

        if ($null -eq $childId) {
            $childId = Invoke-DbScalar -Handle $app -Sql @"
INSERT INTO recordexpectedcontainers (
    recordid,
    containernumber,
    status,
    boedocumentid,
    housebl,
    consigneename,
    inspectionid,
    scannertype,
    scanimageassetid,
    originalscanrecordid,
    sourcecontainerlabel,
    firstseenutc,
    scannedatutc,
    becamereadyutc,
    tenant_id
)
VALUES (
    @RecordId,
    @ContainerNumber,
    'Ready',
    @BoeId,
    @HouseBl,
    @ConsigneeName,
    @InspectionId,
    @ScannerType,
    @ScanImageAssetId,
    @OriginalScanRecordId,
    @SourceContainerLabel,
    @NowUtc,
    @ScanDate,
    @NowUtc,
    @TenantId
)
RETURNING id;
"@ -Parameters @{
                RecordId = [int]$recordId
                ContainerNumber = $key.ContainerNumber
                BoeId = [int]$boeRow.id
                HouseBl = $boeRow.housebl
                ConsigneeName = $boeRow.consigneename
                InspectionId = $candidate.inspectionid
                ScannerType = $candidate.scannertype
                ScanImageAssetId = [Guid]$candidate.scanimageassetid
                OriginalScanRecordId = $candidate.originalscanrecordid
                SourceContainerLabel = $candidate.sourcecontainerlabel
                NowUtc = $now
                ScanDate = $candidate.scandate
                TenantId = [int64]$TenantId
            }
            $createdChildren++
        } else {
            $null = Invoke-DbNonQuery -Handle $app -Sql @"
UPDATE recordexpectedcontainers
SET
    status = CASE WHEN status IN ('Decided', 'Submitted') THEN status ELSE 'Ready' END,
    boedocumentid = COALESCE(boedocumentid, @BoeId),
    housebl = COALESCE(NULLIF(housebl, ''), @HouseBl),
    consigneename = COALESCE(NULLIF(consigneename, ''), @ConsigneeName),
    inspectionid = COALESCE(NULLIF(inspectionid, ''), @InspectionId),
    scannertype = COALESCE(NULLIF(scannertype, ''), @ScannerType),
    scanimageassetid = COALESCE(scanimageassetid, @ScanImageAssetId),
    originalscanrecordid = COALESCE(originalscanrecordid, @OriginalScanRecordId),
    sourcecontainerlabel = COALESCE(NULLIF(sourcecontainerlabel, ''), @SourceContainerLabel),
    scannedatutc = COALESCE(scannedatutc, @ScanDate),
    becamereadyutc = CASE
        WHEN status IN ('Decided', 'Submitted') THEN becamereadyutc
        ELSE COALESCE(becamereadyutc, @NowUtc)
    END
WHERE id = @ChildId;
"@ -Parameters @{
                ChildId = [int]$childId
                BoeId = [int]$boeRow.id
                HouseBl = $boeRow.housebl
                ConsigneeName = $boeRow.consigneename
                InspectionId = $candidate.inspectionid
                ScannerType = $candidate.scannertype
                ScanImageAssetId = [Guid]$candidate.scanimageassetid
                OriginalScanRecordId = $candidate.originalscanrecordid
                SourceContainerLabel = $candidate.sourcecontainerlabel
                ScanDate = $candidate.scandate
                NowUtc = $now
            }
            $updatedChildren++
        }

        $updatedCompleteness += Invoke-DbNonQuery -Handle $app -Sql @"
UPDATE containercompletenessstatuses
SET
    hasimagedata = true,
    imagedatacompleteness = 100,
    scannerdatacompleteness = CASE WHEN coalesce(scannerdatacompleteness, 0) < 100 THEN 100 ELSE scannerdatacompleteness END,
    icumsdatacompleteness = CASE WHEN coalesce(icumsdatacompleteness, 0) < 100 THEN 100 ELSE icumsdatacompleteness END,
    overallcompleteness = (
        (CASE WHEN coalesce(hasscannerdata, false) THEN 100 ELSE coalesce(scannerdatacompleteness, 0) END)
        + (CASE WHEN coalesce(hasicumsdata, false) THEN 100 ELSE coalesce(icumsdatacompleteness, 0) END)
        + 100
    ) / 3,
    status = CASE
        WHEN coalesce(hasscannerdata, false) AND coalesce(hasicumsdata, false) THEN 'Complete'
        ELSE status
    END,
    workflowstage = CASE
        WHEN coalesce(hasscannerdata, false) AND coalesce(hasicumsdata, false) THEN 'ImageAnalysis'
        ELSE workflowstage
    END,
    groupidentifier = @OperationalKey,
    consolidationdetails = @DisplayLabel,
    updatedat = @NowUtc,
    lastcheckedat = @NowUtc
WHERE id = @CcsId;
"@ -Parameters @{
            CcsId = [int]$candidate.ccsid
            OperationalKey = $key.OperationalKey
            DisplayLabel = $key.DisplayLabel
            NowUtc = $now
        }

        $linkedSourceRows += Invoke-DbNonQuery -Handle $app -Sql @"
UPDATE sourcescancontainerlinks
SET
    recordexpectedcontainerid = @ChildId,
    boedocumentid = COALESCE(boedocumentid, @BoeId),
    updatedatutc = @NowUtc
WHERE scanimageassetid = @ScanImageAssetId
  AND normalizedcontainernumber = @NormalizedContainer;
"@ -Parameters @{
            ChildId = [int]$childId
            BoeId = [int]$boeRow.id
            ScanImageAssetId = [Guid]$candidate.scanimageassetid
            NormalizedContainer = $normalizedContainer
            NowUtc = $now
        }

        $null = Invoke-DbNonQuery -Handle $app -Sql @"
WITH counts AS (
    SELECT
        count(*)::int AS total,
        count(*) FILTER (WHERE status = 'AwaitingScan')::int AS awaiting,
        count(*) FILTER (WHERE status = 'Pending')::int AS scanned,
        count(*) FILTER (WHERE status = 'Ready')::int AS ready,
        count(*) FILTER (WHERE status = 'Decided')::int AS decided,
        count(*) FILTER (WHERE status = 'Submitted')::int AS submitted,
        count(*) FILTER (WHERE status = 'NoImageAvailable')::int AS noimage,
        count(*) FILTER (WHERE status = 'NoScanReceived')::int AS noscan
    FROM recordexpectedcontainers
    WHERE recordid = @RecordId
)
UPDATE recordcompletenessstatuses r
SET
    totalexpectedcontainers = counts.total,
    containersawaitingscan = counts.awaiting,
    containersscanned = counts.scanned,
    containersready = counts.ready,
    containersdecided = counts.decided,
    containerssubmitted = counts.submitted,
    containersnoimage = counts.noimage,
    containersnoscan = counts.noscan,
    status = CASE
        WHEN r.status IN ('Archived', 'Failed') THEN r.status
        WHEN counts.submitted = counts.total AND counts.total > 0 THEN 'Completed'
        WHEN counts.submitted > 0 AND counts.submitted + counts.decided + counts.noimage + counts.noscan = counts.total THEN 'PendingSubmission'
        WHEN counts.decided > 0 AND counts.decided + counts.submitted = counts.total - counts.noimage - counts.noscan - counts.awaiting THEN 'InAudit'
        WHEN counts.ready > 0 AND counts.awaiting = 0 AND counts.scanned = 0 THEN 'Ready'
        WHEN counts.ready > 0 THEN 'PartiallyReady'
        WHEN counts.scanned > 0 THEN 'PartiallyReady'
        ELSE 'Pending'
    END,
    workflowstage = CASE
        WHEN r.status IN ('Archived', 'Failed') THEN r.workflowstage
        WHEN counts.submitted = counts.total AND counts.total > 0 THEN 'Completed'
        WHEN counts.submitted > 0 AND counts.submitted + counts.decided + counts.noimage + counts.noscan = counts.total THEN 'PendingSubmission'
        WHEN counts.decided > 0 AND counts.decided + counts.submitted = counts.total - counts.noimage - counts.noscan - counts.awaiting THEN 'Audit'
        WHEN counts.ready > 0 THEN 'ImageAnalysis'
        ELSE 'Pending'
    END,
    firstreadyatutc = CASE
        WHEN counts.ready > 0 AND r.firstreadyatutc IS NULL THEN @NowUtc
        ELSE r.firstreadyatutc
    END,
    updatedatutc = @NowUtc,
    lastcheckedatutc = @NowUtc
FROM counts
WHERE r.id = @RecordId;
"@ -Parameters @{
            RecordId = [int]$recordId
            NowUtc = $now
        }
    }

    if ($Apply) {
        Close-NscimConnection -Handle $downloads
        Close-NscimConnection -Handle $app
    } else {
        Close-NscimConnection -Handle $downloads
        Close-NscimConnection -Handle $app
    }

    Write-Host "[$mode] Summary: createdRecords=$createdRecords updatedRecords=$updatedRecords createdChildren=$createdChildren updatedChildren=$updatedChildren updatedCompleteness=$updatedCompleteness linkedSourceRows=$linkedSourceRows skipped=$skipped"
}
catch {
    if ($downloads -and $downloads.Transaction.Connection) {
        try { $downloads.Transaction.Rollback() } catch { }
        try { $downloads.Connection.Dispose() } catch { }
    }
    if ($app -and $app.Transaction.Connection) {
        try { $app.Transaction.Rollback() } catch { }
        try { $app.Connection.Dispose() } catch { }
    }
    throw
}
