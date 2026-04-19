# Splitter Vision Test — Claude Vision vs steel_wall_midpoint vs eyeball baseline

**Date:** 2026-04-08
**Environment:** NSCIM production splitter service (NSCIM_ImageSplitter, port 5320),
model `claude-sonnet-4-5` via anthropic==0.92.0.
**Method:** 9 representative multi-container X-ray images sampled from existing
`image_split_jobs` rows (where TEST001 synthetic jobs were excluded). Each image
was re-uploaded via `POST /api/split/upload` against a live splitter with
`ANTHROPIC_API_KEY` newly configured on the NSSM service.

## Environment fixes landed during this test

1. **`anthropic` Python package was in `requirements.txt` but never installed in
   the splitter venv.** 1.19.0 added `anthropic==0.40.0` to the pin file but the
   venv was never re-`pip install`-ed on the production host, so the
   `claude_vision` strategy had been silently returning `None` for every job
   since 1.19.0 shipped. Installed `anthropic==0.92.0` and bumped the pin in
   `requirements.txt` to match. This is the bug to bundle with 1.20.0.
2. **`ANTHROPIC_API_KEY` set via `nssm set NSCIM_ImageSplitter AppEnvironmentExtra`** —
   service-scoped, not system-wide, key is not written to any tracked file.
3. **First key had zero credit balance** → all calls rejected with HTTP 400
   `"Your credit balance is too low..."`. Second key from a billed org worked
   immediately.

## Results (9 jobs)

| # | job (short) | W×H | steel_wall | claude_vision | Δ (cv−sw) | conf | ms | eyeball | notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 71f21bbd | 1619×544 | 656 | **653** |   −3 | 0.874 | 5311 | ~700 | |
| 2 | c690e72a | 1587×544 | 638 | **638** |  **0** | 0.874 | 4592 | ~640 | right = vehicle |
| 3 | 6e728f65 | 1614×544 | 664 | **645** |  −19 | 0.874 | 4344 | ~680 | |
| 4 | be0a47c1 | 1594×544 | 665 | **658** |   −7 | 0.874 | 5250 | ~760 | right = vehicle |
| 5 | 35d860b6 | 1659×544 | 682 | **650** | **−32** | 0.808 | 9140 | ~620 | heterogeneous |
| 6 | c5cc8d15 | 1644×544 | 671 | **652** |  −19 | 0.874 | 6311 | ~660 | |
| 7 | 935f234c | 1578×544 | 659 | **645** |  −14 | 0.808 | 4531 | ~620 | right = pickup |
| 8 | 47565796 | 1599×544 | 658 | **655** |   −3 | 0.808 | 4328 | ~620 | |
| 9 | a0ed560b | 1604×544 | 670 | **655** |  −15 | 0.808 | 7593 | ~625 | both = SUVs |

**Aggregate stats:**
- Mean absolute Δ (claude_vision vs steel_wall_midpoint): **12.4 px** (0.8% of image width)
- Max Δ: 32 px (image #5)
- Claude Vision is ≤ steel_wall on **9 of 9 images** (systematic left bias)
- Confidence: 0.874 (high-signal, 5 images) or 0.808 (harder cases, 4 images)
- Latency: 4.3s – 9.1s (mean 5.7s)
- Total tokens: 11,627 in / 1,035 out
- Total cost for 9 calls: **~$0.050** (≈ $0.0056 per call at sonnet-4-5 pricing)

## Interpretation

### Claude Vision agrees with steel_wall_midpoint
The two methods converge to within ~1% of image width on every test image. This
is a strong corroboration of `steel_wall_midpoint` — the hand-engineered heuristic
is already nearly as good as the LLM teacher on this population of images.

### The systematic left bias is explained by the reasoning strings
Claude consistently describes its chosen split as:
> "the gap between the **right wall of the left container** and the **left wall
> of the right container**"
> "the **right corner casting of the left container** and the **left corner
> casting of the right container**"

i.e. Claude picks the *start* of the gap (left edge of the right container),
while `steel_wall_midpoint` picks the *midpoint* of the two outer walls. Both
are defensible. For downstream slicing, the midpoint is usually safer because
it leaves margin on both sides — the gap-edge approach risks cropping pixels
off the left edge of the right container.

**Recommendation:** keep `steel_wall_midpoint` as the production primary for
now. Claude Vision is a useful oracle for disagreement detection, not a
replacement, on this shape of image.

### My eyeball baseline was worse than either method
Downscaled-display human estimation had a mean abs delta of ~35 px vs
claude_vision, with two outliers at −47 and −102. Lesson: do not use
Read-tool-rendered eyeball estimates as a proxy for ground truth on
splitter-accuracy tasks.

### Production dispatch recommendation (unchanged from the earlier draft)
**Only dispatch TRUE cross-record scans to the splitter.** 5 of 9 test jobs
contained vehicles — these are overwhelmingly same-importer multi-car
shipments that should never have been split at all. Adding a pre-filter
(vehicle detection → skip the splitter) eliminates most of the splitter's
current workload and lets the remaining calls go through Claude Vision for
the small number of genuinely heterogeneous cross-record cases.

At ~500 cross-record scans per month after filtering, Claude Vision costs
**~$2.80 per month** at current pricing. Effectively free.

## Bugs fixed along the way (for 1.20.0 release notes)

- `services/image-splitter/requirements.txt`: bumped `anthropic==0.40.0` →
  `anthropic==0.92.0`; package now actually installed in production venv.
- Splitter NSSM service: `ANTHROPIC_API_KEY` now configured via
  `AppEnvironmentExtra` (service-scoped, not system-wide).
- No source code changes to `claude_vision.py` itself — the 1.19.0
  implementation is correct, it had just never run in production due to the
  missing package install.
