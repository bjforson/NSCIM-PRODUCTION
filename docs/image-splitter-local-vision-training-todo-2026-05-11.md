# Image Splitter Local Vision Training TODO

Date: 2026-05-11
Branch context: `codex-two-container-split-intake-20260510`
Current deployed baseline: `2.17.4`

## Purpose

Track the full path from analyst labeling to a locally trained production image splitter.

The target architecture is:

1. Keep the current deterministic splitter live.
2. Add optional OpenAI vision verification in shadow mode only.
3. Capture analyst decisions as ground truth.
4. Train a local model from approved labels and explicit negative examples.
5. Run the local model in shadow mode.
6. Promote the local model only after scanner-specific evaluation gates pass.

This TODO is deliberately broad. It is intended to be split across multiple teams once implementation starts.

## Progress Update: 2.17.5

- [x] Created baseline inventory report for current splitter jobs, labels, scanner coverage, duplicate image groups, stale processing jobs, and missing image/crop rows.
- [x] Added append-only persistence for remote vision teacher runs and local model prediction runs.
- [x] Added disabled-by-default OpenAI vision teacher/verifier in advisory shadow mode.
- [x] Persisted OpenAI advisory output to the append-only remote vision run table when it runs.
- [x] Added explicit Split Review labels for wrong split, single container, bad image/decode failure, and uncertain cases.
- [x] Added dataset export tooling with JSONL manifests, labels, original images, top-strip/preview derivatives, scanner/date filters, dry-run mode, and deterministic train/val/test grouping.
- [x] Added baseline evaluator for stored splitter candidates, current best selection, and ranker-selected candidates.
- [ ] Confirm production policy for sending scanner images to OpenAI before enabling `OPENAI_VISION_ENABLED`.
- [ ] Start analyst labeling pass; current data is still label-poor and not ready for local model training.

## Key Anchors

- Python splitter service: `services/image-splitter/`
- Pipeline orchestrator: `services/image-splitter/pipeline/orchestrator.py`
- Existing remote-vision strategy pattern: `services/image-splitter/strategies/claude_vision.py`
- Splitter database models: `services/image-splitter/models/database.py`
- Splitter API: `services/image-splitter/main.py`
- .NET splitter client: `src/NickScanCentralImagingPortal.Services/ImageSplitter/ImageSplitterService.cs`
- Two-container intake worker: `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeService.cs`
- Split review page: `src/NickScanWebApp.New/Pages/ImageAnalysis/SplitReview.razor`
- API split facade: `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs`

## Status Legend

- `[ ]` Not started
- `[x]` Done
- `[blocked]` Blocked
- `[review]` Needs review
- `[shadow]` Implemented but not driving production decisions

## Non-Negotiable Guardrails

- [ ] Do not put Codex itself in the production request path.
- [ ] Treat any OpenAI vision model as a teacher, verifier, or shadow evaluator only until promotion gates pass.
- [ ] Keep analyst approval as the source of truth.
- [ ] Keep current deterministic splitter available as fallback.
- [ ] Never auto-advance a split into audit unless the job has a valid review state or the new model has passed production gates.
- [ ] Preserve a hard "single container / do not split" label path.
- [ ] Track ASE and FS6000 metrics separately.
- [ ] Store model/provider/version metadata with every prediction.
- [ ] Add rollback steps before enabling any model-driven production behavior.

## Phase 0: Governance And Scope

- [ ] Confirm whether production scanner images may be sent to OpenAI APIs.
- [ ] If external API use is not allowed, disable OpenAI teacher path and use analyst-only labeling.
- [ ] Define allowed data fields for remote vision calls.
- [ ] Confirm whether image bytes may be cached after remote inference.
- [ ] Decide retention period for remote provider responses.
- [ ] Decide whether provider reasoning text may be shown to analysts.
- [ ] Define audit log requirements for AI-assisted splitting.
- [ ] Define which roles can view, approve, reject, or manually correct split labels.
- [ ] Define which roles can trigger backfill, export, training, and model promotion.
- [ ] Add a short operational policy for "AI recommendation is advisory until promoted."
- [ ] Add a security review checkpoint before any external provider is enabled outside local dev.

