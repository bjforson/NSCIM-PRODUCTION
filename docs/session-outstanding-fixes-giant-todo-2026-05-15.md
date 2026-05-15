# Session Outstanding Fixes Giant Todo - 2026-05-15

Master tracker for the 2026-05-15 NSCIM split-selection follow-up session.

This tracker is intentionally operational: it records gates, live evidence slots, rollback notes, and team ownership for the remaining items. Code changes are out of scope for this document. The split-selection code patch has already passed API/Web builds plus SourceScan-focused tests, but it is not deployed until the deploy gates below are completed and evidenced.

## Ownership And Editing Rules

- Tracker owner: session master tracker.
- File ownership for this session: only this file, `docs/session-outstanding-fixes-giant-todo-2026-05-15.md`.
- Code ownership: none in this tracker pass. Do not edit API, WebApp, migration, test, or script files from this session role.
- Parallel-worker rule: other workers may edit the codebase. Treat this document as the coordination point; do not revert or normalize unrelated changes.
- Evidence rule: paste concise command/result summaries, timestamps, screenshots/recording paths, issue links, or DB probe summaries into the evidence slots. Prefer real live-state evidence over build-only claims.
- Status vocabulary: `Not started`, `In progress`, `Blocked`, `Ready for verification`, `Verified`, `Rolled back`, `Deferred`.

## Current Snapshot

- Date: 2026-05-15.
- Repo: `C:\Shared\NSCIM_PRODUCTION`.
- Known completed before this tracker:
  - Split-selection code was patched.
  - API build passed.
  - Web build passed.
  - SourceScan tests passed.
- Known gap:
  - The split-selection fix is not deployed.
- Highest-risk sequencing:
  - Deploy API+Web first under a controlled rollout.
  - Verify live split selection and skip paths before declaring the patch complete.
  - Recover any stuck assignment/progression state after live behavior is proven.
  - Watch operational errors during and after rollout.
  - Separately handle EagleA25 sync-status missing table.
  - Reanalyze remaining CMR composite-key/completeness work after the above is stable.

## Execution Evidence - 2026-05-15 15:36 UTC

- Master tracker created: `docs/session-outstanding-fixes-giant-todo-2026-05-15.md`.
- Controlled deploy readiness plan created: `docs/split-selection-controlled-deploy-verification-plan-2026-05-15.md`.
- CMR point-7 reanalysis created: `docs/cmr-composite-key-remaining-work-reanalysis-2026-05-15.md`.
- EagleA25 guarded sync-status fix implemented and included in the API deploy package.
- Validation before deploy:
  - `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore --nologo`: passed.
  - `dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore --nologo`: passed.
  - `dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "FullyQualifiedName~SourceScan|FullyQualifiedName~EagleA25" --no-restore --logger "console;verbosity=minimal"`: passed, 17/17.
- Controlled deploy package:
  - Stage: `deploy-staging\split-selection-eaglea25-20260515-152544`.
  - Backup: `deploy-backups\split-selection-eaglea25-20260515-152544`.
  - Live copy used `robocopy /MIR /XF appsettings*.json /XD Logs Data`.
  - `API_ROBOCOPY=1`; `WEB_ROBOCOPY=1`.
  - `API_CONFIG_DIFF_COUNT=0`; `WEB_CONFIG_DIFF_COUNT=0`.
- Services after deploy:
  - `NSCIM_API`: Running from `C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe`.
  - `NSCIM_WebApp`: Running from `C:\Shared\NSCIM_PRODUCTION\publish\WebApp\NickScanWebApp.New.exe`.
- Health after deploy:
  - `http://localhost:5205/health`: 200.
  - `http://localhost:5205/health/live`: 200.
  - `http://localhost:5205/health/ready`: 200.
  - `https://localhost:5206/health`: 200.
  - `http://localhost:5299/api/server/health`: 200.
  - `https://localhost:5300/api/server/health`: 200.
  - `http://127.0.0.1:5320/api/health`: 200.
- Protected live probes after deploy:
  - `https://localhost:5206/api/EagleA25/sync-status`: 401 anonymous, proving route is live/protected and not connection-refused.
  - `https://localhost:5206/api/image-splitter/records/4812/split-options`: 401 anonymous, proving route is live/protected and not connection-refused.
  - `https://localhost:5206/api/scan-assets/4812/image`: 401 anonymous, proving route is live/protected and not connection-refused.
