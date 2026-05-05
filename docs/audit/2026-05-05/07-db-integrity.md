# 07 — DB schema, RLS, data integrity

**Audit:** NSCIM v1 cold audit, 2026-05-05
**Agent:** DB integrity
**Scope:** Read-only against `nickscan_production`, `nickscan_icums`,
`nickscan_downloads`, `nick_comms`, `nickhr` (5 PG18 DBs on
`localhost:5432`). Probe scripts are in `C:\temp\nscim-probe\DbIntegrity*.cs`
(four passes, all read-only). All findings are evidenced by query results
captured at probe-time (2026-05-05 07:39 UTC). The remote SQL Server
`networking` (10.0.0.3) is surfaced as a topology fact only — its schema
is owned by ASE and out of NSCIM's mutation path.

## Scope confirmation

This pass exhaustively documents (a) RLS coverage and policy presence on
every table across the 5 PG DBs, (b) tenant_id column presence + NULL
counts, (c) PK / unique-key coverage, (d) FK constraints (in-DB and
cross-DB), (e) index health (idx_scan=0 + the 2026-04-28 compound
queue indexes), (f) denormalized-field NULL coverage on
`containercompletenessstatuses` and adjacent tables, (g) state-machine
distributions for `analysisgroups.status`, `analysisassignments.state`,
and `containercompletenessstatuses.workflowstage`, (h) audit-column
coverage (`createdat`/`createdatutc`), (i) EF migration history, (j)
duplicate-key risk in active CBR + AGs + AAs, and (k) cross-DB logical
FKs (`containerboerelations.icumsboeid` → `nickscan_downloads.boedocuments.id`,
`containercompletenessstatuses.boedocumentid` → same).

