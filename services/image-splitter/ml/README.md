# NSCIM Image Splitter Local Model Scaffold

This folder is the shadow-only starting point for a local splitter model. It
does not change production splitter decisions and it does not write database
rows.

## Input Contract

The scaffold consumes the manifest produced by:

```powershell
python services\image-splitter\tools\export_training_dataset.py --output-dir <dataset-dir>
```

The manifest rows carry the labels captured by analyst review:

- `label.source=analyst_correct` uses `image_split_jobs.correct_split_x`.
- `label.source=ground_truth` uses `image_split_jobs.ground_truth_split_x`.
- `label.class=no_split` captures single-container or rejected/no-split labels.

## Baseline Behavior

The first model is intentionally simple. It learns which stored candidate,
current-best strategy, or teacher output is historically closest to analyst or
ground-truth split labels, then writes a JSON artifact with learned per-source
error/bias statistics. It is useful for shadow evaluation and active-learning
planning, not production promotion.

## Commands

Train:

```powershell
python services\image-splitter\ml\train.py `
  --manifest <dataset-dir>\manifests\split_manifest.jsonl `
  --output-dir <artifact-dir>
```

Evaluate:

```powershell
python services\image-splitter\ml\evaluate.py `
  --manifest <dataset-dir>\manifests\split_manifest.jsonl `
  --model <artifact-dir>\model.json `
  --output-json <artifact-dir>\evaluation_refresh.json
```

Predict:

```powershell
python services\image-splitter\ml\predict.py `
  --manifest <dataset-dir>\manifests\split_manifest.jsonl `
  --model <artifact-dir>\model.json `
  --output-jsonl <artifact-dir>\shadow_predictions.jsonl
```

## Outputs

Training writes:

- `model.json`: JSON model artifact and learned source priors.
- `metadata.json`: artifact URI, sha256, manifest hash, and run identity.
- `evaluation_summary.json`: train and requested eval metrics.
- `config_effective.json`: copied effective baseline config.
- `model_card.md`: generated model card from the template.
