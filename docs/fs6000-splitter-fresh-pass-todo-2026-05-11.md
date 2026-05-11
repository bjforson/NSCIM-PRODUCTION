# FS6000 Splitter Fresh-Pass Todo - 2026-05-11

## Goal

Build a fresh FS6000-first split flow that does not assume two physical
containers just because the scanner/XML row lists two container numbers.

## Teams

### Team A - Python Visual Eligibility Gate

- Status: completed.
- Add an independent pixel-based classifier for FS6000 images.
- Return one of:
  - `dual_container`
  - `single_container`
  - `uncertain`
- Include confidence, reason codes, and candidate frame positions.
- Keep the classifier independent of existing splitter strategies.
- Wire point should be callable before `run_pipeline`.

### Team B - Runtime Status Handling

- Status: completed.
- Ensure `single_container` and `uncertain` do not produce Ready split
  assignments.
- Preserve analyst/audit visibility with a clear status and error/reason.
- Make .NET intake/linking interpret these outcomes safely.

### Team C - Audit/Replay Tooling

- Status: completed.
- Add read-only audit tooling for existing FS6000 `image_split_jobs`.
- Output JSON/CSV classification summaries.
- Identify contaminated completed jobs where visual eligibility is not
  `dual_container`.
- Require an explicit flag before any production DB mutation.

### Team D - Release/Verification

- Status: in progress.
- Run focused Python checks.
- Run .NET build.
- Update `Directory.Build.props`.
- Update `CHANGELOG.md`.
- Commit, push, deploy.

## Acceptance Criteria

- FS6000 visually single/uncertain images are not split into two assignments.
- Existing valid dual-container behavior is preserved.
- Audit tooling can list current affected FS6000 jobs without mutating data.
- The splitter health endpoint still reports healthy after deployment.
- Release notes document the eligibility gate and audit tooling.

## Verification Notes

- Known bad FS6000 job `c4bb9db5-b3ce-4622-bb5d-12be5784123f` now classifies
  as `uncertain` with `should_split=false`.
- Checked known good FS6000 dual-container examples still classify as
  `dual_container`.
- Read-only audit of the 25 most recent completed FS6000 splitter jobs found
  23 `dual_container`, 1 `single_container`, 1 `uncertain`, and 2 high-risk
  completed jobs that had already been split.
