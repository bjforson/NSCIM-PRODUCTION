# Image Splitter Training Readiness Snapshot - 2026-05-13

Generated at: 2026-05-13T13:15:19Z

## Executive Status

Status: not training-ready.

The splitter service is live and the append-only prediction-run schema is ready, but the training corpus is still too thin for a production local model. The immediate blocker is label coverage: there are 134 labelled jobs, 0 negative/no-split labels, and only 13 FS6000 labelled examples.

## Migration Verification

Migration file checked:

- `services/image-splitter/migrations/003_add_append_only_prediction_runs.sql`

Live database verification:

- `image_split_remote_vision_runs`: exists
- `image_split_local_model_prediction_runs`: exists
- All expected remote/local prediction-run columns exist
- All expected indexes exist
- `image_split_remote_vision_runs` rows: 0
- `image_split_local_model_prediction_runs` rows: 0

Write-path probe:

- Inserted one local prediction-run row inside a transaction and rolled it back
- Inserted one remote vision-run row inside a transaction and rolled it back
- Both insert probes succeeded
- No probe rows were persisted

Conclusion: migration 003 was already applied or auto-created by the splitter startup path. No manual migration write was needed.

## Splitter Service State

Raw Image Engine health:

- URL: `http://localhost:5320/api/health`
- Status: healthy
- Database connected: true
- Strategies available: 9

Process supervision:

- The Python splitter is not installed as `NSCIM_RawImageEngine` or `NSCIM_ImageSplitter`
- It is supervised as a child process of `NSCIM_API`
- Running command: `python -m uvicorn main:app --host 127.0.0.1 --port 5320`
- Bound address: `127.0.0.1:5320`

Conclusion: no separate Python service deploy/restart is needed right now. The splitter is already running under the API supervisor with the deployed code path.

## Queue Snapshot

- Total splitter jobs: 713
- Completed: 712
- Failed: 1
- Pending: 0
- Processing: 0
- Completed reviewed: 135
- Completed unreviewed: 577
- Failed unreviewed: 1
- Review-ready all time: 577
- Review-ready last 72 hours: 266
- Candidate results with crops: 2394

## Label Snapshot

Totals:

- Labelled jobs: 134 of 712 completed
- Coverage rate: 18.82%
- Positive labels: 134
- Negative/no-split labels: 0
- Analyst-correct labels: 113
- Ground-truth labels: 21
- Approved verdicts: 86
- Rejected verdicts: 49
- Unreviewed verdicts: 578

By scanner:

| Scanner | Labelled | Completed | Coverage | Positive | Negative | Ground truth |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ASE | 114 | 408 | 27.94% | 114 | 0 | 20 |
| FS6000 | 13 | 265 | 4.91% | 13 | 0 | 0 |
| unknown | 7 | 39 | 17.95% | 7 | 0 | 1 |

## Prediction/Teacher State

- Local prediction runs: 0
- Remote append-only vision runs: 0
- Legacy Claude columns: 87 rows, 87 split predictions, 1 model
- Consensus corpus: 24 rows

## Training Readiness Warnings

- Label volume is low: 134 labelled jobs, target at least 500.
- Positive split labels are low: 134, target at least 300.
- Negative/no-split labels are absent: 0, target at least 50.
- Ground-truth labels are thin: 21.
- Review backlog is high: 577 completed unreviewed jobs.
- There is 1 failed unreviewed splitter job to triage.
- ASE has no negative/no-split labels.
- FS6000 has only 13 labelled jobs across 265 completed jobs.
- FS6000 has no negative/no-split labels.
- No local model prediction runs exist yet, so the local model is not in shadow mode.
- Consensus corpus is small at 24 rows.

## Recommended Next Step

Start a focused labelling pass before training:

1. Label at least 100 FS6000 examples.
2. Capture at least 50 negative/no-split examples across scanners.
3. Clear or triage the 1 failed unreviewed splitter job.
4. Re-run the operational report.
5. Export the dataset and train the local baseline only after the negative and FS6000 gaps are addressed.