Exit criteria:

- [ ] Written decision on external API allowance.
- [ ] Written decision on data retention.
- [ ] Written decision on who can promote models.

## Phase 1: Baseline Inventory

- [ ] Count all completed `image_split_jobs` by scanner type.
- [ ] Count all jobs with `analyst_verdict`.
- [ ] Count all jobs with `correct_split_x`.
- [ ] Count all jobs with `ground_truth_split_x`.
- [ ] Count all rejected jobs.
- [ ] Count single-container examples that reached splitter incorrectly.
- [ ] Count two-container originals that have no splitter job.
- [ ] Count jobs with image bytes missing.
- [ ] Count jobs with result image bytes missing.
- [ ] Count jobs by `best_strategy`.
- [ ] Count jobs by status: pending, processing, completed, failed.
- [ ] Identify stale processing jobs older than the configured orphan threshold.
- [ ] Identify duplicated container-pair jobs.
- [ ] Identify same image hash across multiple jobs.
- [ ] Identify ASE-only issues.
- [ ] Identify FS6000-only issues.
- [ ] Build a small baseline report in `docs/` with counts and sample job IDs.

Exit criteria:

- [ ] We know how many usable positive labels exist.
- [ ] We know how many usable negative labels exist.
- [ ] We know how much manual labeling is still needed before training.

## Phase 2: Label Schema

- [ ] Define label classes:
  - `single_container`
  - `two_container`
  - `uncertain`
  - `bad_image`
  - `scanner_decode_failure`
  - `duplicate`
- [ ] Define split label fields:
  - `split_x`
  - `split_x_normalized`
  - `left_container_number`
  - `right_container_number`
  - `scanner_type`
  - `image_width`
  - `image_height`
  - `label_source`
  - `label_confidence`
  - `reviewer_id`
  - `reviewed_at`
  - `notes`
- [ ] Define candidate label fields:
  - strategy name
  - candidate split X
  - confidence
  - outcome
  - rank
  - model/provider metadata
- [ ] Define negative example fields:
  - reason image should not split
  - original source page or assignment context
  - reviewer note
- [ ] Define ambiguous example handling.
- [ ] Define manual correction handling.
- [ ] Define rejected model prediction handling.
- [ ] Define label precedence:
  - analyst manual split
  - analyst approved candidate
  - analyst rejected as single container
  - operator/import metadata
  - remote vision teacher output
  - deterministic candidate
- [ ] Create JSON schema for training export rows.
- [ ] Add schema version field.

Exit criteria:

- [ ] One documented schema exists for all training examples.
- [ ] The schema can represent both positive and negative examples.
- [ ] The schema can trace every label back to a reviewer or source.

## Phase 3: Database And Persistence

- [ ] Add migration for `image_split_vision_runs`.
- [ ] Suggested fields:
  - `id`
  - `job_id`
  - `provider`
  - `model`
  - `model_version`
  - `prompt_version`
  - `input_image_sha256`
  - `scanner_type`
  - `is_two_container_image`
  - `split_x`
  - `confidence`
  - `reasoning`
  - `raw_response_json`
  - `latency_ms`
  - `input_tokens`
  - `output_tokens`
  - `cost_estimate`
  - `error_message`
  - `created_at`
- [ ] Add migration for local model prediction runs.
- [ ] Suggested fields:
  - `id`
  - `job_id`
  - `model_name`
  - `model_version`
  - `model_sha256`
  - `is_two_container_image`
  - `split_x`
  - `confidence`
  - `uncertainty`
  - `latency_ms`
  - `runtime`
  - `created_at`
- [ ] Add indexes by `job_id`, `provider`, `model_version`, `created_at`.
- [ ] Add index for unlabeled completed jobs.
- [ ] Add index for scanner type and reviewed state.
- [ ] Add index for image hash if not already available.
- [ ] Keep provider-specific columns for backward compatibility if already in use.
- [ ] Make new tables append-only where practical.
- [ ] Ensure migrations are idempotent for production deploy.
- [ ] Add API DTOs for vision runs and local model predictions.
- [ ] Add tests for migration-safe startup.

Exit criteria:

