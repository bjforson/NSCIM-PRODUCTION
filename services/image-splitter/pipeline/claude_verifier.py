"""
Claude-as-verifier (1.20.x).

Instead of asking Claude to produce an absolute pixel coordinate (a regression
task at which Claude is unreliable, as seen on job 4c3c7a6d where three runs
bimodally produced 802 / 802 / 663 on the same image), this module uses
Claude as a RANKING/CLASSIFICATION agent over candidates produced by the
hand-engineered strategies.

Pipeline:
    1. Collect (strategy_name, split_x) from every successful strategy.
    2. Deduplicate candidates within DEDUP_PX pixels of each other (same
       answer, different strategy).
    3. Draw each unique candidate as a labeled colored vertical line on a
       downsampled copy of the image.
    4. Send the annotated image to Claude with a ranking prompt asking it
       to pick which labeled line is ON the real inter-container gap.
    5. Parse the pick, look up the original strategy, return a VerifierPick.

This is much more reliable than pixel regression because:
    - Claude is great at image-to-class mapping (the ABC/ABCD ranking is
      classification, not regression).
    - The candidates are already close to the right answer (the hand-
      engineered strategies land within ~35 px on most images).
    - Even if Claude is slightly uncertain between two candidates, the
      answer is bounded by the hand-engineered strategies' range.
"""
import base64
import io
import json
import logging
import os
import time
from dataclasses import dataclass
from typing import List, Optional, Tuple

from PIL import Image, ImageDraw, ImageFont

logger = logging.getLogger(__name__)

DEDUP_PX = 15  # candidates within this distance are treated as the same answer
MAX_RES = int(os.environ.get("CLAUDE_VISION_MAX_RES", "1568"))
MODEL = os.environ.get("CLAUDE_VISION_MODEL", "claude-sonnet-4-5")

# 1.20.x — Few-shot learning loop.
# Number of worked examples to sample from splitter_consensus_corpus and
# inject into the verifier prompt before the target image. Each example
# becomes an <example> image block showing the correct inter-container
# gap with a green "CORRECT" vertical line.
FEW_SHOT_COUNT = int(os.environ.get("SPLITTER_FEW_SHOT_COUNT", "3"))

# Distinct colors for up to 8 candidates. Red/green/blue/yellow/magenta/cyan/
# orange/white — all chosen for high contrast on an X-ray (grayscale) image.
CANDIDATE_COLORS = [
    "#FF3030",  # A — red
    "#30FF30",  # B — green
    "#3060FF",  # C — blue
    "#FFFF30",  # D — yellow
    "#FF30FF",  # E — magenta
    "#30FFFF",  # F — cyan
    "#FF9030",  # G — orange
    "#FFFFFF",  # H — white
]


@dataclass
class VerifierPick:
    picked_strategy: str
    picked_split_x: int
    picked_label: str           # "A", "B", ...
    ranking: List[str]          # full ordered list of labels from best to worst
    reasoning: str
    claude_confidence: float    # 0..1 self-reported
    candidates_offered: List[Tuple[str, int, str]]  # (strategy_name, split_x, label) as shown to Claude
    annotated_image_bytes: bytes  # the image that was sent to Claude, for /diagnose display
    latency_ms: int
    usage: dict
    few_shot_count: int = 0     # number of corpus examples injected as exemplars


