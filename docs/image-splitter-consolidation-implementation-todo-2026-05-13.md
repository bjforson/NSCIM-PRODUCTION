# Image Splitter Consolidation Implementation TODO

Date: 2026-05-13
Owner: Master Tracker
Program status: implementation_review
Branch context: `codex-local-vision-training-20260511`

## Purpose

This tracker consolidates the image splitter operation into one coherent
production workflow.

The target outcome is:

1. Split Review becomes the canonical review console for two-container split
   quality, not just a loose QA page.
2. Analyst decisions become durable ground truth that can drive dataset export,
   model training, shadow evaluation, and later controlled promotion.
3. Negative labels are captured explicitly, including single-container images,
   bad images, scanner decode failures, duplicates, and uncertain cases.
4. The live splitter remains deterministic and reversible until the local model
   passes scanner-specific promotion gates.
5. Queue state, review progress, model readiness, and deployment health are
   visible to operators.

## Current Operating Gap

- Split Review currently functions mainly as a human labeling and QA workbench.
- Analyst approval is persisted in the splitter service, but the approved split
  is not yet the single canonical production action across every downstream
  record and assignment path.
- The image analyst flow and Split Review flow still have separate approval
  paths.
- Analyst feedback is exportable for training, but it does not currently retrain
  a model or improve the live splitter automatically.
- Negative examples exist conceptually, but the training loop needs a stronger
  operational path for single-container, bad-image, uncertain, duplicate, and
  scanner-specific rejection labels.
- Local model tables and evaluation concepts exist, but the local model training,
  shadow inference, promotion, rollback, and monitoring lifecycle still need to
  be implemented end to end.

## Status Legend

- `[ ]` Not started
- `[in_progress]` Active implementation
- `[blocked]` Waiting on decision or dependency
- `[review]` Ready for review
- `[shadow]` Implemented but not driving production
- `[x]` Done

## Non-Negotiable Guardrails

- [ ] Keep analyst decisions as the source of truth until model promotion gates
      are met.
- [ ] Do not place Codex itself in the production request path.
- [ ] Keep the current deterministic splitter as a fallback.
- [ ] Never auto-advance a split into audit from an unreviewed or invalid split
      unless an explicitly promoted model policy allows it.
- [ ] Preserve a hard "single container / do not split" path.
- [ ] Track ASE and FS6000 metrics separately.
- [ ] Store scanner type, model/provider version, model artifact hash, prompt
      version where applicable, and runtime metadata with every prediction.
- [ ] Make model prediction tables append-only where practical.
- [ ] Keep rollback procedures documented before enabling production model
      behavior.
- [ ] Do not overwrite unrelated code changes from parallel teams.

## Master Tracker Responsibilities

- [in_progress] Maintain this TODO as the single program tracker.
- [in_progress] Keep team statuses current as API/DB, UX, Training/Model,
      Ops/Evaluation, and QA/Deploy teams land changes.
- [ ] Add links to commits, PRs, builds, deployments, and verification evidence.
- [ ] Track cross-team dependencies and blockers.
- [ ] Ensure release notes and version bumps reference the consolidated splitter
      operation.
- [ ] Prevent scope drift between "review labeling", "production split choice",
      "model training", and "model promotion".

## Team Launch Board

| Team | Status | Primary Outcome |
| --- | --- | --- |
| API/DB | review | Split Review approvals now synchronize portal split state |
| Split Review UX | review | Console shows queue/label/training context and direct labels |
| Training/Model | shadow | Local baseline training scaffold added; no production decisions |
| Ops/Evaluation | review | Read-only operational report and runbook added |
| QA/Deploy | in_progress | Focused builds passed; commit/deploy validation pending |

## 2026-05-13 Integration Update

- [x] Created this master tracker.
- [x] Added `GET /api/image-splitter/jobs/review-summary` for queue,
      label, scanner, and portal-link metrics.