- [ ] Every remote or local prediction can be audited later.
- [ ] Predictions are stored without overwriting analyst labels.

## Phase 4: OpenAI Vision Teacher In Shadow Mode

- [ ] Add `openai` dependency to `services/image-splitter/requirements.txt`.
- [ ] Add environment variables:
  - `OPENAI_VISION_ENABLED`
  - `OPENAI_API_KEY`
  - `OPENAI_VISION_MODEL`
  - `OPENAI_VISION_PROMPT_VERSION`
  - `OPENAI_VISION_MAX_IMAGE_SIDE`
  - `OPENAI_VISION_SAMPLE_RATE`
  - `OPENAI_VISION_LOW_CONFIDENCE_ONLY`
  - `OPENAI_VISION_TIMEOUT_SECONDS`
  - `OPENAI_VISION_DAILY_COST_LIMIT`
- [ ] Add startup validation for OpenAI provider configuration.
- [ ] Create `services/image-splitter/strategies/openai_vision.py`.
- [ ] Create `services/image-splitter/pipeline/openai_verifier.py`.
- [ ] Reuse the existing strategy result shape where possible.
- [ ] Prompt must ask for strict JSON only.
- [ ] Prompt must ask for `is_two_container_image` before `split_x`.
- [ ] Prompt must warn that single-container images can appear in the queue.
- [ ] Prompt must emphasize the top strip and corner casting boundary.
- [ ] Prompt must include image dimensions and coordinate system.
- [ ] Prompt must return uncertainty and a review-needed flag.
- [ ] Add robust JSON extraction and validation.
- [ ] Reject out-of-bounds split X.
- [ ] Reject center-default answers when confidence is low or reasoning is weak.
- [ ] Store every response in `image_split_vision_runs`.
- [ ] Add rate limiting.
- [ ] Add cost cap.
- [ ] Add request timeout.
- [ ] Add retry only for transient errors.
- [ ] Add cache by image hash and prompt/model version.
- [ ] Ensure provider failure cannot fail the splitter job.
- [ ] Ensure provider output does not auto-promote any split.
- [ ] Add unit tests with mocked OpenAI responses.
- [ ] Add integration smoke test behind disabled-by-default env flag.

Exit criteria:

- [ ] OpenAI teacher can run on selected jobs in shadow mode.
- [ ] Provider outages do not affect splitter availability.
- [ ] All outputs are auditable.

## Phase 5: Split Review UI For Labelling

- [ ] Enhance `SplitReview.razor` to show original image prominently.
- [ ] Show current top two split candidates.
- [ ] Show strategy name, confidence, and split X for each candidate.
- [ ] Add visual split line overlay on original image.
- [ ] Add side-by-side crop preview for each candidate.
- [ ] Add "Approve this split" action.
- [ ] Add "Manual split" action with draggable vertical line.
- [ ] Add "Single container, do not split" action.
- [ ] Add "Bad image / decode failure" action.
- [ ] Add "Uncertain, needs supervisor" action.
- [ ] Add notes field.
- [ ] Add scanner type badge.
- [ ] Add job age and source date.
- [ ] Add container pair display.
- [ ] Add image dimensions display.
- [ ] Add current best strategy display.
- [ ] Add OpenAI teacher recommendation display when available.
- [ ] Add local model recommendation display when available.
- [ ] Make it clear recommendations are advisory in shadow mode.
- [ ] Add keyboard shortcuts only if they do not increase accidental approvals.
- [ ] Add undo-last-label within a short admin-controlled window.
- [ ] Add filter by scanner type.
- [ ] Add filter by label status.
- [ ] Add filter by model disagreement.
- [ ] Add filter by low confidence.
- [ ] Add filter by "possible single-container false positive."
- [ ] Add pagination or virtualized list if recent queue grows beyond current limit.
- [ ] Ensure images load through signed API URLs.
- [ ] Ensure image URL signing works behind LAN/browser hosts.
- [ ] Ensure reviewer identity is recorded.
- [ ] Add telemetry for labeling latency and action counts.

Exit criteria:

- [ ] Analyst can label positive and negative examples without leaving the review page.
- [ ] Manual split corrections are captured as training labels.
- [ ] Recommendations are visible but cannot silently drive audit progression.

## Phase 6: Label Quality Workflow

- [ ] Define daily labeling target per reviewer.
- [ ] Define minimum labels required for v0 training.
- [ ] Define minimum negative examples required before training.
- [ ] Define minimum ASE positive labels.
- [ ] Define minimum ASE negative labels.
- [ ] Define minimum FS6000 positive labels.
- [ ] Define minimum FS6000 negative labels.
- [ ] Add second-review queue for uncertain labels.
- [ ] Add disagreement queue where model and analyst differ.
- [ ] Add random audit queue for approved labels.
- [ ] Add duplicate-image detection to avoid over-counting repeated scans.
- [ ] Add reviewer agreement metrics.
- [ ] Add label drift report by date and scanner type.
- [ ] Add dashboard counts:
  - unlabeled completed jobs
  - approved splits
  - rejected single-container jobs
  - manual corrections
  - uncertain labels
  - model disagreements
- [ ] Add export of label quality report.

Exit criteria:

- [ ] We have enough labels to train without relying on remote vision as truth.
- [ ] Label noise and duplicates are visible.

## Phase 7: Dataset Export

- [ ] Add `services/image-splitter/tools/export_training_dataset.py`.
- [ ] Export images to stable folder layout:
  - `dataset/images/original/`
  - `dataset/images/top_strip/`
  - `dataset/images/previews/`
  - `dataset/labels/`
  - `dataset/manifests/`
- [ ] Export JSONL manifest.
- [ ] Include job ID and source image ID.
- [ ] Include scanner type.
- [ ] Include container numbers.
- [ ] Include image dimensions.
- [ ] Include image hash.
- [ ] Include label schema version.
- [ ] Include split X and normalized split X.
- [ ] Include label class.
- [ ] Include reviewer metadata.
- [ ] Include candidate strategy list.
- [ ] Include teacher/provider predictions if available.
- [ ] Include local model predictions if available.
- [ ] Exclude rows without usable image bytes.
- [ ] Exclude labels marked duplicate unless explicitly requested.
- [ ] Support `--scanner-type ASE`.
- [ ] Support `--scanner-type FS6000`.
- [ ] Support `--from-date`.
- [ ] Support `--to-date`.
- [ ] Support `--include-unlabeled`.
- [ ] Support `--include-negatives`.
- [ ] Support `--dry-run`.
- [ ] Support reproducible train/val/test split.
- [ ] Split train/val/test by image hash or container/date group, not random rows.
- [ ] Add manifest checksum.
- [ ] Add export summary report.
- [ ] Add test for schema validity.

Exit criteria:

- [ ] We can recreate the same dataset from the same DB state.
- [ ] Training, validation, and test splits do not leak duplicates.

## Phase 8: Baseline Evaluation

- [ ] Build evaluator for current deterministic pipeline.
- [ ] Evaluate `inner_casting_pair`.
- [ ] Evaluate `foreground_seam`.
- [ ] Evaluate `steel_wall_midpoint`.
- [ ] Evaluate candidate ranker.
- [ ] Evaluate best strategy as currently selected.
- [ ] Evaluate per scanner type.
- [ ] Evaluate on positive examples.
- [ ] Evaluate false positives on negative examples.
- [ ] Metrics:
  - mean absolute error in pixels
  - median absolute error in pixels
  - P90 absolute error
  - P95 absolute error
  - normalized absolute error
  - false split rate on single-container images
  - missed split rate on two-container images
  - review-required rate
- [ ] Produce confusion matrix for single/two/uncertain.
- [ ] Produce worst-case gallery.
- [ ] Produce examples where deterministic strategies disagree.
- [ ] Produce scanner-specific failure notes.
- [ ] Store baseline report under `docs/`.

Exit criteria:

- [ ] We know the numeric bar the local model must beat.
- [ ] ASE and FS6000 weaknesses are separated.

## Phase 9: Local Model V0

- [ ] Decide initial model type.
- [ ] Recommended v0:
  - top-strip classifier/regressor
  - input A: full image downsample
  - input B: top 25 percent crop
  - output A: class probabilities
  - output B: split X heatmap or normalized coordinate
  - output C: uncertainty