- Post-deploy ops watch:
  - Log scan after `2026-05-15T15:34:38Z`: no fresh matches for `EagleA25`, `eaglea25synclogs`, `scan-assets/4812/image`, `/api/scan-assets`, `/api/EagleA25`, `/api/image-splitter`, `42P01`, or `Unhandled exception`.
  - `NSCIM_API` still running, PID `48884`.
  - `NSCIM_WebApp` still running, PID `25800`.
- Note: authenticated choose/skip verification was not automated with the `superadmin` token helper because this codebase rotates the user session ID on login; using it can invalidate an active superadmin browser session. Use an already-authenticated operator browser session for the UI choose/skip proof unless explicitly approved.

## Team Labels

- `Team:Deploy` - controlled API/Web publish, Windows service checks, rollback readiness.
- `Team:QA` - browser/API verification, split selection, skip path, assignment progression.
- `Team:Ops` - logs, health, error watch, production incident tracking.
- `Team:DB` - schema/table checks, migrations, owner-level DDL, EagleA25 missing table.
- `Team:Completeness` - CMR composite-key and completeness reanalysis.
- `Team:Tracker` - this document only.

## Global Gates

| Gate | Required Before | Owner Label | Status | Evidence Slot |
| --- | --- | --- | --- | --- |
| G0: Code baseline identified | Any deploy | `Team:Deploy` | Not started | Branch/commit/SHA: TBD |
| G1: Builds/tests reconfirmed or accepted from current patch evidence | Publish | `Team:Deploy` | Not started | API build: TBD; Web build: TBD; SourceScan tests: TBD |
| G2: Production config preservation plan confirmed | Publish copy/restart | `Team:Deploy` | Not started | Confirmed files/backup path: TBD |
| G3: API service state and binary path captured before rollout | API publish | `Team:Deploy` | Not started | Service: TBD; PathName: TBD; binary timestamp before: TBD |
| G4: Web service/app state and binary/static asset path captured before rollout | Web publish | `Team:Deploy` | Not started | Service/app: TBD; path: TBD; timestamp before: TBD |
| G5: API+Web health endpoints pass after rollout | Functional verification | `Team:Deploy`, `Team:Ops` | Not started | HTTP/HTTPS health probes: TBD |
| G6: Live split selection verified | Assignment/progression recovery | `Team:QA` | Not started | Case/source scan IDs, screenshots, API responses: TBD |
| G7: Live skip path verified | Assignment/progression recovery | `Team:QA` | Not started | Case/source scan IDs, screenshots, API responses: TBD |
| G8: Error watch clean or triaged | Completion claim | `Team:Ops` | Not started | Log window, errors, triage notes: TBD |
| G9: EagleA25 sync-status table gap classified | EagleA25 closure | `Team:DB` | Not started | Missing table name, failing endpoint/error, migration plan: TBD |
| G10: CMR/completeness reanalysis refreshed against current tree | Completeness plan update | `Team:Completeness` | Not started | Findings link/summary: TBD |

## 1. Controlled API + Web Deploy

- Owner labels: `Team:Deploy`, `Team:Ops`.
- Priority: P0.
- Status: Not started.
- Scope: publish the already-patched API and WebApp in a controlled way, preserving production configuration and recording proof that live services are running the new bits.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Pre-deploy source SHA captured | Not started | TBD |
| API/Web build evidence attached or rerun accepted | Not started | TBD |
| SourceScan test evidence attached or rerun accepted | Not started | TBD |
| Production appsettings/config preservation confirmed | Not started | TBD |
| API publish staged | Not started | TBD |
| Web publish staged | Not started | TBD |
| API service restarted and running | Not started | TBD |
| Web service/app restarted and running | Not started | TBD |
| API health endpoints pass | Not started | TBD |
| Web entrypoint loads | Not started | TBD |

### Verification Evidence To Capture

- Source branch/commit/SHA deployed: TBD.
- API service name and state: TBD.
- API executable path: TBD.
- API binary timestamp before/after: TBD.
- Web service/app name and state: TBD.
- Web deployed path and timestamp before/after: TBD.
- Health probes:
  - `http://localhost:5205/health`: TBD.
  - `https://localhost:5206/health`: TBD.
  - Web local/host URL: TBD.
- Config preservation proof:
  - API `appsettings*.json` preserved: TBD.
  - Web configuration preserved: TBD.
- Operator-visible smoke check: TBD.

### Rollback Notes

- Preserve pre-deploy binaries and configuration before overwrite.
- If API health fails after restart, first distinguish restart-window connection refusals from persistent failure.
- If config is overwritten or missing, restore preserved production config before debugging application logic.
- Roll back API and Web together if the split-selection live flow regresses due to contract mismatch.
- Capture rollback command, timestamp, and post-rollback health evidence here: TBD.

## 2. Live Split Selection Verification