- [x] Made Split Review approval operational by synchronizing matching
      `AnalysisRecord` rows to `SplitStatus=Chosen` and the selected
      `SplitResultId` after the splitter accepts the approval.
- [x] Mapped Split Review negative labels into portal split states:
      `single_container -> VisualSingle`, `bad_image -> NotApplicable`,
      `uncertain -> Uncertain`.
- [x] Updated the review page into a Splitter Review Console with queue,
      label, training-readiness context, direct candidate save buttons, and
      direct negative label actions.
- [shadow] Added a local baseline training/evaluation/prediction scaffold under
      `services/image-splitter/ml/`; it reads exported manifests and writes
      shadow artifacts only.
- [x] Added read-only operational reporting in
      `services/image-splitter/tools/splitter_operational_report.py`.
- [x] Added an operations runbook for report use and readiness warnings.
- [x] Focused API and WebApp Release builds passed with existing warnings.
- [ ] Full clean release build from committed worktree.
- [ ] Deploy API/WebApp and smoke test live endpoints.

## Phase 0: Shared Baseline And Decisions

- [in_progress] Confirm the current schema for `image_split_jobs`,
      `image_split_results`, remote vision runs, local model prediction runs,
      and any consensus/training corpus tables.
- [in_progress] Refresh live counts for:
  - completed unreviewed split jobs
  - reviewed approved jobs
  - reviewed rejected jobs
  - manually corrected jobs
  - single-container negative labels
  - bad-image negative labels
  - uncertain labels
  - ASE labels
  - FS6000 labels
  - jobs with missing original image bytes
  - jobs with missing crop/result image bytes
  - pending, processing, completed, failed jobs
- [ ] Confirm whether Split Review approval should immediately update the same
      production record fields as the image analyst split-choice dialog.
- [ ] Confirm whether reviewed split approvals should automatically unblock audit
      progression or only mark training/QA labels.
- [ ] Confirm whether external AI services are allowed for scanner images.
- [ ] Confirm who can approve, reject, manually correct, export datasets, train
      models, run backfills, and promote models.
- [ ] Define a minimum labeled dataset threshold by scanner before local model
      training is considered meaningful.
- [ ] Define scanner-specific promotion gates for ASE and FS6000.

Exit criteria:

- [ ] Written decision on Split Review as canonical approval console.
- [ ] Written decision on audit progression behavior.
- [ ] Written decision on external AI policy.
- [ ] Baseline counts added to this tracker or linked report.

## Team A: API/DB

Status: in_progress

Goal: make splitter decisions durable, auditable, and usable by both production
workflow and model training.

### A1: Canonical Approval Contract

- [in_progress] Inventory every current approval endpoint and caller:
  - Split Review approval endpoint
  - Split Review reject/label endpoint
  - image analyst choose-split endpoint
  - manual split correction endpoint
  - any intake/backfill path that can set split status
- [ ] Define one canonical command for "reviewed split decision".
- [ ] Ensure the command supports:
  - approved candidate
  - approved manual split
  - rejected as single container
  - rejected as bad image
  - rejected as scanner decode failure
  - rejected as duplicate
  - uncertain
  - defer/requeue
- [ ] Add idempotency rules so repeated approval clicks do not create divergent
      state.
- [ ] Ensure the canonical command writes both splitter review labels and the
      required production assignment/record state when policy says it should.
- [ ] Ensure left/right container assignment is stored explicitly.
- [ ] Ensure split X and normalized split X are both stored or derivable.
- [ ] Ensure reviewer identity and timestamp are stored.
- [ ] Ensure scanner type is stored on the decision record or joinable without
      ambiguity.

### A2: Label Schema And Data Integrity

- [ ] Finalize label enum values:
  - `approved_candidate`
  - `approved_manual`
  - `single_container`
  - `bad_image`
  - `scanner_decode_failure`
  - `duplicate`
  - `uncertain`
  - `deferred`