def _sample_corpus_examples(n: int, target_width: Optional[int] = None) -> List[dict]:
    """Pull N random rows from splitter_consensus_corpus, optionally stratified
    by image width bucket.

    If target_width is provided, sampling is biased toward corpus rows whose
    image_width falls in the same bucket as the target:
      - "small"  : image_width <  900   (asescan-decoded bitmaps)
      - "medium" : 900 <= image_width < 1400
      - "large"  : image_width >= 1400  (original scanner output)

    Claude's visual comparison is only meaningful when the example images
    are at the same scale as the target. Cross-bucket examples confuse the
    model because absolute coordinates are incomparable.

    Returns [] if the corpus is empty, DB is unreachable, or psycopg2 is
    not installed — the verifier degrades gracefully to zero-shot.
    """
    if n <= 0:
        return []
    try:
        import psycopg2
        from psycopg2.extras import RealDictCursor
    except ImportError:
        return []
    try:
        from config import DATABASE_URL_SYNC
        dsn = DATABASE_URL_SYNC.replace("postgresql+psycopg2://", "postgresql://", 1)
        conn = psycopg2.connect(dsn)
    except Exception as e:
        logger.warning(f"[few_shot] could not connect to corpus DB: {e}")
        return []

    def _bucket(w: int) -> str:
        if w < 900:
            return "small"
        if w < 1400:
            return "medium"
        return "large"

    try:
        cur = conn.cursor(cursor_factory=RealDictCursor)

        # First try: examples in the same bucket as the target
        rows = []
        if target_width is not None:
            target_bucket = _bucket(target_width)
            if target_bucket == "small":
                where = "image_width < 900"
            elif target_bucket == "medium":
                where = "image_width BETWEEN 900 AND 1399"
            else:
                where = "image_width >= 1400"
            cur.execute(
                f"""
                SELECT image_data, image_width, image_height, verified_split_x,
                       c1_right_casting_x_end, c2_left_casting_x_start
                  FROM splitter_consensus_corpus
                 WHERE {where}
                 ORDER BY random()
                 LIMIT %s
                """,
                (n,),
            )
            rows = cur.fetchall()
            logger.info(
                f"[few_shot] target_width={target_width} bucket={target_bucket} "
                f"→ {len(rows)} in-bucket example(s)"
            )

        # Fallback: if we didn't get enough from the target bucket, top up
        # with random examples from any bucket. This ensures few-shot
        # always runs even when the corpus is thin for a particular size.
        if len(rows) < n:
            missing = n - len(rows)
            exclude_ids = tuple(r["image_data"] for r in rows) if rows else ()
            cur.execute(
                """
                SELECT image_data, image_width, image_height, verified_split_x,
                       c1_right_casting_x_end, c2_left_casting_x_start
                  FROM splitter_consensus_corpus
                 ORDER BY random()
                 LIMIT %s
                """,
                (missing + len(rows),),  # overfetch so we can dedupe by image_data
            )
            extra = cur.fetchall()
            seen = {bytes(r["image_data"]) for r in rows}
            for r in extra:
                if bytes(r["image_data"]) in seen:
                    continue
                rows.append(r)
                seen.add(bytes(r["image_data"]))
                if len(rows) >= n:
                    break
            logger.info(
                f"[few_shot] topped up with cross-bucket fallback → {len(rows)} total"
            )

        return [
            {
                "image_data": bytes(r["image_data"]),
                "image_width": r["image_width"],
                "image_height": r["image_height"],
                "verified_split_x": r["verified_split_x"],
                "c1_right_casting_x_end": r["c1_right_casting_x_end"],
                "c2_left_casting_x_start": r["c2_left_casting_x_start"],
            }
            for r in rows
        ]
    finally:
        conn.close()


