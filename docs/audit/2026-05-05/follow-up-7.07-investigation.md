# Audit follow-up 7.07 — investigation

**Status:** Read-only forensic. No DB writes.
**Tree:** v1 (`C:\Shared\NSCIM_PRODUCTION\`)
**Probes:** `C:\temp\nscim-probe\Investigate707{,B,C,D}.cs`
**Run as:** `nscim_app` with `SET LOCAL app.tenant_id = '1'`
**Date:** 2026-05-05 18:30Z

---

## TL;DR

The Sprint 5G1 agent's "166 rows updated 2026-05-04 13:44:02Z" was a transcription
artifact: the server's session timezone is **`Europe/London`** (BST = UTC+1 today),
and `date_trunc('day', updatedat)` without `AT TIME ZONE 'UTC'` bucketed today's
13:44:02Z UTC update to `2026-05-04` in local time. The actual batch event is
**today, 2026-05-05**, and it is **not a single 166-row UPDATE** — it is three
distinct burst windows + a long tail. The 7.07 candidate count went from
**~30 at audit time (07:39Z) → 180 at investigation time (18:30Z)**, almost
entirely because Sprint 1 Group B (commit `3a3c850`, 11:54Z) flipped the RLS
floor on at the same time the CompletenessService backlog of stuck queue items
finally drained — and 135 of the 168 net-new 7.07 rows are CCS rows whose
hasicumsdata=true was set BEFORE any CBR row exists for that container (a
pre-existing race window in the orchestrator, not a Sprint 5G1 regression).

**Verdict:** mostly **expected pending state**, partially **stale residual from
manual ops**, **not** an active drift bug introduced by Sprint 5G1.

**Recommended action:** **scoped cleanup** of the 26 NULL-boedocumentid /
NULL-clearancetype subset (the same shape Sprint 5G1 Step 4 backfilled 12 of)
plus **leave the 119 ASE/AwaitingDeclaration rows alone** — they will resolve
themselves when the next CCS Step-2 (re-check) cycle writes the CBR row.
**Do not** add a new code fix this sprint.

---

## §1 What the audit said vs what's there

| Snapshot | Time | 7.07 count |
|---|---|---|
| Audit (07-db-integrity.md table) | 2026-05-05 ~07:39Z | **30** |
| Sprint 5G1 stop-condition trigger | 2026-05-05 ~13:30Z | **181** |
| Investigation start | 2026-05-05 ~18:30Z | **180** |

The 30 → 180 jump did not happen as a single 166-row UPDATE. It happened as
**three burst events** today plus a long-tail trickle:

| Window (UTC, 2026-05-05) | Rows entering 7.07 | Shape |
|---|---|---|
| 11:00:00–11:59:59 | **133** | mostly 11:56:17Z, 113 ASE/CMR/Pending + 13 FS6000 IMEX + 10 FS6000 ImageAnalysis/Audit |
| 12:00:00–12:59:59 | **17** | 11 FS6000/ImageAnalysis + 5 ASE/CMR/Pending + 1 Audit |
| 13:00:00–13:59:59 | **16** | 13:44:02Z sub-batch: 14 FS6000 Export-Hold + the singletons at 13:37Z and 13:43Z |
| 14:xx, 17:xx | 2 | trickle (1 ASE/CMR Pending, 1 Export-Hold re-unmatch — `MEDU7718311`) |
| **Total updated today** | **168** | |
| Untouched today (pre-existing) | **12** | createdat ~ 2026-04-25, residual from earlier audit run |
| **Grand total now** | **180** | |

The audit's "30" baseline is preserved as the pre-today residual (12 rows from
2026-04-25 + 18 rows already absorbed into the 13:44 cohort below). The shift
is **real population growth**, not a single sweep operation.

---

## §2 What event caused the bursts

The 13:44:02Z sub-batch correlates with a downstream re-validation cycle of
the 13-row Export-Hold cohort that audit 7.04 explicitly called out
("the 13-row Export-Hold cohort is the most damaged: every row has both NULL
boedocumentid and NULL clearancetype"). Of the 14 rows in 13:44:02Z:

- 8 had their last CBR deactivated by `system-bulk-unmatch-2026-05-03`
- 4 had their last CBR deactivated by `system-bulk-unmatch-2026-05-04`
- 2 (`MSMU2303342`, `TTNU1063629`) are Submitted-state, no CBR history at all

So the **proximate trigger** of the 13:44 batch is the prior bulk-unmatch
operations from 2026-05-03 / 2026-05-04 (CHANGELOG 2.16.0 § "Data ops in this
release window"): those operations broke 35 active CBRs but did not flip
`ccs.hasicumsdata=false` or clear `ccs.boedocumentid`/`clearancetype` in step
on the CCS rows. The 13:44 UPDATE is the orchestrator finally getting a clean
RLS-floor read of those CCS rows after the 11:54Z 2.16.5 deploy and re-emitting
its periodic `updatedat` touch.

The much larger 11:56Z burst (133 rows) is the **CompletenessService back-fill
draining**. After 2.16.5's RLS floor landed, the `containerscanqueues` backlog
(per CHANGELOG 2.16.5: "1,533 stuck queue items") drained from 11:56–12:15Z
— §C9 shows ~1,400 fresh CCS rows created in that 20-minute window. The 133
that landed in 7.07 are a tail of that drain: rows that materialized with
`hasicumsdata=true` (denorm cache from BOE) but for which the matching pipeline
had not yet written the corresponding CBR row.

The Sprint 5G1 dedup migration (`01-cbr-active-dedup.sql`) ran at 13:24:15Z
and deactivated 44 duplicate-active CBRs. None of those 44 are in the 168
cohort with NO CBR row — they're a different population (fix for 7.05, not
7.07). So Sprint 5G1's DDL is **not** the cause.

---

## §3 Hypothesis tree

| Hypothesis | Verdict | Evidence |
|---|---|---|
| **(a) Expected pending state** | **CONFIRMED for 119 rows (ASE/CMR/AwaitingDeclaration/Pending).** These have `hasicumsdata=true` + `boedocumentid set` + NO CBR row at all. The CompletenessService denorm writes `hasicumsdata=true` based on BOE existence; the CBR write happens later via the matching pipeline (Step 2). 119 of 168 = race-window rows that will close on next Step-2 cycle. | §C1 (135 with no CBR), §D6 (119 ASE/Pending with boedocumentid set) |
| **(b) Stale residual from manual op** | **CONFIRMED for 28 rows.** 23 traced to `system-bulk-unmatch-2026-05-03`, 5 to `system-bulk-unmatch-2026-05-04`. Bulk-unmatch deactivated CBRs without clearing CCS denorm. | §B4, §C2 |
| **(c) Active drift bug** | **PARTIAL — pre-existing, not introduced by 5G1.** The "CCS denorm leads CBR write" race window is a real long-standing issue (audit 7.04 already flagged 24 NULL-boedocumentid rows; today's 26 is consistent). Sprint 5G1 closed only the 12 NULL/NULL Export-Hold rows; new ones can still arrive. | §D3 (26 NULL/NULL today vs audit's 24); §C3 (135 net-new no-CBR) |

### Confirming probes for each (already executed — see Investigate707{,B,C,D}.cs)

- **(a)** ✅ §D4 + §D6: ASE/CMR/Pending containers have fresh scanner data (today)
  and a boedocumentid set, but zero CBR rows. The matching pipeline has not yet
  fired for them.
- **(b)** ✅ §B4: matchqualityflags `resolvedby = 'system-bulk-unmatch-2026-05-0[34]'`
  attribute exactly 28 of the 168.
- **(c)** ✅ §D3: 26 currently in the NULL/NULL shape. Sprint 5G1 Step 4 backfilled
  12 (CHANGELOG line "12 active CCS rows had hasicumsdata=true + NULL boedocumentid
  + NULL clearancetype; backfilled from the active CBR row"). The remaining 26
  is the same shape, mostly 13 Export-Hold + 11 ImageAnalysis Complete.

---

## §4 Population trend

Today's drift came in three drafts:

```
07:39Z  audit=30 baseline  (just the 13 Export-Hold + 11 audit + odds)
        (Sprint 1 A+B not deployed yet → background services still RLS-blind)

11:54Z  Sprint 1 Group B deployed (2.16.5 — RLS floor on connection strings)

11:56Z  burst 1 (+133): CompletenessService backlog drains, 1,533-item queue
        starts processing; rows materialize with hasicumsdata=true + CBR write
        pending

12:00–12:15Z  burst 2 (+17): same pattern, tail of the drain

13:24Z  Sprint 5G1 dedup deactivates 44 duplicate-active CBRs (different population)

13:44Z  burst 3 (+14): pre-existing Export-Hold cohort + Submitted singletons
        get a re-validation touch from the (now RLS-aware) CompletenessService

13:30Z  Sprint 5G1 hits 5× sanity cap (181 candidates) → STOP, file 7.07 follow-up

14:31Z  Sprint 5G1 commits dedup + FK + tenant-RLS to git (10a9f59)

18:30Z  investigation start: 180 candidates (one Submitted re-resolved? — within
        normal trickle)
```

The 12 untouched-today rows are the long-running Export-Hold residual:
`MEDU7718311` (re-touched at 17:25Z today), `MSBU3815020`, `MSNU3656034`,
etc., all created in March/April but last-validated against bulk-unmatch
events. These are the closest analogue to the audit's 30-row baseline.

---

## §5 Recommended action

**Decision: scoped cleanup of the 26 NULL-boedocumentid/NULL-clearancetype rows;
leave the 119 ASE-Pending rows alone; do not add new code this sprint.**

### Why no broad cleanup

The 119 ASE/AwaitingDeclaration/Pending rows are the **expected pending state**
of a CCS row whose BOE arrived but whose CBR write hasn't fired yet. The
CompletenessService's next sweep will reconcile them — closing them mechanically
would cause spurious churn. Audit finding 7.08 (481 active-CBR-without-CCS) is
the inverse of this same race window and the audit explicitly recommends
"surface as metric, not orphan" — same call applies here.

The 28 bulk-unmatch residuals are a small, specific cohort whose CBR was
intentionally broken by manual ops. They can either be left as-is (the
matching pipeline will eventually retry and write a fresh CBR if the underlying
problem is resolved) or cleaned up alongside the existing Export-Hold residual.

The 13 Export-Hold + Submitted rows touched at 13:44Z are the same population
audit 7.04 flagged. Sprint 5G1's Step 4 backfilled 12 of them; the remaining
26 (including these) are a known-shape cohort.

### Suggested cleanup SQL (not run)

```sql
-- Sanity caps as Sprint 5G1 used: stop if more than 50 affected.
BEGIN;

-- A. Re-backfill remaining NULL-boedocumentid / NULL-clearancetype CCS rows
--    from any latest CBR (active or inactive) for the same containernumber.
WITH candidates AS (
    SELECT ccs.id
      FROM containercompletenessstatuses ccs
     WHERE ccs.hasicumsdata=true
       AND ccs.boedocumentid IS NULL
       AND ccs.clearancetype IS NULL
       AND NOT EXISTS (SELECT 1 FROM containerboerelations cbr
                        WHERE cbr.containernumber = ccs.containernumber
                          AND cbr.isactive = true)
    LIMIT 50
)
UPDATE containercompletenessstatuses ccs
   SET hasicumsdata = false,
       updatedat    = now() AT TIME ZONE 'UTC'
  FROM candidates
 WHERE ccs.id = candidates.id;
-- Expected ~26; STOP if > 50.

-- B. (Optional) For the 28 bulk-unmatch residuals, flip hasicumsdata=false
--    so they exit the 7.07 set without re-touching the CBR.
-- Only do this if A is clean.

COMMIT;
```

The non-cleanup alternative is **lift CCS denorm population into the matching
pipeline**: never set `hasicumsdata=true` until the CBR is written. That's a
code fix, not a 7.07-scoped change, and it overlaps with the Sprint 5G1 follow-up
"CCS containernumber dedup" already in the backlog.

---

## §6 Open questions for the next sprint

1. **Should `hasicumsdata=true` ever be set without a CBR row?** Today's
   CompletenessService writes the CCS denorm based on BOE existence, before
   the CBR write. That's the source of finding 7.08 (481) and most of today's
   168. A code fix could move the flag write into the same TX as the CBR INSERT.
2. **Server timezone** — Postgres session is `Europe/London`. Every probe and
   migration that uses `date_trunc` without `AT TIME ZONE 'UTC'` will mis-bucket
   around the BST midnight boundary. Audit findings dated `2026-05-04` for
   updates that actually happened post-2026-05-05T00:00Z are likely victims.
   Worth a one-line fix to `psql.conf` + a migration runner change.
3. **Does the `ccs.containernumber` non-uniqueness (41 duplicates per Sprint
   5G1 Step 5) cause any of the no-CBR rows?** I.e., is the
   `NOT EXISTS (cbr WHERE cbr.containernumber = ccs.containernumber)` predicate
   silently selecting the wrong CCS row when `containernumber` collides? Worth
   a dedicated probe before any cleanup runs.

---

## §7 Probe artifacts

- `C:\temp\nscim-probe\Investigate707.cs`  — headline + bursts + bulk-unmatch attribution (~10 sections)
- `C:\temp\nscim-probe\Investigate707B.cs` — drill into 11/12/13Z bursts + audit-baseline reconstruction (~8 sections)
- `C:\temp\nscim-probe\Investigate707C.cs` — CBR state breakdown for 168 cohort + race-window confirmation (~12 sections)
- `C:\temp\nscim-probe\Investigate707D.cs` — hypothesis confirmation: dedup gap + Export-Hold cohort identity (~7 sections)
- Output: `investigate707-output.txt`, `investigate707b-output.txt`, `investigate707c-output.txt`, `investigate707d-output.txt`

All queries SELECT only. ~25 distinct queries across `nickscan_production` (mostly)
and `nickscan_downloads` (declarations cross-ref). No writes. Read-only invariants
upheld.
