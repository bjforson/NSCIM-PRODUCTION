# Splitter Accuracy: Ground Truth Benchmark (2026-04-08)

## Setup

- **Annotation tool**: new `/groundtruth` page on the splitter service
  (`services/image-splitter/static/groundtruth.html`). Operator clicks to set
  the TRUE inter-container split for each image.
- **Dataset**: 19 multi-container X-ray scans drawn from the production
  `image_split_jobs` table. All click-annotated by the operator (Jonathan).
  None marked unusable.
- **Strategies measured**: `claude_vision` (1.19.0 original prompt),
  `steel_wall_midpoint` (legacy primary).

## Results

| strategy | n | mean abs err | median err | max err | ≤5px | ≤15px |
|---|---|---|---|---|---|---|
| **claude_vision** (1.19.0 prompt) | 19 | **10.7 px** | **3 px** | 126 px | **79%** (15/19) | **95%** (18/19) |
| steel_wall_midpoint | 19 | 12.4 px | 13 px | 35 px | 37% (7/19) | 63% (12/19) |

## Key findings

1. **claude_vision is nearly pixel-perfect on the typical image.** Median error
   is 3 pixels. 79% of images come back within 5 pixels of ground truth. If
   you exclude the single catastrophic outlier (#3), the mean drops to ~4 px.

2. **claude_vision has one catastrophic failure mode.** On job `48099d95`
   (TGBU2254714,UETU2837554, 1539×544), Claude returns the exact image
   midpoint (770) with reasoning *"The darkest and most prominent vertical
   line indicating the gap between container walls is located at this
   position"* — but the true gap is at 644. That's 126 pixels wrong. Both
   the 1.19.0 prompt and the "improved" 1.20.x prompt fail on this image
   in the same way. Some images lack an obvious inter-container landmark
   and Claude defaults to the image center.

3. **steel_wall_midpoint has a systematic POSITIVE bias.** Across all 19
   images, 13 of the errors are positive (overshooting to the right) and
   none are strongly negative. Mean error is +8 px, max +35 px. This is
   consistent with taking the midpoint of the two outer trailer walls —
   which lands right of the true gap whenever the right container's cargo
   extends visually past the gap.

4. **steel_wall_midpoint is MORE predictable despite being less accurate.**
   Its errors are bounded at ±35 px; claude_vision can miss by 126 px.

## The failed "1.20.x improvement" experiment

Before pulling ground truth, I assumed the bad split positions we were
seeing were a 20ft+40ft asymmetric-trailer geometry problem and rewrote
the Claude Vision prompt to force it toward a 33% / 50% / 67% snap with
corner-casting identification and a width-ratio sanity check.

**The improved prompt was catastrophically worse** — it took claude_vision
from a 3-pixel median error to a 96-pixel mean error on 9 backfilled jobs,
with individual errors up to 141 px. The problem was that I was
eyeballing downscaled Read-tool images and convinced myself of a geometry
that wasn't actually there. The 19-image ground-truth set shows splits
cluster at 40-42% of image width, NOT at the 33%/50%/67% snap points.

**Lessons:**
- Never make prompt changes without ground truth first.
- Low-resolution visual estimation (Read tool) is unreliable for pixel-
  accurate judgments.
- When two strategies converge on the same "wrong" answer (claude=653 and
  steel=656), they are probably both converging on the RIGHT answer and
  the eyeball is wrong.

## Deployed fix

1. **Revert** `strategies/claude_vision.py` to the 1.19.0 prompt.
2. **Promote claude_vision to primary** in the orchestrator via a confidence
   bump (`claude_confidence + 0.05` instead of `claude_confidence * 0.95`)
   so it wins the best-sort over steel_wall's 0.877.
3. **Add a disagreement guard** in `pipeline/orchestrator.py`: if
   `|claude_vision - steel_wall_midpoint| > 50 px`, Claude is probably in a
   catastrophic-failure mode on this image, so drop its confidence below
   steel_wall's and let the fallback primary win. The claude_vision result
   row still gets written so the review UI can surface the discrepancy for
   manual inspection. Metadata flag:
   `disagreement_guard_triggered = true`.
4. **Backfill all 50 pre-1.20.0 jobs** with the reverted prompt so every
   job on the review page has a complete strategy row set.

## Expected post-fix accuracy (on this 19-image dataset)

| case | handled by | expected err |
|---|---|---|
| Typical scan (disagreement ≤ 50 px, 18 of 19 images) | claude_vision primary | median 3 px, 79% within 5 px |
| Outlier (disagreement > 50 px, 1 of 19 images) | steel_wall_midpoint fallback | bounded to steel's ±35 px range |

Effective overall error budget: **bounded at ~35 px worst case**, **median 3 px
typical**, a strict improvement over either strategy alone.