- [ ] Create `services/image-splitter/ml/`.
- [ ] Add training config file.
- [ ] Add dataset loader.
- [ ] Add augmentations:
  - mild brightness shift
  - mild contrast shift
  - small horizontal jitter where label adjusts
  - scanner-specific normalization
- [ ] Avoid augmentations that break physical geometry.
- [ ] Add train script.
- [ ] Add eval script.
- [ ] Add predict script.
- [ ] Add model card template.
- [ ] Add checkpoint naming convention.
- [ ] Add deterministic seed handling.
- [ ] Add CPU-only training fallback.
- [ ] Add GPU training path if available.
- [ ] Add ONNX export.
- [ ] Add ONNX inference validation.
- [ ] Add model SHA256 calculation.
- [ ] Add artifact folder:
  - `services/image-splitter/models/local-splitter/`
- [ ] Do not commit large model binaries unless repo policy allows.
- [ ] Add artifact deployment procedure if binaries are external.

Exit criteria:

- [ ] A local model can be trained from exported labels.
- [ ] The model can be evaluated reproducibly.
- [ ] The model can be exported for runtime inference.

## Phase 10: Local Runtime Integration

- [ ] Add `onnxruntime` dependency or confirm chosen runtime.
- [ ] Add `strategies/local_student.py`.
- [ ] Add config:
  - `LOCAL_SPLITTER_ENABLED`
  - `LOCAL_SPLITTER_MODEL_PATH`
  - `LOCAL_SPLITTER_MODEL_VERSION`
  - `LOCAL_SPLITTER_MIN_CONFIDENCE`
  - `LOCAL_SPLITTER_REQUIRE_CONSENSUS`
  - `LOCAL_SPLITTER_SHADOW_ONLY`
- [ ] Load model once at service startup.
- [ ] Fail startup only if local model is required and missing.
- [ ] Otherwise warn and continue.
- [ ] Preprocess images exactly as training expects.
- [ ] Validate model output bounds.
- [ ] Return `SplitResult` compatible with existing strategies.
- [ ] Store local prediction run.
- [ ] Add runtime latency metrics.
- [ ] Add image hash to prediction metadata.
- [ ] Add model version and SHA to metadata.
- [ ] Add disagreement guard against deterministic candidates.
- [ ] Add single-container false-positive guard.
- [ ] Add low-confidence path to analyst review.
- [ ] Add unit tests for model loading failure.
- [ ] Add unit tests for out-of-bounds predictions.
- [ ] Add integration test with a tiny dummy ONNX model or mock runtime.

Exit criteria:

- [ ] Local model can run inside the splitter service.
- [ ] Local model predictions are logged but shadow-only by default.

## Phase 11: Shadow Deployment

- [ ] Deploy OpenAI teacher disabled by default.
- [ ] Deploy local model disabled by default.
- [ ] Enable local model shadow mode on dev.
- [ ] Enable local model shadow mode on production only after smoke test.
- [ ] Confirm current deterministic split output is unchanged.
- [ ] Confirm analyst review page still loads jobs and images.
- [ ] Confirm predictions are stored.
- [ ] Confirm provider/model failures do not affect job completion.
- [ ] Compare local model vs analyst labels daily.
- [ ] Compare OpenAI teacher vs analyst labels if enabled.
- [ ] Compare local model vs deterministic best split.
- [ ] Report disagreement cases to labeling queue.
- [ ] Monitor latency and memory.
- [ ] Monitor service restart behavior.
- [ ] Monitor queue progression from image download to split review to audit.

Exit criteria:

- [ ] Shadow predictions run without production behavior change.
- [ ] We have enough shadow data for promotion decision.

## Phase 12: Promotion Gates