def _annotate_example_strip(
    image_data: bytes,
    verified_split_x: int,
    c1_right: Optional[int],
    c2_left: Optional[int],
) -> bytes:
    """Draw the verified split as a GREEN line on a top-strip crop of an
    example image, plus (if available) small markers at the casting outer
    edges. Returns JPEG bytes."""
    strip_bytes, scale, crop_w, crop_h, _ = _top_strip_crop(image_data)
    img = Image.open(io.BytesIO(strip_bytes))
    if img.mode != "RGB":
        img = img.convert("RGB")
    draw = ImageDraw.Draw(img)
    try:
        font_big = ImageFont.truetype("arial.ttf", 28)
        font_sm = ImageFont.truetype("arial.ttf", 16)
    except (OSError, IOError):
        try:
            font_big = ImageFont.truetype("DejaVuSans-Bold.ttf", 28)
            font_sm = ImageFont.truetype("DejaVuSans.ttf", 16)
        except (OSError, IOError):
            font_big = ImageFont.load_default()
            font_sm = ImageFont.load_default()

    w, h = img.size
    # Split line
    sx = int(round(verified_split_x * scale))
    if 0 <= sx < w:
        draw.line([(sx, 0), (sx, h)], fill="#00ff88", width=5)

    # Casting edge markers — two short downticks if available
    for edge_x, label in (
        (c1_right, "C1→"),
        (c2_left, "←C2"),
    ):
        if edge_x is None:
            continue
        ex = int(round(edge_x * scale))
        if 0 <= ex < w:
            draw.line([(ex, 0), (ex, 20)], fill="#ffee00", width=3)
            draw.line([(ex, h - 20), (ex, h)], fill="#ffee00", width=3)

    # CORRECT label at the bottom
    label_text = f"CORRECT split: {verified_split_x}"
    box_w = 250
    box_h = 36
    box_x = max(2, min(w - box_w - 2, sx - box_w // 2))
    box_y = h - box_h - 6
    draw.rectangle([(box_x, box_y), (box_x + box_w, box_y + box_h)], fill="#00ff88")
    draw.text((box_x + 8, box_y + 4), label_text, fill="#000000", font=font_big)

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=88)
    return buf.getvalue()


def _top_strip_crop(image_data: bytes) -> Tuple[bytes, float, int, int, int]:
    """Crop the TOP 30% of the image at native resolution (no downsample) so
    corner castings have maximum pixel density for Claude to see.

    Returns:
      (cropped JPEG bytes, scale=1.0, cropped_width, cropped_height, orig_h)

    The returned crop is at the same x resolution as the original (no
    horizontal scaling), so candidate split_x values in the ORIGINAL
    coordinate system map directly to the crop's x coordinates. The y
    coordinate is truncated to the top 30% of the original image.
    """
    img = Image.open(io.BytesIO(image_data))
    if img.mode not in ("RGB", "L"):
        img = img.convert("RGB")
    orig_w, orig_h = img.size
    crop_h = int(round(orig_h * 0.30))
    cropped = img.crop((0, 0, orig_w, crop_h))

    # If the crop is still too wide for Claude's image size constraint, scale
    # horizontally only. Claude has a practical limit around 1568 px long edge;
    # for scans that are 1500-1700 px wide we're within budget. For anything
    # wider, downsample proportionally.
    scale = 1.0
    if orig_w > MAX_RES:
        scale = MAX_RES / orig_w
        new_w = int(round(orig_w * scale))
        new_h = int(round(crop_h * scale))
        cropped = cropped.resize((new_w, new_h), Image.LANCZOS)
    buf = io.BytesIO()
    cropped.save(buf, format="JPEG", quality=92)
    return buf.getvalue(), scale, cropped.size[0], cropped.size[1], orig_h


def _dedup_candidates(results) -> List[Tuple[str, int]]:
    """Return a list of unique (representative_strategy_name, split_x), sorted
    by split_x ascending. Two candidates within DEDUP_PX are merged; the
    representative name is the one with higher confidence.

    Excludes any result whose metadata has cv_center_guard_triggered=True
    (a giveaway signal that Claude Vision defaulted to the image center on
    a difficult image — we don't want to let that into the verifier's
    candidate pool, or the verifier may pick it back).
    """
    raw = []
    for r in results:
        if r is None or r.split_x is None:
            continue
        meta = getattr(r, "metadata", None) or getattr(r, "strategy_metadata", None) or {}
        if isinstance(meta, dict) and meta.get("cv_center_guard_triggered"):
            continue
        raw.append((r.strategy_name, int(r.split_x), float(r.confidence or 0.0)))
    raw.sort(key=lambda t: t[1])

    merged: List[Tuple[str, int, float]] = []
    for name, x, conf in raw:
        if merged and abs(merged[-1][1] - x) <= DEDUP_PX:
            # Merge with previous: keep higher-confidence name, average x
            prev = merged[-1]
            if conf > prev[2]:
                merged[-1] = (name, (prev[1] + x) // 2, conf)
            else:
                merged[-1] = (prev[0], (prev[1] + x) // 2, prev[2])
        else:
            merged.append((name, x, conf))
    return [(name, x) for name, x, _ in merged]


def _annotate_image(image_bytes: bytes, candidates: List[Tuple[str, int, str]], scale: float) -> bytes:
    """Draw each candidate as a labeled colored vertical line on the image.

    candidates is a list of (strategy_name, split_x_ORIGINAL_COORDS, label).
    split_x is in the ORIGINAL image coordinate system and is scaled down
    here to match the downsampled image we just produced.
    """
    img = Image.open(io.BytesIO(image_bytes))
    if img.mode != "RGB":
        img = img.convert("RGB")
    w, h = img.size
    draw = ImageDraw.Draw(img)

    # Try to use a larger font if available; fall back to default
    font_label = None
    font_split = None
    try:
        font_label = ImageFont.truetype("arial.ttf", 28)
        font_split = ImageFont.truetype("arial.ttf", 16)
    except (OSError, IOError):
        try:
            font_label = ImageFont.truetype("DejaVuSans-Bold.ttf", 28)
            font_split = ImageFont.truetype("DejaVuSans.ttf", 16)
        except (OSError, IOError):
            font_label = ImageFont.load_default()
            font_split = ImageFont.load_default()

    # Label boxes live at the BOTTOM of the strip so they don't occlude the
    # roof line / castings that Claude needs to see at the top. Stagger
    # horizontally if candidates are close together.
    for i, (name, split_x_orig, label) in enumerate(candidates):
        color = CANDIDATE_COLORS[i % len(CANDIDATE_COLORS)]
        x = int(round(split_x_orig * scale))
        if x < 0 or x >= w:
            continue
        # Thick vertical line spanning the full crop height
        draw.line([(x, 0), (x, h)], fill=color, width=4)
        # Label box at the bottom
        box_w = 52
        box_h = 34
        box_x = max(2, min(w - box_w - 2, x - box_w // 2))
        box_y = h - box_h - 6
        draw.rectangle(
            [(box_x - 2, box_y - 2), (box_x + box_w + 2, box_y + box_h + 2)],
            fill="#000000",
        )
        draw.rectangle(
            [(box_x, box_y), (box_x + box_w, box_y + box_h)],
            fill=color,
        )
        draw.text((box_x + 10, box_y + 2), label, fill="#000000", font=font_label)

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=88)
    return buf.getvalue()


async def verify_candidates_with_claude(
    image_data: bytes,
    results,
) -> Optional[VerifierPick]:
    """Ask Claude to rank candidate splits and return the winning one.

    1.20.x v2 — uses a TOP STRIP CROP at near-native resolution rather than
    a full-image downsample. Claude was getting fooled by cargo density
    clusters in the middle of the image because the downsampled top strip
    was only ~130 px tall, which wasn't enough resolution for corner
    castings. The top strip at native resolution gives Claude a clear,
    clutter-free view of just the structural features that matter.
    """
    try:
        from anthropic import Anthropic
    except ImportError:
        logger.warning("[claude_verifier] anthropic package not installed — skipping")
        return None
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        logger.info("[claude_verifier] ANTHROPIC_API_KEY not set — skipping")
        return None

    deduped = _dedup_candidates(results)
    if len(deduped) < 2:
        logger.info(f"[claude_verifier] only {len(deduped)} unique candidate(s) — skipping (need 2+)")
        return None

    # Crop the top strip at native resolution
    strip_bytes, scale, crop_w, crop_h, orig_h = _top_strip_crop(image_data)
    labeled = [
        (name, split_x, chr(ord("A") + i))
        for i, (name, split_x) in enumerate(deduped)
    ]
    annotated_bytes = _annotate_image(strip_bytes, labeled, scale)

    candidate_lines = "\n".join(
        f"  - {label}: split_x={split_x} (from {strategy_name})"
        for strategy_name, split_x, label in labeled
    )
    prompt = (
        "You are looking at the TOP STRIP of an X-ray scan of a freight "
        "trailer. This is the upper 30% of the full scan, cropped out to "
        "give you maximum pixel density on the structural features that "
        "define container boundaries. The image has been intentionally "
        "cropped to HIDE the cargo region because cargo clusters in the "
        "middle of a container are not container boundaries and will "
        "mislead you.\n\n"
        "In this strip you should see:\n"
        "  - The ROOF LINE of each container (a horizontal dark line)\n"
        "  - TOP CORNER CASTINGS — small dark RECTANGULAR BLOCKS (about "
        "20–30 pixels wide) at the top corners of each container. A "
        "two-container trailer has 4 top corner castings (2 per container).\n"
        "  - Possibly the top edge of the trailer chassis\n\n"
        "I've overlaid several CANDIDATE vertical lines on the strip, each "
        "labeled with a letter (A, B, C, ...) in a colored box. Each "
        "candidate is a different splitting algorithm's guess at where "
        "the inter-container gap is located.\n\n"
        f"Candidates:\n{candidate_lines}\n\n"
        "## Your task\n\n"
        "For each candidate letter, look at what is UNDER the line in the "
        "strip:\n"
        "  - If the line passes through the PHYSICAL GAP between the x_end "
        "of container 1's right corner casting and the x_start of container "
        "2's left corner casting → this is the correct answer.\n"
        "  - If the line passes through a corner casting itself (inside "
        "the solid steel block) → close but slightly off by ~10-20 px.\n"
        "  - If the line is NOT near any corner casting (the strip has no "
        "dark rectangular block at that x) → WRONG. That candidate is on "
        "cargo or on nothing structural.\n\n"
        "**Important — container geometry reminder:** A standard 40 ft or "
        "20 ft container has a corner casting at EACH top corner. The "
        "boundary of the container is the OUTER edge of its corner casting "
        "block. For the left container ending on the right, that's the "
        "x_END of its right casting. For the right container beginning on "
        "the left, that's the x_START of its left casting. The gap between "
        "them is usually only 2–15 pixels wide — the physical steel-to-"
        "steel gap between two containers touching end-to-end on the "
        "chassis.\n\n"
        "Rank the candidates from MOST LIKELY (on the real gap) to LEAST "
        "LIKELY. Pick a winner.\n\n"
        "## Output (strict JSON)\n\n"
        "Return a single JSON object and nothing else:\n"
        '{\n'
        '  "picked": "<letter>",\n'
        '  "ranking": ["<letter>", ...],\n'
        '  "confidence": <float 0..1>,\n'
        '  "reasoning": "<explain: for the winner, say what visual feature '
        'is under its line (e.g. a dark rectangular block between two '
        'castings). For each other candidate, say why you rejected it '
        '(e.g. line A is at x=650 which is in the middle of container 1 '
        'cargo with no casting visible above it)>"\n'
        '}'
    )

    # 1.20.x — Few-shot example injection. Sample N random rows from
    # splitter_consensus_corpus, annotate each as a top-strip with the
    # verified split line, and prepend them to the content array as
    # <example> blocks. Each example consumes ~1200 input tokens but
    # anchors Claude's visual perception to the actual scan distribution.
    try:
        import asyncio
        # Pass the ORIGINAL (pre-crop) image width so the corpus sampler
        # stratifies by scale. crop_w is the width of the top-strip crop,
        # which equals the original width for jpegs under MAX_RES (most
        # asescan bitmaps), but a downsampled width for very large scans.
        # Use crop_w since that's what Claude actually sees in the prompt.
        target_w = crop_w
        corpus_examples = await asyncio.to_thread(
            _sample_corpus_examples, FEW_SHOT_COUNT, target_w
        )
    except Exception as e:
        logger.warning(f"[few_shot] corpus sampling failed: {e}")
        corpus_examples = []
    logger.info(f"[few_shot] sampled {len(corpus_examples)} examples from corpus")

    content_blocks: List[dict] = []
    if corpus_examples:
        content_blocks.append({
            "type": "text",
            "text": (
                f"I'll show you {len(corpus_examples)} WORKED EXAMPLES first so "
                "you can calibrate your visual perception to what a correct "
                "inter-container gap looks like on these X-ray scans. Each "
                "example has the CORRECT split marked with a green vertical "
                "line labeled 'CORRECT split: N' at the bottom, plus small "
                "yellow tick marks at the outer edges of the two inner corner "
                "castings (C1→ on the left, ←C2 on the right). Study these, "
                "then apply the same visual reasoning to the target image "
                "that follows.\n"
            ),
        })
        for i, ex in enumerate(corpus_examples, 1):
            try:
                ex_bytes = _annotate_example_strip(
                    ex["image_data"],
                    ex["verified_split_x"],
                    ex.get("c1_right_casting_x_end"),
                    ex.get("c2_left_casting_x_start"),
                )
            except Exception as e:
                logger.warning(f"[few_shot] could not annotate example {i}: {e}")
                continue
            ex_b64 = base64.standard_b64encode(ex_bytes).decode("ascii")
            content_blocks.append({
                "type": "text",
                "text": (
                    f"<example {i}>\n"
                    f"Image width: {ex['image_width']}px. "
                    f"Verified split_x (ground truth): {ex['verified_split_x']}. "
                    + (
                        f"Container 1 right casting x_end: {ex['c1_right_casting_x_end']}. "
                        if ex.get("c1_right_casting_x_end") is not None else ""
                    )
                    + (
                        f"Container 2 left casting x_start: {ex['c2_left_casting_x_start']}. "
                        if ex.get("c2_left_casting_x_start") is not None else ""
                    )
                    + "Note how the gap is a narrow 2–15 px region between two "
                    "distinct dark rectangular blocks in the top strip.\n"
                ),
            })
            content_blocks.append({
                "type": "image",
                "source": {
                    "type": "base64",
                    "media_type": "image/jpeg",
                    "data": ex_b64,
                },
            })
            content_blocks.append({"type": "text", "text": f"</example {i}>\n"})
        content_blocks.append({
            "type": "text",
            "text": (
                "\n---\n\nNow here is the TARGET image you need to evaluate. "
                "Apply the same reasoning as the examples above:\n"
            ),
        })

    b64 = base64.standard_b64encode(annotated_bytes).decode("ascii")
    content_blocks.append({
        "type": "image",
        "source": {
            "type": "base64",
            "media_type": "image/jpeg",
            "data": b64,
        },
    })
    content_blocks.append({"type": "text", "text": prompt})

    client = Anthropic(api_key=api_key)
    started = time.monotonic()
    try:
        import asyncio
        def _call():
            return client.messages.create(
                model=MODEL,
                max_tokens=800,
                temperature=0.0,
                messages=[{"role": "user", "content": content_blocks}],
            )
        response = await asyncio.to_thread(_call)
    except Exception as e:
        logger.error(f"[claude_verifier] API call failed: {e}")
        return None
    elapsed_ms = int((time.monotonic() - started) * 1000)

    # Parse JSON response
    text = ""
    for block in response.content:
        if getattr(block, "type", None) == "text":
            text += block.text
    parsed = _extract_json(text)
    if parsed is None:
        logger.warning(f"[claude_verifier] Could not extract JSON from response: {text[:200]}")
        return None

    try:
        picked = str(parsed["picked"]).strip().upper()
        ranking = [str(x).strip().upper() for x in parsed.get("ranking", [])]
        reasoning = str(parsed.get("reasoning", ""))[:800]
        conf = float(parsed.get("confidence", 0.7))
    except (KeyError, TypeError, ValueError) as e:
        logger.warning(f"[claude_verifier] Malformed JSON: {parsed} ({e})")
        return None

    # Look up the picked candidate
    picked_entry = next((c for c in labeled if c[2] == picked), None)
    if picked_entry is None:
        logger.warning(f"[claude_verifier] Claude picked unknown label {picked!r}")
        return None

    usage = {}
    try:
        if response.usage is not None:
            usage = {
                "input_tokens": response.usage.input_tokens,
                "output_tokens": response.usage.output_tokens,
            }
    except Exception:
        pass

    return VerifierPick(
        picked_strategy=picked_entry[0],
        picked_split_x=picked_entry[1],
        picked_label=picked,
        ranking=ranking,
        reasoning=reasoning,
        claude_confidence=conf,
        candidates_offered=labeled,
        annotated_image_bytes=annotated_bytes,
        latency_ms=elapsed_ms,
        usage=usage,
        few_shot_count=len(corpus_examples),
    )


def _extract_json(text: str):
    if not text:
        return None
    start = text.find("{")
    if start == -1:
        return None
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                try:
                    return json.loads(text[start : i + 1])
                except json.JSONDecodeError:
                    return None
    return None
