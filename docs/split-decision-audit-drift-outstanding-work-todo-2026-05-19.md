# Split Decision Audit Drift Outstanding Work Todo - 2026-05-19

Scope: image-analysis decision saving through audit for split/multi-container records.

## Confirmed Issues

- [x] Image-analysis save only blocked `Ready` split records with no `SplitResultId`; other unresolved split states could still save.
- [x] Deprecated bulk analyst save could promote groups with unresolved split records.
- [x] Decision Agent could create completed decisions without split-safe lineage.
- [x] Decision side effects and group progression could count stale split decisions.
- [x] Audit list/detail/submit could select container/scanner decisions without split-compatible group lineage.
- [x] Audit re-submit did not refresh `ImageAnalysisDecisionId`.
- [x] Audit child rows could resolve parent audit rows too broadly.
- [x] Frontend decision/tag loads were not consistently group/scanner scoped.
- [x] Live data contains unresolved split records with completed decisions and historical audit rows.
- [x] Live data contains duplicate completed decision rows where unreferenced duplicates can be removed safely.

## Code Fixes

- [x] Add shared split decision eligibility contract.
- [x] Block unresolved split decisions in primary image-analysis save.
- [x] Block unresolved split decisions in deprecated analyst bulk save.
- [x] Prevent Decision Agent auto-decisions on unresolved split records.
- [x] Prevent decision side effects from advancing unresolved split records.
- [x] Prevent group progression from counting split-incompatible decisions.
- [x] Scope frontend decision loads by group/scanner where workflow context exists.
- [x] Scope audit selection/display/submit by group, scanner, and split lineage.
- [x] Refresh audit `ImageAnalysisDecisionId` on re-submit.
- [x] Add regression tests for split eligibility and guardrail coverage.

## Data Repair

- [x] Create transactional repair script with dry-run mode.
- [x] Back up every touched row to `maintenance.split_decision_audit_drift_repair_audit`.
- [x] Dry-run active/non-terminal repair scope.
- [x] Dry-run full active + terminal repair scope.
- [x] Apply repair after code guardrails are live.
- [x] Verify unresolved split drift is zero after repair.
- [x] Verify duplicate unreferenced completed decisions are zero after repair.

## Deployment

- [x] Agent reviewed publish path and confirmed staged cutover is safer than direct `Deploy.ps1` publish.
- [x] Commit coherent code + repair script.
- [x] Publish API/WebApp to staging folders.
- [x] Back up live API/WebApp publish folders.
- [x] Overlay staged publish output and restore live `appsettings*.json` from backup.
- [x] Restart with `Deploy.ps1 -SkipBuild`.
- [x] Verify service paths, DLL hashes, health endpoints, and event logs.
- [x] Push `main` after live verification is clean.

## Live Verification

- [x] `NSCIM_API` health returns `200`.
- [x] `NSCIM_WebApp` health returns `200`.
- [x] Running processes point at `C:\Shared\NSCIM_PRODUCTION\publish\API` and `...\publish\WebApp`.
- [x] Live DLL hashes match staged publish DLLs.
- [x] No fresh application errors after cutover.
- [x] Live DB split drift query returns no unresolved split completed decisions.
- [x] Live DB audit split drift query returns no unresolved split audit rows.