- [ ] Add schema version to exported labels.
- [ ] Add label source:
  - analyst
  - reviewer
  - backfill
  - deterministic_strategy
  - remote_teacher
  - local_model_shadow
- [ ] Add label confidence where relevant.
- [ ] Preserve original candidate ranks and strategy names.
- [ ] Preserve rejected candidates as negative candidate outcomes when a better
      candidate or manual split is chosen.
- [ ] Add DB constraints or service-level guards so `approved` requires a valid
      result or manual split coordinate.
- [ ] Add DB constraints or service-level guards so `single_container` cannot
      also be treated as a successful two-container split.
- [ ] Add indexes for review queue queries:
  - scanner type
  - status
  - analyst verdict
  - reviewed at
  - created at
  - image hash
  - container pair
- [ ] Add migration-safe startup checks for missing tables or columns.

### A3: Production Record Synchronization

- [ ] Map how `AnalysisRecord.SplitResultId`, split status, audit readiness,
      assignments, and container sides are updated today.
- [ ] Decide whether Split Review approval should call the same service method
      used by the image analyst split-choice dialog.
- [ ] Implement a shared domain service if the logic is duplicated.
- [ ] Add guardrails for already audited or already saved records.
- [ ] Add guardrails for records whose container pair changed after splitter job
      creation.
- [ ] Add reconciliation for labels that exist in the splitter DB but have not
      been reflected in production record state.
- [ ] Add a read-only audit command before any production backfill mutation.
- [ ] Add a dry-run mode for synchronization/backfill.

### A4: API Acceptance Checks

- [ ] Unit tests for every decision type.
- [ ] Integration tests for approve candidate -> production record updated.
- [ ] Integration tests for approve manual -> production record updated.
- [ ] Integration tests for single container -> no split assignment created.
- [ ] Integration tests for duplicate click/idempotency.
- [ ] Integration tests for stale job and missing crop handling.
- [ ] Integration tests for ASE and FS6000 scanner-specific behavior.

## Team B: Split Review UX

Status: in_progress

Goal: make Split Review the fast, clear, operator-friendly console for reviewing
split results and feeding training data.

### B1: Review Flow

- [in_progress] Keep approve actions close to the candidate being reviewed.
- [in_progress] Keep container side confirmation above or adjacent to the split
      crops before approval.
- [ ] Show the actual left and right split crops at full useful size.
- [ ] Avoid relying only on a tiny original image with a line overlay.
- [ ] Provide a quick way to zoom or open a full-size crop without losing queue
      position.
- [ ] Preserve keyboard-friendly approve, reject, swap, and next actions.
- [ ] Use the same "load next" behavior as image/audit assignment flows.
- [ ] After approve/reject/manual label, automatically advance to the next
      reviewable job and keep the page responsive.
- [ ] Keep a visible "remaining in queue" count and current scanner filter.
- [ ] Show current job age and scanner type.
- [ ] Show whether the job is training-only, production-impacting, or both.
- [ ] Clearly separate production approval actions from training label actions
      if policy keeps them distinct.

### B2: Negative Labeling UX

- [ ] Provide one-click negative labels:
  - single container
  - bad image
  - scanner decode failure
  - duplicate
  - uncertain
- [ ] Require optional notes only where useful, not for every routine action.
- [ ] Add a visible reason summary after reject/label.
- [ ] Allow correction from negative label back to approved split if a reviewer
      made a mistake.
- [ ] Add confirmation only for destructive or production-impacting changes.
- [ ] Show reviewer history for the current job if available.

### B3: Queue And Filters

- [ ] Add filters by scanner:
  - all
  - ASE
  - FS6000
- [ ] Add filters by label status:
  - unreviewed
  - approved
  - rejected
  - uncertain
  - manual correction
- [ ] Add filters by production state:
  - not synchronized
  - synchronized
  - blocked
  - audit-ready
- [ ] Add filters by training state:
  - exportable
  - needs negative label
  - needs manual correction
  - excluded