- [ ] Define hard gate for single-container false positive rate.
- [ ] Define hard gate for missed two-container rate.
- [ ] Define hard gate for mean pixel error.
- [ ] Define hard gate for P95 pixel error.
- [ ] Define hard gate for review-required rate.
- [ ] Define hard gate for latency.
- [ ] Define separate ASE thresholds.
- [ ] Define separate FS6000 thresholds.
- [ ] Define minimum sample size by scanner type.
- [ ] Define rollback trigger.
- [ ] Define monitoring alert thresholds.
- [ ] Create promotion checklist.
- [ ] Require signed approval before enabling non-shadow mode.
- [ ] Add feature flag for "model may rank candidates."
- [ ] Add feature flag for "model may choose top candidate."
- [ ] Keep "model may auto-advance to audit" disabled until a later phase.

Exit criteria:

- [ ] There is a written, numeric standard for promotion.
- [ ] Promotion can be reversed with config only.

## Phase 13: Controlled Production Use

- [ ] Enable local model to rank candidates only.
- [ ] Keep analyst approval required.
- [ ] Monitor ranking improvement.
- [ ] Enable local model as primary candidate only for high confidence.
- [ ] Require deterministic agreement within configured threshold.
- [ ] Route all disagreements to review.
- [ ] Route all single/two uncertainty to review.
- [ ] Keep OpenAI teacher as sampled QA only if allowed.
- [ ] Keep deterministic fallback active.
- [ ] Add daily report for:
  - model accepted
  - model overridden
  - model uncertain
  - deterministic fallback
  - analyst manual correction
  - false positive single-container catches
- [ ] Review weekly before further automation.

Exit criteria:

- [ ] Local model helps analysts without creating silent progression risk.

## Phase 14: Active Learning Loop

- [ ] Nightly job selects examples for labeling.
- [ ] Selection buckets:
  - high model uncertainty
  - model vs deterministic disagreement
  - model vs analyst disagreement
  - new scanner/source pattern
  - suspected single-container false positive
  - random sample of high-confidence predictions
- [ ] Add active-learning queue endpoint.
- [ ] Add active-learning filter to review UI.
- [ ] Add export tag for active-learning examples.
- [ ] Retrain on a fixed cadence.
- [ ] Record training dataset version.
- [ ] Record code commit used for training.
- [ ] Record model metrics by dataset version.
- [ ] Compare new model against previous model.
- [ ] Keep previous model artifact for rollback.

Exit criteria:

- [ ] The model improves from real analyst feedback instead of ad hoc manual tuning.

## Phase 15: Operational Hardening

- [ ] Add health endpoint fields for model availability.
- [ ] Add health endpoint fields for provider availability.
- [ ] Add health endpoint fields for prediction latency.
- [ ] Add log correlation IDs across API, WebApp, and splitter.
- [ ] Add structured logs for split decisions.
- [ ] Add alerts for high failed job rate.
- [ ] Add alerts for high stale processing job count.
- [ ] Add alerts for sudden single-container rejection spike.
- [ ] Add alerts for model latency spike.
- [ ] Add alerts for model unavailable when enabled.
- [ ] Add backup/export for label database.
- [ ] Add restore test for label database.
- [ ] Add deployment note for model artifact path.
- [ ] Add rollback note for model feature flags.
- [ ] Add smoke test script after deploy.

Exit criteria:

- [ ] Operators can tell whether the splitter, model, labels, and queue are healthy.

## Phase 16: Security And Privacy

- [ ] Ensure remote provider API keys are only loaded from secure config/environment.
- [ ] Ensure API keys are never logged.
- [ ] Redact provider raw responses if they include sensitive data beyond intended fields.
- [ ] Do not include container/customer metadata in remote prompts unless needed.
- [ ] Prefer sending image only plus dimensions and scanner type.
- [ ] Cache remote calls by image hash without exposing sensitive metadata.
- [ ] Add cost guardrails for remote calls.
- [ ] Add timeout and circuit breaker.
- [ ] Add allowlist for remote provider enablement.
- [ ] Add admin-only controls for remote teacher runs.
- [ ] Add audit log for remote vision use.
- [ ] Add audit log for label edits.
- [ ] Add audit log for model promotion.

Exit criteria:

- [ ] Security review can trace data flow and control points.

## Phase 17: Testing Matrix

