# Image Splitter Training Baseline Inventory

Date: 2026-05-11
Branch: `codex-local-vision-training-20260511`

This is a read-only inventory of the current `image_split_jobs` / `image_split_results` data before the local vision training foundation work begins.

## Headline

- Total splitter jobs: 697
- Completed jobs: 696
- Failed jobs: 1
- Jobs with image bytes: 697
- Jobs missing image bytes: 0
- Jobs without any result rows: 0
- Result rows: 3,167
- Result rows missing at least one crop image: 60
- Stale processing jobs older than 2 minutes: 0

## Scanner Coverage

| Scanner type | Jobs |
| --- | ---: |
| ASE | 398 |
| FS6000 | 259 |
| Unknown/null | 40 |

## Label Coverage

| Label measure | Count |
| --- | ---: |
| Jobs with any analyst verdict | 22 |
| Approved jobs | 12 |
| Rejected jobs | 10 |
| Jobs with `correct_split_x` | 58 |
| Jobs with `ground_truth_split_x` | 21 |

## Label Coverage By Scanner

Approved:

| Scanner type | Count |
| --- | ---: |
| ASE | 2 |
| FS6000 | 4 |
| Unknown/null | 6 |

Rejected:

| Scanner type | Count |
| --- | ---: |
| ASE | 3 |
| FS6000 | 1 |
| Unknown/null | 6 |

Ground truth split X:

| Scanner type | Count |
| --- | ---: |
| ASE | 20 |
| FS6000 | 0 |
| Unknown/null | 1 |

Correct split X:

| Scanner type | Count |
| --- | ---: |
| ASE | 47 |
| FS6000 | 5 |
| Unknown/null | 6 |

## Unlabelled Completed Jobs

| Scanner type | Unlabelled completed jobs |
| --- | ---: |
| ASE | 393 |
| FS6000 | 254 |
| Unknown/null | 27 |

## Current Best Strategy Distribution

| Best strategy | Jobs |
| --- | ---: |
| `steel_wall_midpoint` | 283 |
| `foreground_seam` | 253 |
| `inner_casting_pair` | 122 |
| `container_gap` | 18 |
| `corner_fitting` | 8 |
| `edge_detection` | 7 |
| `claude_vision` | 6 |

## Duplicate Image Groups

- Duplicate image hash groups: 11
- Jobs inside duplicate image hash groups: 26

## Readout

The dataset is useful but not yet training-ready. The main blocker is label depth:

- Only 22 jobs have analyst verdicts.
- FS6000 has no `ground_truth_split_x` rows and only 5 `correct_split_x` rows.
- ASE has better split-X coverage, but only 2 approved analyst verdicts.
- There are enough unlabelled completed jobs to start a serious labeling pass immediately.

Near-term label target before a first local model:

- At least 100 approved ASE two-container examples.
- At least 100 approved FS6000 two-container examples.
- At least 100 explicit negative examples across scanner types.
- At least 25 manually corrected difficult examples per scanner type.
