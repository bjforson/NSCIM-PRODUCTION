param(
    [switch]$Apply,
    [switch]$IncludeTerminal,
    [string]$TenantId = "1",
    [string]$RepairRun = ("split-decision-audit-drift-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
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
    Invoke-NscimQuery -Handle $Handle -Sql $Sql -Parameters @{
        tenant_id = [long]$TenantId
        repair_run = $RepairRun
    } |
        Format-Table -AutoSize |
        Out-String -Width 260 |
        Write-Host
}

function Invoke-CountStep {
    param(
        [hashtable]$Handle,
        [string]$Title,
        [string]$Sql
    )

    Show-Query -Handle $Handle -Title $Title -Sql $Sql
}

$statusPredicate = if ($IncludeTerminal) {
    "ag.status not in ('Cancelled','Archived')"
} else {
    "ag.status not in ('Completed','Submitted','Cancelled','Archived')"
}

$commonTargetCte = @"
with unresolved_records as (
    select distinct
        ar.id as analysisrecordid,
        ar.groupid,
        ar.containernumber,
        coalesce(ar.scannertype, ag.scannertype, '') as scannertype,
        ar.splitjobid,
        ar.splitoptiona_resultid,
        ar.splitoptionb_resultid,
        ag.status as groupstatus
    from analysisrecords ar
    join analysisgroups ag on ag.id = ar.groupid and ag.tenant_id = ar.tenant_id
    join imageanalysisdecisions d
      on d.tenant_id = ar.tenant_id
     and d.containernumber = ar.containernumber
     and d.decision in ('Normal','Abnormal')
     and d.groupidentifier is not null
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
     and (
          d.scannertype = coalesce(ar.scannertype, ag.scannertype, '')
          or d.scannertype = split_part(coalesce(ar.scannertype, ag.scannertype, ''), '-', 1)
          or d.scannertype like split_part(coalesce(ar.scannertype, ag.scannertype, ''), '-', 1) || '-%'
     )
    where ar.tenant_id = @tenant_id
      and ar.ismulticontainerscan = true
      and not (
          (ar.splitstatus = 'Chosen' and ar.splitjobid is not null and ar.splitresultid is not null)
          or ar.splitstatus in ('Skipped','NotApplicable','VisualSingle','Uncertain')
      )
),
target_records as (
    select ur.*
    from unresolved_records ur
    join analysisgroups ag on ag.id = ur.groupid
    where $statusPredicate
),
target_groups as (
    select distinct groupid
    from target_records
),
target_decisions as (
    select distinct
        d.id,
        tr.analysisrecordid,
        tr.groupid,
        tr.groupstatus,
        d.splitjobid as decision_splitjobid,
        d.splitresultid as decision_splitresultid,
        tr.splitjobid as record_splitjobid,
        tr.splitoptiona_resultid,
        tr.splitoptionb_resultid,
        d.createdat,
        d.updatedat
    from target_records tr
    join analysisgroups ag on ag.id = tr.groupid
    join imageanalysisdecisions d
      on d.tenant_id = @tenant_id
     and d.containernumber = tr.containernumber
     and d.decision in ('Normal','Abnormal')
     and d.groupidentifier is not null
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
     and (
          d.scannertype = tr.scannertype
          or d.scannertype = split_part(tr.scannertype, '-', 1)
          or d.scannertype like split_part(tr.scannertype, '-', 1) || '-%'
     )
),
recoverable_decisions as (
    select *
    from target_decisions
    where decision_splitjobid is not null
      and decision_splitresultid is not null
      and decision_splitjobid = record_splitjobid
      and decision_splitresultid in (splitoptiona_resultid, splitoptionb_resultid)
),
recoverable_record_choice as (
    select distinct on (analysisrecordid)
        analysisrecordid,
        decision_splitjobid,
        decision_splitresultid
    from recoverable_decisions
    order by analysisrecordid, coalesce(updatedat, createdat) desc, id desc
),
invalid_decisions as (
    select td.*
    from target_decisions td
    where not exists (
        select 1
        from recoverable_decisions rd
        where rd.id = td.id
    )
),
invalid_reset_decisions as (
    select *
    from invalid_decisions
    where groupstatus not in ('Completed','Submitted')
),
invalid_reset_records as (
    select distinct tr.*
    from target_records tr
    join invalid_reset_decisions id on id.analysisrecordid = tr.analysisrecordid
),
invalid_reset_groups as (
    select distinct groupid
    from invalid_reset_records
),
target_audit as (
    select distinct ad.id
    from target_records tr
    join analysisgroups ag on ag.id = tr.groupid
    join auditdecisions ad
      on ad.tenant_id = @tenant_id
     and ad.containernumber = tr.containernumber
     and (ad.groupidentifier = ag.groupidentifier or ad.groupidentifier = ag.normalizedgroupidentifier)
     and (
          ad.scannertype = tr.scannertype
          or ad.scannertype = split_part(tr.scannertype, '-', 1)
          or ad.scannertype like split_part(tr.scannertype, '-', 1) || '-%'
     )
),
invalid_reset_audit as (
    select distinct ad.id
    from invalid_reset_decisions id
    join auditdecisions ad
      on ad.tenant_id = @tenant_id
     and ad.imageanalysisdecisionid = id.id
)
"@

$previewSql = @"
$commonTargetCte
select 'target_groups' as metric, count(distinct groupid)::int as count from target_groups
union all
select 'target_unresolved_records', count(distinct analysisrecordid)::int from target_records
union all
select 'target_completed_decisions', count(distinct id)::int from target_decisions
union all
select 'recoverable_decisions', count(distinct id)::int from recoverable_decisions
union all
select 'invalid_nonterminal_decisions_to_reset', count(distinct id)::int from invalid_reset_decisions
union all
select 'invalid_terminal_decisions_to_mark_non_choice', count(distinct id)::int
from invalid_decisions
where groupstatus in ('Completed','Submitted')
union all
select 'target_audit_rows_all', count(distinct id)::int from target_audit
union all
select 'target_audit_rows_to_delete', count(distinct id)::int from invalid_reset_audit
union all
select 'target_audit_child_rows_to_delete', count(aid.id)::int
from auditimagedecisions aid
join invalid_reset_audit ta on ta.id = aid.auditdecisionid;
"@

$previewByStatusSql = @"
$commonTargetCte
select
    ag.status as groupstatus,
    count(distinct ag.id)::int as groups,
    count(distinct tr.analysisrecordid)::int as unresolved_records
from target_records tr
join analysisgroups ag on ag.id = tr.groupid
group by ag.status
order by groups desc, ag.status;
"@

$terminalPreviewSql = @"
with unresolved_records as (
    select distinct ar.id as analysisrecordid, ar.groupid
    from analysisrecords ar
    join analysisgroups ag on ag.id = ar.groupid and ag.tenant_id = ar.tenant_id
    join imageanalysisdecisions d
      on d.tenant_id = ar.tenant_id
     and d.containernumber = ar.containernumber
     and d.decision in ('Normal','Abnormal')
     and d.groupidentifier is not null
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
     and (
          d.scannertype = coalesce(ar.scannertype, ag.scannertype, '')
          or d.scannertype = split_part(coalesce(ar.scannertype, ag.scannertype, ''), '-', 1)
          or d.scannertype like split_part(coalesce(ar.scannertype, ag.scannertype, ''), '-', 1) || '-%'
     )
    where ar.tenant_id = @tenant_id
      and ar.ismulticontainerscan = true
      and not (
          (ar.splitstatus = 'Chosen' and ar.splitjobid is not null and ar.splitresultid is not null)
          or ar.splitstatus in ('Skipped','NotApplicable','VisualSingle','Uncertain')
      )
)
select ag.status as groupstatus, count(distinct ur.groupid)::int as groups, count(distinct ur.analysisrecordid)::int as records
from unresolved_records ur
join analysisgroups ag on ag.id = ur.groupid
group by ag.status
order by groups desc, ag.status;
"@

$duplicatePreviewSql = @"
with decision_groups as (
    select
        ag.id as groupid,
        ag.groupidentifier,
        d.containernumber,
        d.scannertype,
        d.id as decisionid,
        d.createdat,
        d.updatedat,
        count(ad.id) as audit_refs
    from imageanalysisdecisions d
    join analysisgroups ag
      on ag.tenant_id = d.tenant_id
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
    left join auditdecisions ad on ad.imageanalysisdecisionid = d.id and ad.tenant_id = d.tenant_id
    where d.tenant_id = @tenant_id
      and d.decision in ('Normal','Abnormal')
    group by ag.id, ag.groupidentifier, d.containernumber, d.scannertype, d.id
),
ranked as (
    select *,
        count(*) over (partition by groupid, containernumber, scannertype) as row_count,
        row_number() over (
            partition by groupid, containernumber, scannertype
            order by (audit_refs > 0) desc, coalesce(updatedat, createdat) desc, decisionid desc
        ) as keep_rank
    from decision_groups
),
deletable as (
    select decisionid
    from ranked
    where row_count > 1 and keep_rank > 1 and audit_refs = 0
)
select count(*)::int as duplicate_decisions_deletable
from deletable;
"@

$schemaSql = @"
create schema if not exists maintenance;

create table if not exists maintenance.split_decision_audit_drift_repair_audit (
    id bigserial primary key,
    repair_run text not null,
    step text not null,
    table_name text not null,
    row_pk text not null,
    action text not null,
    before_data jsonb,
    after_data jsonb,
    created_at timestamptz not null default now()
);
"@

$backupSql = @"
$commonTargetCte
insert into maintenance.split_decision_audit_drift_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-target-analysisgroups', 'analysisgroups', ag.id::text, 'backup-update', to_jsonb(ag)
from analysisgroups ag
join target_groups tg on tg.groupid = ag.id
union all
select @repair_run, 'backup-target-analysisrecords', 'analysisrecords', ar.id::text, 'backup-update', to_jsonb(ar)
from analysisrecords ar
join target_records tr on tr.analysisrecordid = ar.id
union all
select @repair_run, 'backup-target-decisions', 'imageanalysisdecisions', d.id::text, 'backup-update-or-delete', to_jsonb(d)
from imageanalysisdecisions d
join target_decisions td on td.id = d.id
union all
select @repair_run, 'backup-target-auditdecisions', 'auditdecisions', ad.id::text, 'backup-preserve-or-delete', to_jsonb(ad)
from auditdecisions ad
join target_audit ta on ta.id = ad.id
union all
select @repair_run, 'backup-target-auditimagedecisions', 'auditimagedecisions', aid.id::text, 'backup-preserve-or-delete', to_jsonb(aid)
from auditimagedecisions aid
join target_audit ta on ta.id = aid.auditdecisionid
union all
select @repair_run, 'backup-target-manifestsnapshots', 'manifestsnapshots', ms.id::text, 'backup-preserve-or-delete', to_jsonb(ms)
from manifestsnapshots ms
join target_decisions td on td.id = ms.imageanalysisdecisionid
union all
select @repair_run, 'backup-target-containerannotations', 'containerannotations', ca.id::text, 'backup-update', to_jsonb(ca)
from containerannotations ca
join target_decisions td on td.id = ca.imageanalysisdecisionid
union all
select @repair_run, 'backup-target-assignments', 'analysisassignments', aa.id::text, 'backup-update', to_jsonb(aa)
from analysisassignments aa
join invalid_reset_groups tg on tg.groupid = aa.groupid
where aa.state = 'Active'
union all
select @repair_run, 'backup-target-queueentries', 'analysisqueueentries', aq.assignmentid::text, 'backup-delete', to_jsonb(aq)
from analysisqueueentries aq
join invalid_reset_groups tg on tg.groupid = aq.groupid
union all
select @repair_run, 'backup-target-completeness', 'containercompletenessstatuses', ccs.id::text, 'backup-update', to_jsonb(ccs)
from containercompletenessstatuses ccs
join invalid_reset_records tr
  on ccs.tenant_id = @tenant_id
 and ccs.containernumber = tr.containernumber
 and (
      ccs.scannertype = tr.scannertype
      or ccs.scannertype = split_part(tr.scannertype, '-', 1)
      or ccs.scannertype like split_part(tr.scannertype, '-', 1) || '-%'
 )
join analysisgroups ag on ag.id = tr.groupid
where ccs.groupidentifier = ag.groupidentifier or ccs.groupidentifier = ag.normalizedgroupidentifier;
"@

$deleteAuditImagesSql = @"
$commonTargetCte
, deleted as (
    delete from auditimagedecisions aid
    using invalid_reset_audit ta
    where aid.auditdecisionid = ta.id
    returning aid.id
)
select count(*)::int as deleted_audit_image_decisions from deleted;
"@

$deleteAuditSql = @"
$commonTargetCte
, deleted as (
    delete from auditdecisions ad
    using invalid_reset_audit ta
    where ad.id = ta.id
    returning ad.id
)
select count(*)::int as deleted_audit_decisions from deleted;
"@

$deleteManifestSnapshotsSql = @"
$commonTargetCte
, deleted as (
    delete from manifestsnapshots ms
    using invalid_reset_decisions id
    where ms.imageanalysisdecisionid = id.id
    returning ms.id
)
select count(*)::int as deleted_manifest_snapshots from deleted;
"@

$unlinkAnnotationsSql = @"
$commonTargetCte
, updated as (
    update containerannotations ca
       set imageanalysisdecisionid = null,
           updatedat = now(),
           updatedby = 'split-decision-audit-drift-repair'
    from invalid_reset_decisions id
    where ca.imageanalysisdecisionid = id.id
    returning ca.id
)
select count(*)::int as unlinked_container_annotations from updated;
"@

$deleteDecisionsSql = @"
$commonTargetCte
, deleted as (
    delete from imageanalysisdecisions d
    using invalid_reset_decisions td
    where d.id = td.id
    returning d.id
)
select count(*)::int as deleted_image_analysis_decisions from deleted;
"@

$recoverLineageSql = @"
$commonTargetCte
, updated as (
    update analysisrecords ar
       set splitstatus = 'Chosen',
           splitjobid = coalesce(ar.splitjobid, rrc.decision_splitjobid),
           splitresultid = rrc.decision_splitresultid,
           status = 'Decided'
    from recoverable_record_choice rrc
    where ar.id = rrc.analysisrecordid
      and (
          ar.splitstatus is distinct from 'Chosen'
          or ar.splitjobid is distinct from coalesce(ar.splitjobid, rrc.decision_splitjobid)
          or ar.splitresultid is distinct from rrc.decision_splitresultid
          or ar.status is distinct from 'Decided'
      )
    returning ar.id
)
select count(*)::int as recovered_split_lineage_records from updated;
"@

$markTerminalNonChoiceSql = @"
$commonTargetCte
, terminal_invalid_records as (
    select distinct tr.analysisrecordid
    from target_records tr
    join invalid_decisions id on id.analysisrecordid = tr.analysisrecordid
    where id.groupstatus in ('Completed','Submitted')
),
updated as (
    update analysisrecords ar
       set splitstatus = 'Skipped',
           splitresultid = null,
           status = 'Decided'
    from terminal_invalid_records tir
    where ar.id = tir.analysisrecordid
      and (
          ar.splitstatus is distinct from 'Skipped'
          or ar.splitresultid is not null
          or ar.status is distinct from 'Decided'
      )
    returning ar.id
)
select count(*)::int as marked_terminal_non_choice_records from updated;
"@

$resetRecordsSql = @"
$commonTargetCte
, updated as (
    update analysisrecords ar
       set status = 'Ready'
    from invalid_reset_records tr
    where ar.id = tr.analysisrecordid
      and ar.status is distinct from 'Ready'
    returning ar.id
)
select count(*)::int as reset_analysis_records from updated;
"@

$resetCompletenessSql = @"
$commonTargetCte
, updated as (
    update containercompletenessstatuses ccs
       set workflowstage = 'ImageAnalysis',
           updatedat = now()
    from invalid_reset_records tr
    join analysisgroups ag on ag.id = tr.groupid
    where ccs.tenant_id = @tenant_id
      and ccs.containernumber = tr.containernumber
      and (
          ccs.scannertype = tr.scannertype
          or ccs.scannertype = split_part(tr.scannertype, '-', 1)
          or ccs.scannertype like split_part(tr.scannertype, '-', 1) || '-%'
      )
      and (ccs.groupidentifier = ag.groupidentifier or ccs.groupidentifier = ag.normalizedgroupidentifier)
      and ccs.workflowstage is distinct from 'ImageAnalysis'
    returning ccs.id
)
select count(*)::int as reset_completeness_rows from updated;
"@

$resetGroupsSql = @"
$commonTargetCte
, updated as (
    update analysisgroups ag
       set status = 'Ready',
           updatedatutc = now()
    from invalid_reset_groups tg
    where ag.id = tg.groupid
      and ag.status is distinct from 'Ready'
    returning ag.id
)
select count(*)::int as reset_analysis_groups from updated;
"@

$expireAssignmentsSql = @"
$commonTargetCte
, updated as (
    update analysisassignments aa
       set state = 'Expired',
           updatedatutc = now()
    from invalid_reset_groups tg
    where aa.groupid = tg.groupid
      and aa.state = 'Active'
    returning aa.id
)
select count(*)::int as expired_active_assignments from updated;
"@

$deleteQueueSql = @"
$commonTargetCte
, deleted as (
    delete from analysisqueueentries aq
    using invalid_reset_groups tg
    where aq.groupid = tg.groupid
    returning aq.assignmentid
)
select count(*)::int as deleted_queue_entries from deleted;
"@

$backupDuplicateSql = @"
with decision_groups as (
    select
        ag.id as groupid,
        d.containernumber,
        d.scannertype,
        d.id as decisionid,
        d.createdat,
        d.updatedat,
        count(ad.id) as audit_refs
    from imageanalysisdecisions d
    join analysisgroups ag
      on ag.tenant_id = d.tenant_id
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
    left join auditdecisions ad on ad.imageanalysisdecisionid = d.id and ad.tenant_id = d.tenant_id
    where d.tenant_id = @tenant_id
      and d.decision in ('Normal','Abnormal')
    group by ag.id, d.containernumber, d.scannertype, d.id
),
ranked as (
    select *,
        count(*) over (partition by groupid, containernumber, scannertype) as row_count,
        row_number() over (
            partition by groupid, containernumber, scannertype
            order by (audit_refs > 0) desc, coalesce(updatedat, createdat) desc, decisionid desc
        ) as keep_rank
    from decision_groups
),
deletable as (
    select decisionid
    from ranked
    where row_count > 1 and keep_rank > 1 and audit_refs = 0
)
insert into maintenance.split_decision_audit_drift_repair_audit
    (repair_run, step, table_name, row_pk, action, before_data)
select @repair_run, 'backup-duplicate-decisions', 'imageanalysisdecisions', d.id::text, 'backup-delete', to_jsonb(d)
from imageanalysisdecisions d
join deletable dd on dd.decisionid = d.id;
"@

$deleteDuplicateSql = @"
with decision_groups as (
    select
        ag.id as groupid,
        d.containernumber,
        d.scannertype,
        d.id as decisionid,
        d.createdat,
        d.updatedat,
        count(ad.id) as audit_refs
    from imageanalysisdecisions d
    join analysisgroups ag
      on ag.tenant_id = d.tenant_id
     and (d.groupidentifier = ag.groupidentifier or d.groupidentifier = ag.normalizedgroupidentifier)
    left join auditdecisions ad on ad.imageanalysisdecisionid = d.id and ad.tenant_id = d.tenant_id
    where d.tenant_id = @tenant_id
      and d.decision in ('Normal','Abnormal')
    group by ag.id, d.containernumber, d.scannertype, d.id
),
ranked as (
    select *,
        count(*) over (partition by groupid, containernumber, scannertype) as row_count,
        row_number() over (
            partition by groupid, containernumber, scannertype
            order by (audit_refs > 0) desc, coalesce(updatedat, createdat) desc, decisionid desc
        ) as keep_rank
    from decision_groups
),
deletable as (
    select decisionid
    from ranked
    where row_count > 1 and keep_rank > 1 and audit_refs = 0
),
deleted as (
    delete from imageanalysisdecisions d
    using deletable dd
    where d.id = dd.decisionid
    returning d.id
)
select count(*)::int as deleted_duplicate_decisions from deleted;
"@

$auditCountSql = @"
select repair_run, table_name, action, count(*)::int as rows
from maintenance.split_decision_audit_drift_repair_audit
where repair_run = @repair_run
group by repair_run, table_name, action
order by table_name, action;
"@

$handle = $null
$success = $false

try {
    $handle = Open-NscimConnection -UseSuperuser -TenantId $TenantId

    Write-Host "Repair run: $RepairRun"
    Write-Host "Mode:       $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })"
    Write-Host "Scope:      $(if ($IncludeTerminal) { 'active + terminal groups' } else { 'active/non-terminal groups only' })"

    Show-Query -Handle $handle -Title "All unresolved split drift by group status" -Sql $terminalPreviewSql
    Show-Query -Handle $handle -Title "Target repair counts" -Sql $previewSql
    Show-Query -Handle $handle -Title "Target repair counts by status" -Sql $previewByStatusSql
    Show-Query -Handle $handle -Title "Unreferenced duplicate decisions that can be deleted" -Sql $duplicatePreviewSql

    if (-not $Apply) {
        Write-Host ""
        Write-Host "Dry run only. Re-run with -Apply to create audit backups and repair target rows."
        $success = $true
        return
    }

    Write-Section "Applying repair"
    Invoke-NscimNonQuery -Handle $handle -Sql $schemaSql | Out-Null
    Invoke-NscimNonQuery -Handle $handle -Sql $backupSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-CountStep -Handle $handle -Title "Recover split lineage from completed decisions" -Sql $recoverLineageSql
    Invoke-CountStep -Handle $handle -Title "Mark terminal no-lineage rows as non-choice" -Sql $markTerminalNonChoiceSql
    Invoke-CountStep -Handle $handle -Title "Delete invalid audit child rows" -Sql $deleteAuditImagesSql
    Invoke-CountStep -Handle $handle -Title "Delete invalid audit rows" -Sql $deleteAuditSql
    Invoke-CountStep -Handle $handle -Title "Delete invalid manifest snapshots" -Sql $deleteManifestSnapshotsSql
    Invoke-CountStep -Handle $handle -Title "Unlink invalid decision annotations" -Sql $unlinkAnnotationsSql
    Invoke-CountStep -Handle $handle -Title "Delete invalid completed image decisions" -Sql $deleteDecisionsSql
    Invoke-CountStep -Handle $handle -Title "Reset analysis records for split choice" -Sql $resetRecordsSql
    Invoke-CountStep -Handle $handle -Title "Reset completeness workflow to ImageAnalysis" -Sql $resetCompletenessSql
    Invoke-CountStep -Handle $handle -Title "Reset groups to Ready" -Sql $resetGroupsSql
    Invoke-CountStep -Handle $handle -Title "Expire active assignments on reset groups" -Sql $expireAssignmentsSql
    Invoke-CountStep -Handle $handle -Title "Delete stale queue entries for reset groups" -Sql $deleteQueueSql
    Invoke-NscimNonQuery -Handle $handle -Sql $backupDuplicateSql -Parameters @{ tenant_id = [long]$TenantId; repair_run = $RepairRun } | Out-Null
    Invoke-CountStep -Handle $handle -Title "Delete unreferenced duplicate completed decisions" -Sql $deleteDuplicateSql

    Show-Query -Handle $handle -Title "Target repair counts after repair" -Sql $previewSql
    Show-Query -Handle $handle -Title "Duplicate cleanup counts after repair" -Sql $duplicatePreviewSql
    Show-Query -Handle $handle -Title "Audit backups written" -Sql $auditCountSql

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