- Owner labels: `Team:QA`, `Team:Ops`.
- Priority: P0.
- Status: Not started.
- Scope: prove the patched split-selection behavior works in the normal deployed UI/API, not just in local builds or tests.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Deployed API+Web version confirmed | Not started | Depends on item 1 |
| Test case/source scan identified | Not started | TBD |
| Split selection UI opens through normal operator path | Not started | TBD |
| Selecting a split result persists the intended selection | Not started | TBD |
| Follow-on page/state reflects the chosen split | Not started | TBD |
| No unexpected 4xx/5xx during the flow | Not started | TBD |

### Verification Evidence To Capture

- Operator route used: TBD.
- Case/container/source scan IDs: TBD.
- Selected split result ID(s): TBD.
- Before state summary: TBD.
- Action taken in UI: TBD.
- API requests/responses or network trace summary: TBD.
- After state summary: TBD.
- Screenshot/recording/log reference: TBD.

### Rollback Notes

- If the UI shows a wrong or stale split result, stop assignment/progression recovery and preserve the failing case for debugging.
- If persistence fails but UI selection appears successful, capture the request payload and server response before retrying.
- If the deployed flow fails while local tests passed, treat as deploy/config/API routing issue until live endpoints and assets are proven aligned.

## 3. Live Skip Path Verification

- Owner labels: `Team:QA`, `Team:Ops`.
- Priority: P0.
- Status: Not started.
- Scope: prove the skip/no-selection path remains functional after deployment and does not trap operators or corrupt assignment state.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Deployed API+Web version confirmed | Not started | Depends on item 1 |
| Skip-eligible case/source scan identified | Not started | TBD |
| Skip action is visible and usable in normal UI | Not started | TBD |
| Skip result persists correctly | Not started | TBD |
| Queue/progression moves to the expected next state | Not started | TBD |
| No unexpected 4xx/5xx during the skip flow | Not started | TBD |

### Verification Evidence To Capture

- Operator route used: TBD.
- Case/container/source scan IDs: TBD.
- Skip reason/status chosen, if applicable: TBD.
- Before assignment/progression state: TBD.
- After assignment/progression state: TBD.
- API requests/responses or network trace summary: TBD.
- Screenshot/recording/log reference: TBD.

### Rollback Notes

- If skip leaves the assignment stuck, do not bulk-repair until item 4 has a targeted recovery plan.
- If skip records an incorrect terminal state, capture the affected row IDs and stop additional skip testing.
- If skip only fails for one scanner/data source, label that evidence clearly so recovery can be scanner-scoped.

## 4. Assignment / Progression Recovery

- Owner labels: `Team:QA`, `Team:DB`, `Team:Ops`.
- Priority: P1 after items 2 and 3 pass.
- Status: Not started.
- Scope: identify and recover any assignments, queues, or progression records left stuck by the pre-deploy issue or by failed verification attempts.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Live split selection verified | Not started | Depends on item 2 |
| Live skip path verified | Not started | Depends on item 3 |
| Stuck assignment inventory query defined | Not started | TBD |
| Impacted rows exported/snapshotted before changes | Not started | TBD |
| Recovery action reviewed by DB/Ops | Not started | TBD |
| Recovery executed in smallest safe batch | Not started | TBD |
| Queue/progression state verified after recovery | Not started | TBD |

### Verification Evidence To Capture

- Query or report used to identify stuck records: TBD.
- Count of impacted assignments before recovery: TBD.
- Sample affected IDs: TBD.
- Recovery method: TBD.
- Count recovered: TBD.
- Count still blocked and reason: TBD.
- Post-recovery operator/API validation: TBD.

### Rollback Notes

- Snapshot impacted rows before mutation.
- Prefer targeted row recovery over broad status rewrites.
- Keep exact row IDs in the tracker or linked evidence so any bad recovery can be reversed.
- If recovery depends on business-state interpretation, pause for owner approval before DB mutation.

## 5. Ops Error Watch

- Owner labels: `Team:Ops`, `Team:Deploy`.
- Priority: P0 during deploy, P1 post-deploy.
- Status: Not started.
- Scope: watch runtime logs, health, and operator-facing errors during rollout and verification.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Watch window started before service restart | Not started | TBD |
| API logs checked during restart window | Not started | TBD |
| Web logs checked during restart window | Not started | TBD |
| Split-selection verification window checked | Not started | TBD |
| Skip-path verification window checked | Not started | TBD |
| Post-deploy quiet window completed | Not started | TBD |
| New errors classified as blocker/non-blocker | Not started | TBD |

### Verification Evidence To Capture

