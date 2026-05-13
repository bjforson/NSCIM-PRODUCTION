# {{model_name}}

Version: {{model_version}}

Training run: {{training_run_id}}

Created at: {{created_at}}

Production mode: {{production_mode}}

## Data

- Manifest: {{manifest_path}}
- Manifest sha256: {{manifest_sha256}}
- Training rows: {{train_rows}}
- Training positive split labels: {{train_positive_labels}}
- Training negative no-split labels: {{train_negative_labels}}
- Evaluation rows: {{eval_rows}}

## Artifact

- Model artifact sha256: {{artifact_sha256}}

## Intended Use

This artifact is for local shadow evaluation of the NSCIM image splitter. It
ranks existing exported splitter candidates and applies learned per-source bias
correction from analyst or ground-truth labels.

## Current Limits

- It does not inspect pixels directly.
- It does not write production decisions.
- No-split and single-container labels are evaluated but not modeled yet.
- Promotion requires a separate production wiring and validation step.