- [ ] Unit test label schema serialization.
- [ ] Unit test export schema validation.
- [ ] Unit test OpenAI response parsing.
- [ ] Unit test remote provider disabled path.
- [ ] Unit test remote provider timeout path.
- [ ] Unit test local model disabled path.
- [ ] Unit test local model unavailable path.
- [ ] Unit test prediction bounds.
- [ ] Unit test single-container label capture.
- [ ] Unit test manual split label capture.
- [ ] Unit test approval label capture.
- [ ] Integration test split job completion with no model.
- [ ] Integration test split job completion with local model shadow.
- [ ] Integration test split job completion with remote teacher mocked.
- [ ] Integration test SplitReview loads signed images.
- [ ] Integration test SplitReview submits manual split.
- [ ] Integration test SplitReview submits single-container rejection.
- [ ] Regression test queue progression image to split review to analysis assignment.
- [ ] Performance test batch splitter throughput.
- [ ] Performance test local model inference latency.
- [ ] Smoke test after deploy.

Exit criteria:

- [ ] Model work does not regress the production assignment flow.

## Phase 18: Documentation

- [ ] Add architecture document for teacher/student splitter.
- [ ] Add data dictionary for label schema.
- [ ] Add analyst labeling guide.
- [ ] Add admin guide for export/training.
- [ ] Add runbook for model deployment.
- [ ] Add rollback guide.
- [ ] Add model card for each promoted model.
- [ ] Add changelog entry for each feature flag release.
- [ ] Add version bump for each deployable milestone.

Exit criteria:

- [ ] Future maintainers can understand and safely operate the system.

## Team Split For Implementation

Team A: Data And Persistence

- [ ] Migrations for prediction runs.
- [ ] Label schema.
- [ ] Dataset export.
- [ ] Baseline inventory reports.

Team B: Vision Provider And Splitter Runtime

- [ ] OpenAI teacher provider.
- [ ] Local ONNX strategy.
- [ ] Provider failure handling.
- [ ] Runtime metrics.

Team C: Analyst Review UX

- [ ] Split review UI improvements.
- [ ] Manual split tooling.
- [ ] Negative label workflow.
- [ ] Recommendation display.

Team D: Training And Evaluation

- [ ] Dataset loader.
- [ ] Baseline evaluator.
- [ ] Local model training.
- [ ] ONNX export.
- [ ] Metrics reports.

Team E: QA, Security, And Deployment

- [ ] Test matrix.
- [ ] Feature flags.
- [ ] Security review.
- [ ] Deployment/rollback runbooks.
- [ ] Changelog/version discipline.

## Suggested Milestones

M0: Decision And Data Policy

- [ ] External provider policy approved or rejected.
- [ ] Roles and audit expectations agreed.

M1: Label Foundation

- [ ] Label schema implemented.
- [ ] Split review page captures all needed labels.
- [ ] Dataset export works.

M2: Teacher Shadow

- [ ] OpenAI teacher provider available behind feature flag.
- [ ] Teacher outputs stored but not used for production decisions.

M3: Baseline Report

- [ ] Current deterministic splitter measured.
- [ ] ASE and FS6000 metrics separated.

M4: Local Model V0

- [ ] Trainable local model exists.
- [ ] ONNX artifact exported.
- [ ] Evaluation report generated.

M5: Runtime Shadow

- [ ] Local model runs in production shadow mode.
- [ ] Predictions logged.
- [ ] No production behavior change.

M6: Candidate Ranking

- [ ] Local model can rank candidates for analyst review.
- [ ] Analyst remains final approver.

M7: Controlled Primary Candidate

- [ ] Local model can become primary candidate only under strict confidence and agreement gates.
- [ ] Disagreements route to review.

M8: Continuous Learning

- [ ] Active-learning queue feeds retraining.
- [ ] Model promotion process is repeatable.

## Immediate Next Actions

- [ ] Run Phase 1 baseline inventory query set.
- [ ] Confirm external OpenAI image-use policy.
- [ ] Implement `image_split_vision_runs` table.
- [ ] Implement dataset export script.
- [ ] Add SplitReview actions for single-container and manual correction if incomplete.
- [ ] Build deterministic baseline evaluator.
- [ ] Decide first label target count for ASE and FS6000.
- [ ] Start labeling queue with current completed jobs.