- Watch start/end timestamps: TBD.
- API log source/path: TBD.
- Web log source/path: TBD.
- Error count in deploy window: TBD.
- Error count in verification window: TBD.
- New error signatures: TBD.
- Action taken for each new error: TBD.

### Rollback Notes

- Roll back if new errors indicate data corruption, repeated 500s in split-selection routes, auth/session breakage across normal operator routes, or health endpoints remain degraded after restart stabilization.
- Do not roll back for isolated connection refusals during the planned restart window unless they persist.
- Preserve logs from both failing and post-rollback windows.

## 6. EagleA25 Sync-Status Missing Table

- Owner labels: `Team:DB`, `Team:Ops`.
- Priority: P1 unless it blocks deploy health or operator verification.
- Status: Not started.
- Scope: classify and fix the missing-table failure behind EagleA25 sync-status without mixing it into the split-selection rollout unless it blocks live verification.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Failing endpoint/error captured | Not started | TBD |
| Missing table name confirmed | Not started | TBD |
| Expected EF entity/migration path identified | Not started | TBD |
| Existing production schema checked | Not started | TBD |
| DDL owner requirements confirmed | Not started | TBD |
| Fix path chosen: migration, manual DDL, or deferred issue | Not started | TBD |
| Post-fix `sync-status` verified | Not started | TBD |

### Verification Evidence To Capture

- Endpoint checked, likely `GET /api/EagleA25/sync-status`: TBD.
- Exact exception/error text: TBD.
- Missing relation/table name: TBD.
- DB/schema checked: TBD.
- Migration or DDL reference: TBD.
- Owner-level account/role used for schema change, if any: TBD.
- Post-fix response summary: TBD.

### Rollback Notes

- If DDL is applied, record exact migration/DDL and a reverse plan.
- Use owner-level DB credentials for ownership-protected schema work when required.
- Keep EagleA25 fixes isolated from split-selection deployment unless a shared migration bundle makes that impossible.
- If deferred, create/link the follow-up issue and record why it does not block split-selection closure.

## 7. Remaining CMR Composite-Key / Completeness Reanalysis

- Owner labels: `Team:Completeness`, `Team:DB`, `Team:QA`.
- Priority: P2 after rollout stabilization, or P1 if verification exposes completeness/progression inconsistencies.
- Status: Not started.
- Scope: refresh the analysis of remaining CMR composite-key/completeness work against the current tree and live behavior. Prior completeness findings should be treated as clues, not current proof.

### Reanalysis Targets

- CMR composite-key expectations and actual DB uniqueness/lookup behavior.
- Container completeness decisions that depend on CMR pending/present state.
- Queue completion versus save ordering.
- Reconciliation paths that can mark a container complete based on BOE presence alone.
- Direct test coverage around the core completeness decision method.
- Any split-selection or assignment/progression data that feeds completeness state.

### Gates

| Gate | Status | Evidence Slot |
| --- | --- | --- |
| Current code inventory captured | Not started | TBD |
| Current DB/index/key shape checked | Not started | TBD |
| Current tests identified and run or risk-noted | Not started | TBD |
| CMR pending guard parity rechecked | Not started | TBD |
| Reconciliation early-complete risk rechecked | Not started | TBD |
| Implementation plan updated with current findings | Not started | TBD |

### Verification Evidence To Capture

- Files/services reviewed: TBD.
- DB tables/indexes/constraints checked: TBD.
- Tests run and result summary: TBD.
- Current gaps confirmed: TBD.
- Gaps no longer present: TBD.
- New gaps found: TBD.
- Recommended next implementation slice: TBD.

### Rollback Notes

- This is an analysis/planning item until explicitly approved for code/DB changes.
- Any eventual composite-key migration needs a data audit and duplicate-handling plan before DDL.
- Do not couple CMR/completeness changes to the split-selection deploy unless live verification proves a direct blocker.

## Completion Definition

The session can be called complete only when:

- Items 1 through 5 are `Verified` or have explicit accepted exceptions.
- Item 6 is `Verified` or explicitly deferred with a linked owner and non-blocking rationale.
- Item 7 has a refreshed reanalysis result and recommended next step.
- All evidence slots needed for the final claim are filled with live-state proof, not only local build output.
- Rollback status is clear: either no rollback needed, rollback completed, or rollback plan remains available until post-deploy quiet window ends.

## Final Session Notes

- Overall status: Not started.
- Main blocker: split-selection patch is not deployed.
- Next operational action: run item 1 controlled API+Web deploy, then immediately perform items 2, 3, and 5 in the live environment.
- Last tracker update: 2026-05-15 initial creation.