- [ ] Add pagination or virtualized loading so reviewers can process more than
      the first page without hidden backlog confusion.
- [ ] Add queue totals returned from the API, not inferred from the currently
      loaded page.

### B4: UX Acceptance Checks

- [ ] Reviewer can approve candidate A or B in one click after side confirmation.
- [ ] Reviewer can view both split crop images large enough to judge quality.
- [ ] Reviewer can reject as single container without selecting a split.
- [ ] Reviewer can process the next job without returning to a separate list.
- [ ] Queue count decreases or refreshes visibly after each decision.
- [ ] The UI handles missing original image bytes gracefully.
- [ ] The UI handles missing crop bytes gracefully.
- [ ] The UI handles stale job state gracefully.

## Team C: Training/Model

Status: in_progress

Goal: turn analyst feedback into a local model lifecycle without making the
model production-authoritative before it earns that role.

### C1: Dataset Export

- [in_progress] Confirm current dataset exporter consumes analyst-approved split
      coordinates.
- [ ] Extend exporter to include explicit negative examples.
- [ ] Export all candidate metadata, not only the winning split.
- [ ] Export original image, crop previews, scanner type, dimensions, hashes,
      label version, reviewer metadata, and decision reason.
- [ ] Group train/validation/test splits by image hash or container pair to avoid
      leakage.
- [ ] Keep ASE and FS6000 splits separately measurable.
- [ ] Add manifest checksums.
- [ ] Add a dry-run mode that prints row counts by scanner and label class.
- [ ] Add exclusion reasons for unusable rows.

### C2: Training Strategy

- [ ] Decide first local model target:
  - image-level two-container eligibility classification
  - split X regression
  - candidate ranking
  - combined classifier plus ranker
- [ ] Start with a conservative two-stage model:
  - stage 1: should this image split at all
  - stage 2: where should the split be
- [ ] Train with scanner type as metadata or maintain scanner-specific heads.
- [ ] Add augmentation that preserves geometric meaning.
- [ ] Avoid augmentations that distort split location labels.
- [ ] Track MAE in pixels and normalized split error.
- [ ] Track two-container eligibility precision and recall.
- [ ] Track false-positive split rate on single-container examples.
- [ ] Track scanner-specific performance.
- [ ] Save model artifact, config, metrics, dataset manifest, git SHA, and
      training command.

### C3: Shadow Inference

- [ ] Add local model runtime behind a disabled-by-default feature flag.
- [ ] Store every local prediction in append-only prediction run tables.
- [ ] Include model name, version, artifact hash, runtime, latency, confidence,
      uncertainty, scanner type, and input hash.
- [ ] Do not override analyst labels.
- [ ] Do not auto-promote predictions into production split choices.
- [ ] Compare shadow predictions against analyst decisions after review.
- [ ] Add replay tooling to run the model over historical reviewed jobs.
- [ ] Add batch scoring for unreviewed jobs in shadow mode.

### C4: Promotion Rules

- [ ] Define minimum sample counts by scanner and negative class.
- [ ] Define required performance gates:
  - split X error threshold
  - eligibility false-positive threshold
  - eligibility false-negative threshold
  - manual correction rate
  - uncertainty coverage
  - latency budget
  - failure rate
- [ ] Require separate ASE pass/fail.
- [ ] Require separate FS6000 pass/fail.
- [ ] Require rollback plan before promotion.
- [ ] Require monitoring dashboard before promotion.
- [ ] Require shadow period before promotion.
- [ ] Require human sign-off before production mode.

### C5: Training/Model Acceptance Checks

- [ ] Exporter produces positive and negative labels.
- [ ] Exporter row counts match DB review counts.
- [ ] Training run is reproducible from manifest and config.
- [ ] Model artifact can be loaded in a clean environment.
- [ ] Shadow predictions are stored without affecting production state.
- [ ] Evaluation report separates ASE and FS6000.
- [ ] Promotion cannot be enabled without passing gates.

