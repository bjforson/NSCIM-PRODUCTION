# NSCIM v1 — End-to-End Cold Audit (2026-05-05)

## Goal

Produce an exhaustive, evidence-based inventory of issues across the NSCIM v1
production stack. Agents audit cold — they do **NOT** target any specific
user-reported symptom. Findings surface naturally; symptom coverage is
validated in Phase 3 *as a check on the audit*, not as a driver of it.

## Hard constraints (apply to every agent)

- **Read-only.** No code edits, no DB writes, no service restarts, no commits, no `Write`/`Edit` of source files. Probe scripts may be added under `C:\temp\nscim-probe\` only.
- **v1 only.** Tree at `C:\Shared\NSCIM_PRODUCTION\`. Do not edit, audit, or diff against `C:\Shared\ERP V2\` or `C:\NICK ERP\`.
- **NickHR / NickFinance / NickComms internals are out of scope.** Boundary calls into the cargo pipeline (e.g. NickComms.Gateway forwarding scan webhooks; NickHR shift-attendance gating analyst availability) are in scope — surface them, don't audit the modules themselves.
- **Every claim needs evidence.** file:line, query result, log excerpt, schema dump, NSSM/sc.exe output. No "I think" findings.
- **Tenant isolation:** any DB probe runs `SET LOCAL app.tenant_id = '1'` first. RLS fails closed; without it queries silently return empty.
- **Recent code is not sacred.** Today's commits and the last 30 days of churn must be evaluated at the same scrutiny level as old code. *Recent ≠ correct.*

## Output format

Each agent writes to `C:\Shared\NSCIM_PRODUCTION\docs\audit\2026-05-05\0X-{name}.md`.

### Required sections

1. **Scope confirmation** — one paragraph stating what was covered and what was deliberately not.
2. **Findings table** — structured rows. ID format: `XN.NN` where `X` is the agent number.
3. **Narrative** — short prose contextualizing the most important findings. Cap **800 words**.
4. **Open questions** — ambiguities the auditor couldn't resolve without further probing. These become Phase 3 follow-ups.

### Findings table columns

| Column | Description |
|---|---|
| ID | Agent.serial, e.g. `4.07` |
| Severity | P0 / P1 / P2 / P3 (definitions below) |
| File:Line | Specific code or query reference (or `runtime` / `db` / `config`) |
| Issue | One-line summary |
| Evidence | Concrete proof (excerpt, query result, log line) |
| Proposed Fix | Minimum-diff recommendation |
| Effort | XS (≤30 min) / S (½ day) / M (1–2 days) / L (≥3 days) |
| Risk of Fix | Low / Med / High (touching auth = High; touching ingestion = Med; logging = Low) |

### Severity definitions

- **P0**: Active production incident. Data loss, security breach, or total feature breakage. Fix today.
- **P1**: Real bug actively affecting users or data. Fix this week.
- **P2**: Latent bug, design smell, perf risk, missing observability. Fix this quarter.
- **P3**: Improvement / minor noise / nit. Backlog.

Don't game the severities — most findings are P2/P3, that's fine. P0/P1 should be sparse and obvious.

### Word budget

Findings table: unbounded (one row per real issue).
Narrative: ≤800 words.
Open questions: ≤300 words.

Goal is forced prioritization, not artificial brevity.

## Recent context

Heavy iteration in the last 14 days:

- Match-correctness arc (2.16.0): 6-layer rule model, mass unmatching of bad CBRs, documenttype tagging
- Orchestrator orphan-AG guard (2.16.1) — new `Cancelled` status for AGs whose every container has no boedocumentid + no active CBR
- Cargo-group `IsConsolidated` filter relaxation (2.16.0/.2/.3) — in stages, with backfill
- ReadyGroupsCacheService refined — only mark consolidated when MasterBlNumber is non-null (2.16.3)
- Deploy.ps1 ergonomics + SCM dependency drops on `NSCIM_WebApp` and `NickHR_WebApp`

Read `CHANGELOG.md` for the canonical record. The 2.16.x entries cover what was just changed and why.

## Memory references

The harness keeps notes at:

- `C:\Users\Administrator\.claude\projects\C--Users-Administrator-Documents-GitHub-NICKSCAN-CENTRAL--IMAGE-PORTAL\memory\MEMORY.md` — index
- Topical files (read what's relevant to your scope):
  - `reference_nickerp_topology.md` — services, ports, DB ownership
  - `reference_rls_now_enforces.md` — tenant isolation behavior
  - `reference_single_session_gotchas.md` — JWT sid + IMemoryCache + NickHR JWT key
  - `reference_port_match_rules_enabled_2026_05_02.md` — match-correctness rules
  - `reference_audit_2026_04_28.md` — last security/perf audit
  - `reference_week1_security_deployed.md` — phase-1 security state
  - `reference_pg_lowercase_columns.md` — Postgres column-name convention
  - `reference_v1_v2_separation.md` — v1/v2 boundary
  - `reference_service_binpaths_and_deploy.md` — service install paths

## Probe environment

`C:\temp\nscim-probe\` — Npgsql C# probe. `dotnet run -- <subcommand>` from there.

- `nscim_app` role + env `NICKSCAN_DB_PASSWORD` for SELECT/UPDATE on app data
- `postgres` role + env `NICKHR_DB_PASSWORD` for DDL or system catalogs
- Always `SET LOCAL app.tenant_id = '1'` first
- All Postgres column names lowercase (`createdat` not `created_at`)
- BOE table is `boedocuments` in `nickscan_downloads`; cross-DB joins not supported in vanilla PG — use 2-pass C# scripts

Existing probes for reference:
- `OrphanAgSweep.cs` — orphan AG detection + cleanup pattern
- `BlastRadius*.cs` — population analysis for the IsConsolidated bug
- `Health.cs` — DB state probe template

You may add new probe files (read-only by default — keep writes off unless you're deliberately reproducing a write path with rollback).

## Agent registry

| # | Agent | Output file | Depth |
|---|---|---|---|
| 1 | Topology | `01-topology.md` | Exhaustive |
| 2 | Scanner intake → CCS | `02-intake.md` | Exhaustive |
| 3 | Match-correctness pipeline | `03-match-correctness.md` | Exhaustive |
| 4 | Assignment pipeline | `04-assignment.md` | Exhaustive |
| 5 | ICUMS ingestion + queues + submission | `05-icums.md` | Exhaustive |
| 6 | Frontend operations | `06-frontend.md` | Exhaustive |
| 7 | DB schema + RLS + data integrity | `07-db-integrity.md` | Exhaustive |
| 8 | Observability + logging | `08-observability.md` | Exhaustive |

## Phase 3

After all 8 reports land, the orchestrator (the calling Claude) consolidates into `REPORT.md`:

- Dedup across agents
- Severity-sort
- Dependency graph (which fixes unblock which)
- Cross-check known symptoms (assignments not showing, MasterBlNumber, orphan AGs, etc.) — *validation*, not *input*

The result drives a separate fix-it sprint.

## Discipline reminders

- Don't stop at first hypothesis — disprove rivals.
- Distinguish *bug* (wrong behavior) from *design smell* (working but fragile) from *improvement* (works fine, could be cleaner).
- If a test or specific repro probe would establish a finding more concretely, propose it but don't run it (Phase 3 will).
- If you find a finding that's blocking production *right now*, mark P0 and surface it in your narrative explicitly — don't bury it.

## Authority

This charter supersedes individual agent prompts in case of conflict. If your prompt contradicts the charter, the charter wins. If you can't follow the charter, say so in your output's open-questions section and stop rather than improvise.
