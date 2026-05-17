param(
    [switch]$Apply,
    [string]$TenantId = "1",
    [string]$RepairRun = ("dual-container-identity-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ==="
}

function Show-Query {
    param(
        [hashtable]$Handle,
        [string]$Title,
        [string]$Sql
    )

    Write-Section $Title
    Invoke-NscimQuery -Handle $Handle -Sql $Sql -Parameters @{ tenant_id = [long]$TenantId } |
        Format-Table -AutoSize |
        Out-String -Width 240 |
        Write-Host
}

function Invoke-ScalarQuery {
    param(
        [hashtable]$Handle,
        [string]$Title,
        [string]$Sql
    )

    Write-Section $Title
    Invoke-NscimQuery -Handle $Handle -Sql $Sql -Parameters @{
        tenant_id = [long]$TenantId
        repair_run = $RepairRun
    } |
        Format-Table -AutoSize |
        Out-String -Width 240 |
        Write-Host
}

$previewSql = @"
select 'containerscanqueues' as table_name, count(*)::int as polluted
from containerscanqueues
where tenant_id = @tenant_id and containernumber ~ '[,;]'
union all
select 'containercompletenessstatuses', count(*)::int
from containercompletenessstatuses
where tenant_id = @tenant_id and containernumber ~ '[,;]'
union all
select 'analysisgroups', count(*)::int
from analysisgroups
where tenant_id = @tenant_id and groupidentifier ~ '[,;]'
union all
select 'analysisrecords', count(*)::int
from analysisrecords
where tenant_id = @tenant_id and containernumber ~ '[,;]'
union all
select 'imageanalysisdecisions', count(*)::int
from imageanalysisdecisions
where tenant_id = @tenant_id and containernumber ~ '[,;]'
union all
select 'analysisqueueentries', count(*)::int
from analysisqueueentries
where tenant_id = @tenant_id and containersjson ~ '[,;]'
order by table_name;
"@

$queueCoverageSql = @"
with polluted as (
    select *
    from containerscanqueues
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
),
tokens as (
    select
        p.id,
        p.scannertype,
        p.inspectionid,
        token_data.token,
        case
            when nullif(p.inspectionid, '') is null then null
            when token_data.ord <= 26 then p.inspectionid || '-' || chr(96 + token_data.ord::int)
            else p.inspectionid || '-' || token_data.ord::text
        end as child_inspectionid
    from polluted p
    cross join lateral regexp_split_to_table(p.containernumber, '\s*[,;]\s*') with ordinality as token_data(token, ord)
    where token_data.token ~ '^[A-Z]{4}[0-9]{7}$'
),
coverage as (
    select
        t.id,
        count(*)::int as token_count,
        count(child.id)::int as child_matches
    from tokens t
    left join containerscanqueues child
        on child.tenant_id = @tenant_id
       and child.scannertype = t.scannertype
       and child.containernumber = t.token
       and coalesce(child.inspectionid, '') = coalesce(t.child_inspectionid, '')
    group by t.id
)
select
    count(*)::int as polluted_rows,
    coalesce(sum(token_count), 0)::int as total_tokens,
    coalesce(sum(child_matches), 0)::int as child_matches,
    count(*) filter (where child_matches >= token_count)::int as fully_covered,
    count(*) filter (where child_matches = 0)::int as no_children
from coverage;
"@

$completenessCoverageSql = @"
with polluted as (
    select *
    from containercompletenessstatuses
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
),
tokens as (
    select
        p.id,
        p.scannertype,
        p.inspectionid,
        token_data.token,
        case
            when nullif(p.inspectionid, '') is null then null
            when token_data.ord <= 26 then p.inspectionid || '-' || chr(96 + token_data.ord::int)
            else p.inspectionid || '-' || token_data.ord::text
        end as child_inspectionid
    from polluted p
    cross join lateral regexp_split_to_table(p.containernumber, '\s*[,;]\s*') with ordinality as token_data(token, ord)
    where token_data.token ~ '^[A-Z]{4}[0-9]{7}$'
),
coverage as (
    select
        t.id,
        count(*)::int as token_count,
        count(child.id)::int as child_matches
    from tokens t
    left join containercompletenessstatuses child
        on child.tenant_id = @tenant_id
       and child.scannertype = t.scannertype
       and child.containernumber = t.token
       and coalesce(child.inspectionid, '') = coalesce(t.child_inspectionid, '')
    group by t.id
)
select
    count(*)::int as polluted_rows,
    coalesce(sum(token_count), 0)::int as total_tokens,
    coalesce(sum(child_matches), 0)::int as child_matches,
    count(*) filter (where child_matches >= token_count)::int as fully_covered,
    count(*) filter (where child_matches = 0)::int as no_children
from coverage;
"@

$schemaSql = @"
create schema if not exists maintenance;

create table if not exists maintenance.dual_container_identity_repair_audit (
    id bigserial primary key,
    repair_run text not null,
    step text not null,
    table_name text not null,
    row_pk text not null,
    action text not null,
    before_data jsonb null,
    after_data jsonb null,
    created_at timestamptz not null default now()
);
"@

$auditQueueSql = @"
insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-polluted-queue', 'containerscanqueues', id::text, 'backup-delete', to_jsonb(q)
from containerscanqueues q
where tenant_id = @tenant_id and containernumber ~ '[,;]';
"@

$insertQueueChildrenSql = @"
with polluted as (
    select *
    from containerscanqueues
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
),
tokens as (
    select
        p.*,
        token_data.token,
        token_data.ord,
        count(*) over (partition by p.id) as token_count,
        case
            when nullif(p.inspectionid, '') is null then null
            when token_data.ord <= 26 then p.inspectionid || '-' || chr(96 + token_data.ord::int)
            else p.inspectionid || '-' || token_data.ord::text
        end as child_inspectionid
    from polluted p
    cross join lateral regexp_split_to_table(p.containernumber, '\s*[,;]\s*') with ordinality as token_data(token, ord)
    where token_data.token ~ '^[A-Z]{4}[0-9]{7}$'
),
inserted as (
    insert into containerscanqueues (
        containernumber, scannertype, inspectionid, scandate, status, priority, retrycount, maxretries,
        queuedat, processedat, completedat, errormessage, metadata, createdat, updatedat, tenant_id,
        scanimageassetid, originalscanrecordid, sourcecontainerlabel, scancontainerposition, splitjobid, splitresultid
    )
    select
        t.token,
        t.scannertype,
        t.child_inspectionid,
        t.scandate,
        t.status,
        t.priority,
        t.retrycount,
        t.maxretries,
        t.queuedat,
        t.processedat,
        t.completedat,
        t.errormessage,
        case
            when nullif(t.metadata, '') is null then jsonb_build_object(
                'OriginalContainerNumber', t.containernumber,
                'MultiContainerScan', true,
                'SplitTokenIndex', t.ord - 1,
                'SplitTokenCount', t.token_count,
                'RepairRun', @repair_run)::text
            when t.metadata ~ '^\s*\{' then (t.metadata::jsonb || jsonb_build_object(
                'OriginalContainerNumber', t.containernumber,
                'MultiContainerScan', true,
                'SplitTokenIndex', t.ord - 1,
                'SplitTokenCount', t.token_count,
                'RepairRun', @repair_run))::text
            else t.metadata
        end,
        t.createdat,
        now(),
        t.tenant_id,
        t.scanimageassetid,
        t.originalscanrecordid,
        coalesce(nullif(t.sourcecontainerlabel, ''), t.containernumber),
        t.scancontainerposition,
        t.splitjobid,
        t.splitresultid
    from tokens t
    where not exists (
        select 1
        from containerscanqueues child
        where child.tenant_id = t.tenant_id
          and child.scannertype = t.scannertype
          and child.containernumber = t.token
          and coalesce(child.inspectionid, '') = coalesce(t.child_inspectionid, '')
    )
    returning id
)
select count(*)::int as inserted_queue_children from inserted;
"@

$deleteQueueSql = @"
with deleted as (
    delete from containerscanqueues
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
    returning id
)
select count(*)::int as deleted_polluted_queue_rows from deleted;
"@

$auditCompletenessSql = @"
insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-polluted-completeness', 'containercompletenessstatuses', id::text, 'backup-delete', to_jsonb(c)
from containercompletenessstatuses c
where tenant_id = @tenant_id and containernumber ~ '[,;]';
"@

$insertCompletenessChildrenSql = @"
with polluted as (
    select *
    from containercompletenessstatuses
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
),
tokens as (
    select
        p.*,
        token_data.token,
        token_data.ord,
        case
            when nullif(p.inspectionid, '') is null then null
            when token_data.ord <= 26 then p.inspectionid || '-' || chr(96 + token_data.ord::int)
            else p.inspectionid || '-' || token_data.ord::text
        end as child_inspectionid
    from polluted p
    cross join lateral regexp_split_to_table(p.containernumber, '\s*[,;]\s*') with ordinality as token_data(token, ord)
    where token_data.token ~ '^[A-Z]{4}[0-9]{7}$'
),
inserted as (
    insert into containercompletenessstatuses (
        containernumber, scannertype, inspectionid, scandate, hasicumsdata, icumsdatadate, boedocumentid,
        clearancetype, status, scannerdatacompleteness, icumsdatacompleteness, imagedatacompleteness,
        overallcompleteness, hasscannerdata, hasimagedata, isconsolidated, totalhousebls, completehousebls,
        consolidationdetails, groupidentifier, createdat, updatedat, errormessage, retrycount, lastcheckedat,
        workflowstage, tenant_id, scanimageassetid, originalscanrecordid, sourcecontainerlabel
    )
    select
        t.token,
        t.scannertype,
        t.child_inspectionid,
        t.scandate,
        t.hasicumsdata,
        t.icumsdatadate,
        t.boedocumentid,
        t.clearancetype,
        t.status,
        t.scannerdatacompleteness,
        t.icumsdatacompleteness,
        t.imagedatacompleteness,
        t.overallcompleteness,
        t.hasscannerdata,
        t.hasimagedata,
        t.isconsolidated,
        t.totalhousebls,
        t.completehousebls,
        t.consolidationdetails,
        case when t.groupidentifier ~ '[,;]' then null else t.groupidentifier end,
        t.createdat,
        now(),
        t.errormessage,
        t.retrycount,
        t.lastcheckedat,
        t.workflowstage,
        t.tenant_id,
        t.scanimageassetid,
        t.originalscanrecordid,
        coalesce(nullif(t.sourcecontainerlabel, ''), t.containernumber)
    from tokens t
    where not exists (
        select 1
        from containercompletenessstatuses child
        where child.tenant_id = t.tenant_id
          and child.scannertype = t.scannertype
          and child.containernumber = t.token
          and coalesce(child.inspectionid, '') = coalesce(t.child_inspectionid, '')
    )
    returning id
)
select count(*)::int as inserted_completeness_children from inserted;
"@

$auditAnalysisGroupsForCcsSql = @"
insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-analysisgroup-ccs-repoint', 'analysisgroups', ag.id::text, 'backup-update', to_jsonb(ag)
from analysisgroups ag
join containercompletenessstatuses c on c.id = ag.recordcompletenessstatusid
where ag.tenant_id = @tenant_id
  and c.tenant_id = @tenant_id
  and c.containernumber ~ '[,;]';
"@

$repointAnalysisGroupsSql = @"
with polluted_refs as (
    select
        ag.id as group_id,
        c.id as polluted_ccs_id,
        c.scannertype,
        c.inspectionid,
        c.tenant_id,
        token_data.token,
        token_data.ord,
        case
            when nullif(c.inspectionid, '') is null then null
            when token_data.ord <= 26 then c.inspectionid || '-' || chr(96 + token_data.ord::int)
            else c.inspectionid || '-' || token_data.ord::text
        end as child_inspectionid
    from analysisgroups ag
    join containercompletenessstatuses c on c.id = ag.recordcompletenessstatusid
    cross join lateral regexp_split_to_table(c.containernumber, '\s*[,;]\s*') with ordinality as token_data(token, ord)
    where ag.tenant_id = @tenant_id
      and c.tenant_id = @tenant_id
      and c.containernumber ~ '[,;]'
      and token_data.ord = 1
      and token_data.token ~ '^[A-Z]{4}[0-9]{7}$'
),
child_pick as (
    select distinct on (pr.group_id)
        pr.group_id,
        child.id as child_ccs_id
    from polluted_refs pr
    join containercompletenessstatuses child
      on child.tenant_id = pr.tenant_id
     and child.scannertype = pr.scannertype
     and child.containernumber = pr.token
     and coalesce(child.inspectionid, '') = coalesce(pr.child_inspectionid, '')
    order by pr.group_id, child.id
),
updated as (
    update analysisgroups ag
       set recordcompletenessstatusid = cp.child_ccs_id,
           updatedatutc = now()
    from child_pick cp
    where ag.id = cp.group_id
      and ag.recordcompletenessstatusid is distinct from cp.child_ccs_id
    returning ag.id
)
select count(*)::int as repointed_analysisgroups from updated;
"@

$deleteCompletenessSql = @"
with deleted as (
    delete from containercompletenessstatuses c
    where c.tenant_id = @tenant_id
      and c.containernumber ~ '[,;]'
      and not exists (
          select 1
          from analysisgroups ag
          where ag.tenant_id = @tenant_id
            and ag.recordcompletenessstatusid = c.id
      )
    returning id
)
select count(*)::int as deleted_polluted_completeness_rows from deleted;
"@

$auditAnalysisSql = @"
insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-analysisrecords', 'analysisrecords', id::text, 'backup-delete', to_jsonb(r)
from analysisrecords r
where tenant_id = @tenant_id and containernumber ~ '[,;]';

insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-decisions', 'imageanalysisdecisions', id::text, 'backup-update-or-delete', to_jsonb(d)
from imageanalysisdecisions d
where tenant_id = @tenant_id and containernumber ~ '[,;]';

insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-manifestsnapshots', 'manifestsnapshots', ms.id::text, 'backup-update', to_jsonb(ms)
from manifestsnapshots ms
join imageanalysisdecisions d on d.id = ms.imageanalysisdecisionid
where d.tenant_id = @tenant_id and d.containernumber ~ '[,;]';

insert into maintenance.dual_container_identity_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-comma-analysisgroups', 'analysisgroups', id::text, 'backup-update', to_jsonb(ag)
from analysisgroups ag
where tenant_id = @tenant_id and groupidentifier ~ '[,;]';
"@

$reassignManifestSnapshotsSql = @"
with bad_decisions as (
    select
        d.id as bad_decision_id,
        d.groupidentifier,
        d.scannertype,
        d.tenant_id,
        ms.id as snapshot_id,
        nullif(ms.rawmanifestjson::jsonb #>> '{ContainerDetails,ContainerNumber}', '') as child_container
    from imageanalysisdecisions d
    join manifestsnapshots ms on ms.imageanalysisdecisionid = d.id
    where d.tenant_id = @tenant_id
      and d.containernumber ~ '[,;]'
),
existing_child as (
    select distinct on (bd.bad_decision_id, bd.snapshot_id)
        bd.bad_decision_id,
        bd.snapshot_id,
        bd.child_container,
        good.id as good_decision_id
    from bad_decisions bd
    join imageanalysisdecisions good
      on good.tenant_id = bd.tenant_id
     and good.scannertype = bd.scannertype
     and good.groupidentifier = bd.groupidentifier
     and good.containernumber = bd.child_container
     and good.containernumber !~ '[,;]'
     and good.id <> bd.bad_decision_id
    where bd.child_container ~ '^[A-Z]{4}[0-9]{7}$'
    order by bd.bad_decision_id, bd.snapshot_id, good.id desc
),
updated as (
    update manifestsnapshots ms
       set imageanalysisdecisionid = ec.good_decision_id,
           containernumber = ec.child_container
    from existing_child ec
    where ms.id = ec.snapshot_id
    returning ms.id
)
select count(*)::int as reassigned_manifest_snapshots from updated;
"@

$deleteDuplicateDecisionsSql = @"
with deleted as (
    delete from imageanalysisdecisions d
    where d.tenant_id = @tenant_id
      and d.containernumber ~ '[,;]'
      and not exists (select 1 from auditdecisions ad where ad.imageanalysisdecisionid = d.id)
      and not exists (select 1 from manifestsnapshots ms where ms.imageanalysisdecisionid = d.id)
    returning id
)
select count(*)::int as deleted_duplicate_decisions from deleted;
"@

$repairRemainingDecisionsSql = @"
with remaining as (
    select
        d.id as decision_id,
        d.groupidentifier,
        d.scannertype,
        d.tenant_id,
        ms.id as snapshot_id,
        nullif(ms.rawmanifestjson::jsonb #>> '{ContainerDetails,ContainerNumber}', '') as child_container
    from imageanalysisdecisions d
    join manifestsnapshots ms on ms.imageanalysisdecisionid = d.id
    where d.tenant_id = @tenant_id
      and d.containernumber ~ '[,;]'
),
record_match as (
    select distinct on (r.decision_id)
        r.decision_id,
        r.snapshot_id,
        r.child_container,
        ar.id as analysisrecord_id,
        ar.splitjobid,
        case
            when lower(coalesce(ar.splitposition, '')) = 'left' then ar.splitoptiona_resultid
            when lower(coalesce(ar.splitposition, '')) = 'right' then ar.splitoptionb_resultid
            else coalesce(ar.splitresultid, ar.splitoptiona_resultid, ar.splitoptionb_resultid)
        end as chosen_splitresultid
    from remaining r
    join analysisgroups ag
      on ag.tenant_id = r.tenant_id
     and ag.scannertype = r.scannertype
     and ag.groupidentifier = r.groupidentifier
    join analysisrecords ar
      on ar.tenant_id = r.tenant_id
     and ar.groupid = ag.id
     and ar.containernumber = r.child_container
    where r.child_container ~ '^[A-Z]{4}[0-9]{7}$'
    order by r.decision_id, ar.id desc
),
updated_decisions as (
    update imageanalysisdecisions d
       set containernumber = rm.child_container,
           splitjobid = coalesce(d.splitjobid, rm.splitjobid),
           splitresultid = coalesce(d.splitresultid, rm.chosen_splitresultid),
           splitchoicestrategy = coalesce(d.splitchoicestrategy, 'repair-manifest-child'),
           updatedat = now()
    from record_match rm
    where d.id = rm.decision_id
    returning d.id
),
updated_snapshots as (
    update manifestsnapshots ms
       set containernumber = rm.child_container
    from record_match rm
    where ms.id = rm.snapshot_id
    returning ms.id
),
updated_records as (
    update analysisrecords ar
       set status = 'Decided'
    from record_match rm
    where ar.id = rm.analysisrecord_id
      and ar.status <> 'Decided'
    returning ar.id
)
select
    (select count(*)::int from updated_decisions) as updated_remaining_decisions,
    (select count(*)::int from updated_snapshots) as updated_remaining_snapshots,
    (select count(*)::int from updated_records) as updated_child_analysisrecords;
"@

$deleteCommaAnalysisRecordsSql = @"
with deleted as (
    delete from analysisrecords
    where tenant_id = @tenant_id and containernumber ~ '[,;]'
    returning id
)
select count(*)::int as deleted_comma_analysisrecords from deleted;
"@

$renameCancelledCommaGroupsSql = @"
with updated as (
    update analysisgroups ag
       set groupidentifier = 'SPLIT-SUP-' || ag.id::text,
           normalizedgroupidentifier = 'SPLITSUP' || replace(ag.id::text, '-', ''),
           updatedatutc = now()
    where ag.tenant_id = @tenant_id
      and ag.groupidentifier ~ '[,;]'
      and ag.status = 'Cancelled'
    returning ag.id
)
select count(*)::int as renamed_cancelled_comma_groups from updated;
"@

$deletePollutedQueueEntriesSql = @"
with deleted as (
    delete from analysisqueueentries aqe
    where aqe.tenant_id = @tenant_id
      and aqe.containersjson ~ '[,;]'
      and exists (
          select 1
          from analysisgroups ag
          where ag.id = aqe.groupid
            and ag.tenant_id = aqe.tenant_id
            and ag.status = 'Cancelled'
      )
    returning aqe.assignmentid
)
select count(*)::int as deleted_cancelled_polluted_queue_entries from deleted;
"@

$auditCountSql = @"
select repair_run, table_name, action, count(*)::int as rows
from maintenance.dual_container_identity_repair_audit
where repair_run = @repair_run
group by repair_run, table_name, action
order by table_name, action;
"@

$handle = $null
$success = $false

try {
    $handle = Open-NscimConnection -UseSuperuser -TenantId $TenantId

    Show-Query -Handle $handle -Title "Pollution counts before repair" -Sql $previewSql
    Show-Query -Handle $handle -Title "Queue child coverage before repair" -Sql $queueCoverageSql
    Show-Query -Handle $handle -Title "Completeness child coverage before repair" -Sql $completenessCoverageSql

    if (-not $Apply) {
        Write-Host ""
        Write-Host "Dry run only. Re-run with -Apply to create audit rows and repair production data."
        $success = $true
        return
    }

    Write-Section "Applying repair run $RepairRun"
    Invoke-NscimNonQuery -Handle $handle -Sql $schemaSql | Out-Null
    Invoke-NscimNonQuery -Handle $handle -Sql $auditQueueSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-ScalarQuery -Handle $handle -Title "Insert missing queue children" -Sql $insertQueueChildrenSql
    Invoke-ScalarQuery -Handle $handle -Title "Delete polluted queue rows" -Sql $deleteQueueSql

    Invoke-NscimNonQuery -Handle $handle -Sql $auditCompletenessSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-ScalarQuery -Handle $handle -Title "Insert missing completeness children" -Sql $insertCompletenessChildrenSql
    Invoke-NscimNonQuery -Handle $handle -Sql $auditAnalysisGroupsForCcsSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-ScalarQuery -Handle $handle -Title "Repoint analysis groups from polluted completeness rows" -Sql $repointAnalysisGroupsSql
    Invoke-ScalarQuery -Handle $handle -Title "Delete polluted completeness rows" -Sql $deleteCompletenessSql

    Invoke-NscimNonQuery -Handle $handle -Sql $auditAnalysisSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-ScalarQuery -Handle $handle -Title "Reassign manifest snapshots to existing child decisions" -Sql $reassignManifestSnapshotsSql
    Invoke-ScalarQuery -Handle $handle -Title "Delete duplicate comma decisions with no references" -Sql $deleteDuplicateDecisionsSql
    Invoke-ScalarQuery -Handle $handle -Title "Repair remaining comma decisions through manifest child" -Sql $repairRemainingDecisionsSql
    Invoke-ScalarQuery -Handle $handle -Title "Delete comma analysis records" -Sql $deleteCommaAnalysisRecordsSql
    Invoke-ScalarQuery -Handle $handle -Title "Rename cancelled comma analysis groups" -Sql $renameCancelledCommaGroupsSql
    Invoke-ScalarQuery -Handle $handle -Title "Delete cancelled polluted queue entries" -Sql $deletePollutedQueueEntriesSql

    Show-Query -Handle $handle -Title "Pollution counts after repair" -Sql $previewSql
    Invoke-ScalarQuery -Handle $handle -Title "Audit rows written" -Sql $auditCountSql

    $success = $true
}
finally {
    if ($handle) {
        if ($success) {
            Close-NscimConnection $handle
        } else {
            try { $handle.Transaction.Rollback() } catch { }
            try { $handle.Connection.Dispose() } catch { }
        }
    }
}