## Team D: Ops/Evaluation

Status: in_progress

Goal: make the splitter service observable, measurable, and operationally
controllable.

### D1: Queue Metrics

- [in_progress] Define canonical queue counts:
  - pending split jobs
  - processing split jobs
  - completed unreviewed jobs
  - reviewed approved jobs
  - reviewed rejected jobs
  - failed jobs
  - stale processing jobs
  - exportable labels
  - labels missing crop bytes
  - labels missing original bytes
- [ ] Add metrics by scanner type.
- [ ] Add metrics by age bucket.
- [ ] Add metrics by best strategy.
- [ ] Add metrics by reviewer decision.
- [ ] Add metrics for image load failures in Split Review.
- [ ] Add metrics for API latency and splitter service latency.
- [ ] Add metrics for shadow model latency and error rate.
- [ ] Add metrics for model disagreement with analyst labels.

### D2: Dashboards And Reports

- [ ] Add or update an operations dashboard for splitter queue health.
- [ ] Add a training readiness report.
- [ ] Add a daily label accumulation report.
- [ ] Add a scanner-specific quality report.
- [ ] Add a failed/stale job report.
- [ ] Add a report for production record synchronization gaps.
- [ ] Add a report for jobs where the top deterministic candidate was rejected.
- [ ] Add a report for repeated manual corrections by scanner or strategy.

### D3: Evaluation Framework

- [ ] Evaluate current deterministic candidate ranking against reviewed labels.
- [ ] Evaluate remote teacher output only if permitted and enabled.
- [ ] Evaluate local model shadow output.
- [ ] Keep "current best" evaluation separate from independent labels because
      analyst approvals may be biased toward candidates already shown.
- [ ] Build a holdout set that includes manually corrected and negative examples.
- [ ] Produce confusion matrix for split eligibility.
- [ ] Produce split X error distribution.
- [ ] Produce top-1/top-2 candidate match rate.
- [ ] Produce scanner-specific failure examples for human review.

### D4: Ops Acceptance Checks

- [ ] Operators can see exactly how many split jobs are waiting for review.
- [ ] Operators can see whether backlog is growing or shrinking.
- [ ] Operators can see training readiness by scanner.
- [ ] Operators can identify stale or failed jobs without database queries.
- [ ] Evaluators can compare deterministic, remote teacher, and local model
      predictions against analyst labels.
- [ ] Reports are reproducible from a timestamped command or endpoint.

## Team E: QA/Deploy

Status: in_progress

Goal: make the consolidation shippable, reversible, and testable without
publishing unrelated parallel work.

### E1: Test Matrix

- [in_progress] Build a test matrix covering:
  - Split Review approve candidate
  - Split Review approve manual
  - Split Review reject single container
  - Split Review reject bad image
  - Split Review uncertain
  - image analyst choose split
  - audit progression after approved split
  - audit blockage after negative split label
  - ASE image
  - FS6000 image
  - missing original image
  - missing crop image
  - stale processing job
  - duplicate job
  - failed splitter job
- [ ] Add API integration tests for canonical approval state transitions.
- [ ] Add WebApp tests or manual browser checklist for review workflow.
- [ ] Add Python service tests for export and shadow model writes.
- [ ] Add migration tests or startup checks.
- [ ] Add rollback smoke tests.

### E2: Deployment Plan

- [ ] Create a release branch before code changes if one is not already active.
- [ ] Keep unrelated dirty files out of release commits.
- [ ] Use a clean deployment worktree when the main workspace has unrelated
      in-flight changes.
- [ ] Build API, WebApp, services, and Python splitter service as applicable.
- [ ] Run focused tests first, then wider build/test pass.
- [ ] Bump version in the shared version file.
- [ ] Update `CHANGELOG.md`.
- [ ] Commit with a message that references splitter consolidation.
- [ ] Push the branch.
- [ ] Deploy WebApp/API/service changes from clean source.
- [ ] Verify service status after deployment.
- [ ] Verify health endpoints after deployment.
- [ ] Verify Split Review loads in the normal app route.
- [ ] Verify queue counts from live endpoint.
- [ ] Verify at least one approve/reject path in a controlled test or dry-run
      environment.

