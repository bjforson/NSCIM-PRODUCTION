# Image Splitter Operations Runbook - 2026-05-13

## Operational Report

Use the read-only splitter report to see the state of review queues, label
coverage, prediction runs, current strategy distribution, and training
readiness warnings:

```powershell
services\image-splitter\venv\Scripts\python.exe services\image-splitter\tools\splitter_operational_report.py
```

For automation, emit JSON:

```powershell
services\image-splitter\venv\Scripts\python.exe services\image-splitter\tools\splitter_operational_report.py --json
```

The CLI opens a PostgreSQL read-only session and does not write files or mutate
splitter tables.

## What To Watch

- `completed_unreviewed`: operational review backlog.
- `review_ready_with_crops`: completed unreviewed jobs that have visible split
  crops and can be reviewed by the split review page.
- `positive split labels`: usable split examples for model training.
- `negative/no-split labels`: single-container or bad-image examples; these are
  required before training a robust split/no-split classifier.
- `ground truth labels`: independent manual split labels. Low counts here mean
  evaluation may be biased toward candidates analysts already approved.
- `local prediction runs`: should grow after the local model enters shadow mode.
- `remote prediction runs` and `legacy_claude_columns`: teacher/advisory vision
  signals available for comparison.
- `current best strategy distribution`: which deterministic strategies are
  dominating live results.

## Current Training Readiness Gate

Do not promote a local splitter model until the report has no critical warnings
for:

- total labelled examples,
- positive split examples,
- negative/no-split examples,
- scanner-specific label coverage,
- local model shadow prediction runs,
- review backlog.

The default thresholds are intentionally conservative and can be tuned with the
CLI flags when running experiments.