Out of scope: query-plan tuning (suggestions only), v2 (`C:\Shared\ERP V2\`),
NickHR/NickFinance internals, the deep observability tables (Agent 8
owns those).

## Findings

| ID | Sev | File:Line / DB | Issue | Evidence | Proposed Fix | Effort | Risk |
|---|---|---|---|---|---|---|---|
| 7.01 | **P1** | db: `nickscan_production` | **Zero in-DB FK constraints on the cargo-pipeline core tables.** `analysisgroups`, `analysisassignments`, `analysisqueueentries`, `containercompletenessstatuses`, `containerboerelations`, `imageanalysisdecisions` all rely on logical FKs in C# code only. Only `auditdecisions.imageanalysisdecisionid` has a real FK (1 of 6 expected). | Pass-3 query: `FROM information_schema.table_constraints WHERE table_name IN (...) AND constraint_type='FOREIGN KEY'` returned **1 row** out of 6 tables checked. | Add `FOREIGN KEY ... ON DELETE NO ACTION ON UPDATE CASCADE` on the 5 missing relationships in a migration; backfill orphan cleanups first (see 7.07/7.08). | M | Med |
| 7.02 | **P1** | db: `nickscan_production.analysisqueueentries` | **No RLS, no `tenant_id` column at all.** Phase-1 multi-tenancy migrations never covered AQE because the table was added later (`20260423180227_AddSplitJobIdToCrossRecordScans`). `Grep tenant_id Core/Entities/Analysis/AnalysisQueueEntry.cs` = 0 hits — the entity itself doesn't carry it. Today this is latent (single-tenant prod), but cross-tenant leak risk for any future multi-tenant rollout. Topology agent flagged this; confirmed. | `pg_class.relrowsecurity=False`, `relforcerowsecurity=False`, no `pg_policy` rows. Cardinality at probe time = 0. | Add `tenant_id bigint NOT NULL DEFAULT current_setting('app.tenant_id')::bigint`, then enable RLS + `FORCE ROW LEVEL SECURITY` + `tenant_isolation_*` policy. Update entity class + ApplicationDbContext. Coordinate with assignment-pipeline agent (04). | S | Med |
| 7.03 | P2 | db: `nickscan_production.splitter_consensus_corpus` | Splitter-training table has no RLS, no `tenant_id` (24 rows, 14 columns of training data + image bytea). Single-tenant data today but lives in the same DB as RLS-enforced tables — inconsistency. | `pg_class.relrowsecurity=False`, `has_tenant_id=False` (probe pass 3). | Either tenant it (per phase-1 pattern) or move to a dedicated training DB. Either is fine; current state is the inconsistent one. | XS | Low |
| 7.04 | **P1** | db: `nickscan_production.containercompletenessstatuses` | **CCS denormalized fields silently NULL**: 2,599 active CCS rows with `hasicumsdata=true`, of which **24 have NULL `boedocumentid`** and **24 have NULL `clearancetype`** and **735 have NULL `groupidentifier`**. The 13-row Export-Hold cohort is the most damaged: every row has both NULL boedocumentid and NULL clearancetype. This matches the 2026-05-04 mis-tagged-consolidated incident and is wider than that one event. | `SELECT count(*) FILTER (WHERE boedocumentid IS NULL) FROM containercompletenessstatuses WHERE hasicumsdata=true` → 24. Workflow-stage breakdown: Pending=726/0/0/724, Audit=634/4/4/0, ImageAnalysis=285/4/4/5, Submitted=119/2/2/0, Export-Hold=13/13/13/6, Completed=11/0/0/0, PendingSubmission=811/1/1/0. | Backfill from CBR (`UPDATE ccs SET boedocumentid = (SELECT cbr.icumsboeid …)` scoped to active rows). Add a maintenance probe to flag drift. Optionally add a check constraint `(hasicumsdata=false OR boedocumentid IS NOT NULL)`. | S | Low |
| 7.05 | P2 | db: `nickscan_production.containerboerelations` | **18 distinct containers have multiple `isactive=true` CBRs (37 excess rows total), all on the SAME scanner.** Sample MRKU2369877 has 3 identical active rows (id 375/376/377, all `icumsboeid=17239`, `scannertype=ASE`, same `createdat=2026-03-25 02:33:18`). Looks like a triple-write bug from the 2026-03 ingestion path. | `SELECT containernumber, count(*) FROM containerboerelations WHERE isactive=true GROUP BY containernumber HAVING count(*) > 1` → 18 rows; max=3. `SELECT count(*) FROM (... HAVING count(*) > 1 same scanner)` → 18 (same scanner only). | Deactivate all but the MAX(id) per (containernumber, isactive=true) group. Add a partial unique index `CREATE UNIQUE INDEX ... ON containerboerelations(containernumber) WHERE isactive=true` to prevent recurrence. | S | Low |
| 7.06 | P2 | db: `nickscan_production.imageanalysisdecisions` | **392 `imageanalysisdecisions` rows reference a `containernumber` with NO matching CCS row.** Most-recent orphan reviewed on 2026-04-21 14:23 by `pimage`. All 392 have `groupidentifier` and `scannertype` populated (none NULL). Looks like CCS rows were retired/archived after decisions were saved — but the decisions weren't archived in step. | `SELECT count(*) FROM imageanalysisdecisions iad WHERE NOT EXISTS (SELECT 1 FROM ccs WHERE ccs.containernumber=iad.containernumber)` → 392. Sample rows: 70226135661, 90326222889, 40326212003, … | Decide intent: are decisions immutable post-CCS-retire (= add `archivedat` column to keep them but flag), or should they cascade-delete? Until decided, surface in `MatchCorrections.razor`. Adding the FK from finding 7.01 will block until cleanup. | S | Med |
| 7.07 | P2 | db: `nickscan_production` | **30 CCS rows with `hasicumsdata=true` have NO active CBR.** These are CCS rows where ICUMS data was matched but the CBR row was deactivated without flipping `hasicumsdata=false`. | `SELECT count(*) FROM ccs WHERE hasicumsdata=true AND NOT EXISTS (SELECT 1 FROM cbr WHERE cbr.containernumber=ccs.containernumber AND cbr.isactive=true)` → 30. | Sweep: for each, either re-activate the most-recent CBR or flip `hasicumsdata=false` + clear denorm cols. | XS | Low |
| 7.08 | P2 | db: `nickscan_production` | **481 active CBR rows have NO matching CCS.** Inverse of 7.07. CBR was created (typically by ICUMS cascade) but CCS never materialized — scanner data didn't arrive yet, and CCS-bootstrap didn't run. | `SELECT count(*) FROM cbr WHERE isactive=true AND NOT EXISTS (SELECT 1 FROM ccs WHERE ccs.containernumber=cbr.containernumber)` → 481. | Investigate `ContainerCompletenessOrchestratorService.cs` bootstrap — is it gated on scanner data arriving first? If yes, this is expected pending state, not drift. Surface as metric, not orphan. | S | Med |
| 7.09 | P2 | db: `nickscan_production.analysisgroups` | **7 AGs in `Ready` status with NO assignment row at all.** Topology agent flagged: 16 Ready AGs + 1 Analyst marked ready, but `AnalysisQueueEntries=0` (i.e. orchestrator queue-materialization isn't running cleanly). 7 of the 16 have no AA either, meaning the orchestrator never even created the assignment. | `SELECT count(*) FROM analysisgroups ag WHERE ag.status='Ready' AND NOT EXISTS (SELECT 1 FROM analysisassignments aa WHERE aa.groupid=ag.id)` → 7. | Cross-check with assignment-pipeline agent (04). DB-side fix is unclear until code path is established. Likely a code bug, not data drift. | S | Med |
| 7.10 | P2 | db: `nickscan_production.analysisassignments` | **No `Active` state assignments — entire population is in terminal states.** 11,437 rows total: Expired=9,235, Released=2,089, Cancelled=113. Last 24h: 60 Cancelled (orphan-AG guard from 2.16.1), 68 Expired, 6 Released. Orchestrator is leasing + expiring without ever holding `Active`. | `SELECT state, count(*) FROM analysisassignments GROUP BY state`: Expired/Released/Cancelled only. `WHERE createdatutc > now()-interval '24h'`: same three states. | This is operational, not a DB-integrity bug per se — but the absence of any Active row suggests assignments are being expired faster than operators can claim them. Defer to agent 04. | XS | Low |
| 7.11 | P2 | db: `nickscan_downloads.boedocuments` | **86,381 of 119,668 `boedocuments` (72%) have NULL `documenttype`.** Of those, 86,365 are `clearancetype='CMR'` (CMR pre-decs that were never tagged in 2.16.0's documenttype rollout). 16 are `clearancetype='IM'` — these are real BOEs that slipped the documenttype tagging. **Also: 86,365 NULL `regimecode` and 86,365 NULL `declarationnumber` in the same cohort** — the CMR cohort is essentially an empty shell with only `containernumber`. | `SELECT count(*) FILTER (WHERE documenttype IS NULL)` → 86,381; `SELECT clearancetype, count(*) … WHERE documenttype IS NULL` → CMR=86,365, IM=16. NULL_regimecode=86,365 (= same CMR cohort). | Per memory `feedback_regime80_cmr_is_transit.md`, CMRs are deliberately not regime-tagged. The 16 IM-without-documenttype is the bug — backfill from `regimecode` via `RegimeDirectionMap`. | XS | Low |
| 7.12 | P2 | db: `nickscan_downloads.boedocuments` | **354 BOEs with NULL `deliveryplace` (0.30%).** This is the cohort that defeats the cardinal port rule (Layer 1) — `IsLocationMatch(scanner, NULL)` returns false and zeroes `hasicumsdata`. | `SELECT count(*) FILTER (WHERE deliveryplace IS NULL OR deliveryplace='')` → 354. | Backfill from manifest. Long-term: enforce NOT NULL with default fallback at ingestion. Cross-cuts agent 03 (match-correctness). | S | Med |
| 7.13 | P3 | db: `nickscan_downloads.boedocuments` | **boedocuments.declarationnumber duplicates** (each declaration = many containers, expected for consolidated). Top dup = 80326231986 with 100 rows; 86,365 NULL declarationnumber rows are the CMR cohort (also expected). | `SELECT declarationnumber, count(*) FROM boedocuments WHERE declarationnumber IS NOT NULL GROUP BY declarationnumber HAVING count(*) > 1 ORDER BY 2 DESC LIMIT 10`: top values 100/70/51/42/40/40/40/40/40. | None — by design. Doc as expected behavior so the duplicate count doesn't trigger false alarm in Phase 3. | XS | Low |
| 7.14 | P2 | db: `nickscan_production` index health | **34 MB unused index on `endpointusagelog.ix_endpointusagelog_isdeprecated_timestamp`** + matching 34 MB `_tenant_id` companion. Both `idx_scan=0`. The endpoint-usage table is high-volume churn (Agent 8 territory) but two 34 MB indexes scanned zero times = pure write overhead. | `pg_stat_user_indexes`: `endpointusagelog | ix_endpointusagelog_isdeprecated_timestamp | 0 | 34 MB`; `endpointusagelog | ix_endpointusagelog_tenant_id | 0 | 34 MB`. | Drop one or both after confirming with EndpointUsageBufferService query patterns. | XS | Low |
| 7.15 | P3 | db: `nickscan_icums` | **23 indexes scanned 0 times in `nickscan_icums`**, including 4 large ones on `icumcontainerdata` and `icummanifestitems` (consigneename 3.4MB, shippername 3.1MB, masterblnumber 3.0MB, hscode 1.9MB, countryoforigin 1.5MB, declarationnumber 1.4MB). pg_stat_user_indexes counters likely got reset by a recent restart, OR these are genuinely unused. | `SELECT relname, indexrelname, idx_scan, pg_size_pretty(pg_relation_size(indexrelid)) FROM pg_stat_user_indexes WHERE idx_scan=0 ORDER BY size DESC` (16 rows >500KB). | Verify pg_stat_reset history (see Agent 8). If counters were reset during 2026-04-24 .NET 10 deploy, monitor for 7 days then revisit. The `ix_icumcontainerdata_declarationnumber` zero-scan is suspicious — that should be a hot path. | XS | Low |
| 7.16 | P2 | db: `nickscan_production` | **`ix_analysisassignments_tenant_id` (376 KB), `ix_imageanalysisdecisions_tenant_id` (112 KB), and 11 other `_tenant_id` indexes have idx_scan=0.** All multi-tenant indexes are unused — single-tenant production never filters by tenant_id directly (RLS pushes it as `app.tenant_id` setting, not WHERE clause). | `pg_stat_user_indexes WHERE idx_scan=0`: 13 distinct `*_tenant_id` indexes total. | Either drop them (single-tenant won't use them) or wait for multi-tenant rollout. Recommend keeping; the cost is modest and they're load-bearing for future. Document the situation. | XS | Low |
| 7.17 | P2 | db: `nickscan_production`, `nickscan_downloads` | **Dual `icumsdownloadqueue` tables — IDENTICAL schemas (16 columns each)** in two DBs. `production.icumsdownloadqueues` (plural) has `n_tup_ins=55, n_tup_upd=0, n_tup_del=0` (i.e. only inserts, no churn). `downloads.icumsdownloadqueue` (singular) has `n_tup_ins=414, n_tup_upd=180, n_tup_del=394` (active churn). | Probe pass 4: `pg_stat_user_tables` — see numbers. Pass 3: identical column lists. | One is dead. The production-side queue with no churn looks abandoned. Confirm with agent 05 (ICUMS), then drop the dead one. Naming `icumsdownloadqueue` (singular) vs `icumsdownloadqueues` (plural) is a footgun on its own. | S | Low |
| 7.18 | P3 | db: `nickscan_production` audit columns | **19 production tables lack `createdat`/`createdatutc` audit columns**. Includes `analysisqueueentries` (acceptable — materialized cache), `applicationlogs` (uses `timestamp`), `endpointusagelog` (uses `timestamp`), `permissionauditlogs` (likely uses `auditedat`), `userreadiness` (uses `lastheartbeat`). The remaining list: `auditlogs`, `icumsdownloadqueues`, `image_split_assignments`, `imagecaches`, `manifestsnapshots`, `originalscanrecords`, `recordexpectedcontainers`, `recordreconciliationstate`, `rolepermissions`, `settingshistory`, `shiftswaprequests`, `splitter_consensus_corpus`, `userpermissions`, `wavependingcontainers`. | Pass 1 query 8A: `SELECT t.relname FROM pg_class t WHERE NOT EXISTS (... attname IN ('createdat','createdatutc',...))`. | Audit per-table (most have a domain-specific timestamp). Document expected-vs-actual columns. The `recordreconciliationstate` and `imagecaches` lack are most concerning. | M | Low |
| 7.19 | P2 | db: `nickhr` audit columns | **7 `nickhr` Identity tables lack `createdat`** — but they're ASP.NET Identity tables, intended schema. `AuditLogs` uses `Timestamp`, `LoginAudits` uses `LoginTime`. Acceptable. | Pass 1 + Pass 3. | Document; no fix. Listed for completeness. | XS | Low |
| 7.20 | P2 | db: `nickhr` permissions | **NickHR.WebApp connects as `postgres` superuser** per topology agent's finding §I (Connection strings table). RLS bypass is automatic for superusers. `pg_roles` confirms: `postgres rolsuper=True rolbypassrls=True`. NickHR.WebApp queries to `nickhr` therefore see all rows regardless of `app.tenant_id`. Latent today (single-tenant), critical at multi-tenant. | `SELECT rolname, rolsuper, rolbypassrls FROM pg_roles` → postgres super=True, nscim_app super=False. | Migrate NickHR.WebApp to a non-super role (`nickhr_app`?) — re-grant DML on Identity tables, set ownership of the rest to postgres, validate startup. | M | High |
| 7.21 | P3 | db: `nickscan_production` | **Sites table is empty (0 rows)** despite Lanes/ScannerAssets entities depending on it. Scanner→port mapping is therefore done in C# (`ScannerLocationMap`) not in DB. Stale schema. | `SELECT id, name, code FROM sites` → 0 rows. | Consider dropping `sites`, `lanes`, `scannerassets` if not used (FK chain is pure dead weight) OR populate them and migrate ScannerLocationMap to read from DB. | M | Med |
| 7.22 | P3 | db: `nickscan_production.analysisgroups` | **28 AGs have NULL scannertype** (5 Cancelled, 19 Completed, 4 Ready). Wave-AGs use suffix `_W1` in groupidentifier; some appear scannertype-less. | `SELECT status, count(*) FROM analysisgroups WHERE scannertype IS NULL GROUP BY status` → Cancelled=5, Completed=19, Ready=4. | Either backfill from contained CCS rows or document that wave-rolled-up AGs deliberately skip scannertype. The 4 Ready ones are most operationally suspect. | XS | Low |
| 7.23 | P3 | db: `nickscan_production` | **Column-name drift: 60+ snake_case columns in `image_split_*`, `crossrecordscans`, `splitter_consensus_corpus`** (e.g. `container1_boe`, `claude_vision_confidence`, `split_x`). Rest of the schema is unquoted lowercase (Postgres-default). Two conventions co-exist. | Pass 3 query: ~61 rows from `image_split_jobs` + `image_split_results` + `crossrecordscans` + `splitter_consensus_corpus`. | EF + Postgres lowercase together work; snake-case explicit columns also work; the **risk** is C# code that types `CamelCase` and silently maps to lowercase but then a snake-case column gets renamed. Document the convention split. | XS | Low |
| 7.24 | P2 | db: `nickscan_downloads.icumsdownloadqueue` | **Lacks `createdat` column** (uses `queuedat` instead). Same in production-side icumsdownloadqueues — both use `queuedat`. Inconsistent with the rest of the schema. | Pass 4 query 8A on `nickscan_downloads`. | Document or rename. Medium-low risk. | XS | Low |
| 7.25 | P2 | db: cross-DB FK | **Cross-DB logical FK from `containerboerelations.icumsboeid` → `nickscan_downloads.boedocuments.id` had ZERO orphans at probe time** (3,054 active CBRs → 3,035 distinct icumsboeids, all 3,035 found). **CCS.boedocumentid → boedocuments.id**: 2,551 distinct values, 2,551 found, 0 orphans. Discipline is being held in app code despite no DB enforcement. | Pass-2 cross-DB probe. | None today, but fragile. The pattern is a 2-pass C# script in the probe — every read path that joins prod↔downloads is doing this in C# and getting it right. Document so future devs don't naively try a single SQL JOIN. | XS | Low |
| 7.26 | P3 | db: `nickscan_production.analysisgroups` | **`recordcompletenessstatusid` NULL on 691 of 2,678 AGs (26%)**. Wave-processing FK — possibly a per-grouptype thing or a backfill gap. | `SELECT count(*) FILTER (WHERE recordcompletenessstatusid IS NOT NULL) FROM analysisgroups` → 1987/2678. | Determine whether NULL is allowed for some GroupType. If not, backfill. | S | Low |
| 7.27 | P2 | db: cross-DB FK | **`containerboerelations` has a FK `containerboerelations.scannerdataid` (NOT NULL, integer)** that points at scanner-specific tables (`fs6000scans.id`, `asescans.id`, etc.) determined at runtime by `scannertype`. There's no DB-level check enforcing this; the int can dangle. | `information_schema.columns` shows `scannerdataid integer NOT NULL`; pass 3 FK list shows no FK constraint enforcing it. | Either polymorphic FK (CHECK constraint on scannertype + actual FK to subset table per scanner) or accept and document. Latent risk. | M | Low |
| 7.28 | P2 | db: `nickscan_production` migrations | **Migration history is from 2026-03 to 2026-04-23**. Last applied: `20260423180227_AddSplitJobIdToCrossRecordScans` (8.0.11). The rest of the rollout (2.15.x → 2.16.3) was schema-stable on the existing tables — no migrations applied. Per CLAUDE.md, the 2026-04-24 .NET 10 retarget didn't add migrations. **NickHR has 36 migrations, last `20260430120000_Add_Permissions_RolePermissions`** (10.0.4) — actively churning. **nickscan_icums has 1 migration only** (Initial), **nickscan_downloads has 2** (Initial + AddIngestionWarningsColumns). | Pass 2 + Pass 4 — `__EFMigrationsHistory` queries. | None — informational. Useful for Phase 3 to know what changes are queued vs applied. | XS | Low |
| 7.29 | P3 | db: `nickscan_production` | **`auditdecisions.iscompleted` boolean and `auditdecisions.completedat` overlap with state machine** in `auditdecisions.decision` + parent AG.status. Three sources of truth for completion state. | Pass 2: `auditdecisions cols` query. | Cross-check with agent 04 to determine canonical source. | XS | Low |

## Narrative

**Three findings dominate the profile:**

1. **Zero in-DB FK constraints on the cargo-pipeline core (7.01).** The
   schema is held together by application-level logical FKs only. The
   `analysisassignments → analysisgroups` relationship (the single most
   load-bearing FK in the entire system, queried on every operator
   heartbeat) has no enforcement. The 481 active-CBR-without-CCS rows
   (7.08) and the 392 imageanalysisdecisions-without-CCS rows (7.06) are
   exactly the kind of drift you'd expect from this gap. Adding FKs is
   the highest-leverage cleanup, but it requires the orphan-cleanup
   passes 7.06–7.09 first, otherwise the constraint creation will fail.

2. **`analysisqueueentries` has no RLS and no `tenant_id` (7.02).** The
   topology agent flagged this as the only operationally-active table
   in `nickscan_production` without RLS; my catalog scan confirms — only
   `__EFMigrationsHistory`, `analysisqueueentries`, and
   `splitter_consensus_corpus` have `relrowsecurity=False`. The other
   180 tables are `FORCE ROW LEVEL SECURITY` per memory
   `reference_rls_now_enforces.md`. The risk is latent (single-tenant
   prod today) but the table drives cross-tenant assignment eligibility.
   The fact that the entity class itself has no `TenantId` property
   (verified via `grep`) means the fix is a code change, not just a DDL.

3. **CCS denormalization is silently rotting (7.04).** 24 active CCS
   rows have `hasicumsdata=true` but NULL `boedocumentid` AND NULL
   `clearancetype`. The Export-Hold workflow stage is the worst-affected:
   13 of 13 rows have both NULLs. This is the same shape as the
   2026-05-04 mis-tagged-consolidated event; it's wider than that one
   incident. The denormalization scope is narrow (the CCS table only
   has `boedocumentid`, `clearancetype`, `groupidentifier` denorm
   fields — no `regimecode`, `declarationnumber`, `deliveryplace`),
   which means the dialog and matching pipeline still have to re-fetch
   from BOE for many cases. That's not bad design, just bounded.

**Operational observations worth dispatching to other agents:**

- `analysisassignments` has zero `Active` state rows (11,437 total:
  Expired=9,235, Released=2,089, Cancelled=113). Last 24h created 134
  assignments, all terminal. **Defer to agent 04** — the lease-expiry
  loop is faster than operator-claim. Suggests orchestrator timing bug.

- 7 Ready AGs with no AA at all (7.09). Combined with `AnalysisQueueEntries
  cardinality=0` from topology, the orchestrator's queue-materialization
  step is silently failing for some Ready AGs. **Defer to agent 04.**

- `nickscan_downloads.boedocuments`: 86,365 CMR-cohort rows with NULL
  `documenttype`/`regimecode`/`declarationnumber` (per memory, deliberate
  for CMRs). 16 IM-clearancetype rows missing `documenttype` — those
  are the bug. 354 NULL `deliveryplace` (Layer 1 port-rule defeaters)
  — defer to agent 03.

**Cross-DB logical-FK discipline is currently 100%** at probe time
(7.25). Both `containerboerelations.icumsboeid` and
`containercompletenessstatuses.boedocumentid` resolve cleanly to
`nickscan_downloads.boedocuments.id` with zero orphans. Discipline is
being held in app code despite no DB enforcement — but every cross-DB
read in the probes had to be written as a 2-pass C# join, which is a
fragile pattern.

**Index health** is good for hot paths. The 7 compound queue indexes
from the 2026-04-28 audit are all present (Pass 1 4B). The dead
indexes are mostly small, except for two 34 MB never-scanned
`endpointusagelog` indexes (7.14) and the
`ix_icumcontainerdata_declarationnumber` 1.4 MB never-scanned index
(7.15) — the latter is suspicious because that's a documented hot path.

**Migration coherence** (7.28): nickscan_production has 15 applied
migrations, last on 2026-04-23. nickhr has 36, last on 2026-04-30.
nickscan_downloads/nickscan_icums/nick_comms each have 1-2. Phase-1
multi-tenancy migrations were SQL files run manually (per the
phase1-tenancy/ folder), not EF migrations — they don't show up in
`__EFMigrationsHistory`. **`analysisqueueentries` and
`splitter_consensus_corpus` were both created after the manual
phase-1 RLS rollout and never tenanted** — that's the proximate cause
of 7.02 and 7.03.

## Open questions

1. **Should imageanalysisdecisions cascade-delete with CCS?** 7.06's
   392 orphans suggest CCS rows are being archived/deleted while
   decisions are kept — by accident or by design? If by design, what
   audit-trail field marks the CCS as no-longer-existent for the
   decision viewer? Fix path depends on this answer.

2. **Is `splitter_consensus_corpus` intentionally single-tenant?** The
   training-data pattern usually is, but inconsistent with phase-1
   policy. Confirm with the splitter integration owner before adding RLS.

3. **`icumsdownloadqueue` (singular, downloads) vs `icumsdownloadqueues`
   (plural, production)** — which is canonical? Pass-4 stats suggest
   the production-side is dead (55 inserts, 0 churn) and downloads-side
   is live (414/180/394). If confirmed, the production table can be
   dropped — but the table-name plural/singular split needs unifying.

4. **The 481 active-CBR-without-CCS (7.08)**: is this expected pending
   state (CCS materializes only after scanner data arrives) or is the
   ContainerCompletenessOrchestrator failing to bootstrap? Agent 04
   should clarify.

5. **NickHR.WebApp using `postgres` superuser (7.20)**: is this a known
   migration deferred for a reason (e.g. NickHR Identity tables need
   superuser for ALTER), or just a copy-paste from the 2026-03 setup
   when nscim_app didn't exist yet? Security agents should resolve.

## Probe artifacts

All evidence captured in:

- `C:\temp\nscim-probe\DbIntegrity.cs` (pass 1 — RLS coverage, tenant_id, PK, indexes, denorm gaps, state machines, audit cols, migration history, CCS schema, dup keys)
- `C:\temp\nscim-probe\DbIntegrity2.cs` (pass 2 — fixed column names; CBR/AA introspection; cross-DB orphan probe)
- `C:\temp\nscim-probe\DbIntegrity3.cs` (pass 3 — splitter_consensus, snake_case audit, FK constraints, sites/lanes, NickHR identity tables)
- `C:\temp\nscim-probe\DbIntegrity4.cs` (pass 4 — AA state distribution, migration timelines, dual-icumsdownloadqueue activity, BOE NULL coverage)
- Output: `dbintegrity-output.txt` (683 lines), `dbintegrity2-output.txt` (429 lines), `dbintegrity3-output.txt` (282 lines), `dbintegrity4-output.txt` (182 lines)

Total queries run: ~120 across 5 DBs as `nscim_app` (with RLS) +
`postgres` (system catalogs and bypass). All under explicit `SET LOCAL
app.tenant_id = '1'` for nscim_app reads. No writes performed.