### E3: Rollback Plan

- [ ] Document previous deployed version.
- [ ] Keep deterministic splitter fallback enabled.
- [ ] Keep model feature flags disabled by default.
- [ ] Provide a switch to disable canonical production sync if it causes issues.
- [ ] Provide a switch to disable local model shadow scoring.
- [ ] Provide a switch to disable remote teacher calls.
- [ ] Provide DB rollback guidance for additive migrations.
- [ ] Provide data repair guidance for decisions created during a bad deploy.
- [ ] Ensure old image analyst split-choice flow still works until replacement
      is validated.

### E4: QA/Deploy Acceptance Checks

- [ ] Build passes.
- [ ] Focused tests pass.
- [ ] Version bump is present.
- [ ] Changelog entry is present.
- [ ] Commit is pushed.
- [ ] Deployment completes from clean source.
- [ ] Health endpoints are healthy.
- [ ] Split Review page loads.
- [ ] Queue metrics match API/database counts.
- [ ] Rollback instructions are documented and tested at least as a dry run.

## Cross-Team Dependencies

- API/DB must define the canonical decision contract before UX wires every
  action to final endpoints.
- API/DB must expose queue totals before UX can display reliable backlog counts.
- API/DB must expose complete label classes before Training/Model can export
  reliable positive and negative datasets.
- Split Review UX must capture negative labels before Training/Model can train
  a safe eligibility classifier.
- Training/Model must publish shadow prediction metadata before Ops/Evaluation
  can build disagreement and readiness reports.
- Ops/Evaluation must publish promotion metrics before QA/Deploy can approve any
  model-driven production behavior.
- QA/Deploy must verify clean deployment source before any release is published.

## Consolidated Workflow Target

1. Scanner downloads an image and metadata.
2. Two-container intake determines whether a split job is needed.
3. Splitter service generates deterministic candidate splits.
4. Optional local model and optional remote teacher run in shadow/advisory mode.
5. Split Review queue presents unreviewed jobs with large actual split crops.
6. Reviewer confirms left/right container assignment.
7. Reviewer approves candidate, approves manual split, or applies a negative
   label.
8. Canonical approval service writes durable splitter decision data.
9. If configured, canonical approval service synchronizes production
   assignment/record state.
10. Audit progression only proceeds when split state is valid under policy.
11. Dataset export consumes approved positive labels and explicit negative
   labels.
12. Local model trains from versioned exports.
13. Local model runs in shadow and stores append-only predictions.
14. Evaluation compares deterministic, remote teacher, and local model outputs
   against analyst labels.
15. Model promotion requires scanner-specific pass gates, rollback, and sign-off.

## Definition Of Done For Program

- [ ] Split Review purpose is documented in user-facing or operator-facing docs.
- [ ] Split Review is the canonical review console for splitter decisions.
- [ ] Approval, rejection, manual correction, and negative labels share one
      durable decision path.
- [ ] Analyst split decisions are synchronized with production records according
      to the approved policy.
- [ ] Negative labels are first-class training examples.
- [ ] Queue totals and review progress are visible without direct database
      queries.
- [ ] Dataset export produces reproducible manifests with scanner-specific
      counts.
- [ ] Local model training can run from exported labels.
- [ ] Local model shadow predictions are stored and evaluated.
- [ ] Promotion gates exist and are enforced.
- [ ] Rollback path exists and is documented.
- [ ] Version bump, changelog, commit, push, deployment, and smoke checks are
      complete.

## Evidence Log

Add entries here as teams complete work.

| Date | Team | Evidence | Status |
| --- | --- | --- | --- |
| 2026-05-13 | Master Tracker | Created consolidation implementation tracker | in_progress |
