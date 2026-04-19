"""
Claude Vision splitting strategy (1.19.0)

Uses Anthropic's Claude Vision API to identify the split point between two
containers in an X-ray scan. Claude Vision sees the image globally and
understands the semantic structure ("two containers side by side with a gap
between them") in a way that hand-engineered heuristics cannot.

This is intended as the teacher in a teacher/student architecture:
- Claude Vision is the oracle that establishes accurate splits on scans that
  the other strategies disagree on or score low confidence
- Analysts still make the final call via the /annotate tool
- Eventually, a local student model can be trained from Claude's labels

Environment:
    ANTHROPIC_API_KEY        — required. Strategy returns None if unset.
    CLAUDE_VISION_MODEL      — default: claude-sonnet-4-5
    CLAUDE_VISION_MAX_RES    — default: 1568 (Anthropic's recommended max)

Cost: roughly $0.003–$0.008 per call at 1568px (Sonnet 4.5 pricing).
Latency: ~3–5 seconds per call.
"""
import base64
import io
import json
import logging
import os
import time
from typing import Optional

import numpy as np
from PIL import Image

from strategies.base import BaseSplitStrategy, SplitResult

logger = logging.getLogger(__name__)

DEFAULT_MODEL = os.environ.get("CLAUDE_VISION_MODEL", "claude-sonnet-4-5")
MAX_RES = int(os.environ.get("CLAUDE_VISION_MAX_RES", "1568"))


class ClaudeVisionStrategy(BaseSplitStrategy):
    """Ask Claude Vision for the split X coordinate between two containers."""

    @property
    def name(self) -> str:
        return "claude_vision"

    def __init__(self):
        self._client = None
        self._api_key_present = bool(os.environ.get("ANTHROPIC_API_KEY"))

    def _get_client(self):
        """Lazy import + init so the splitter doesn't hard-depend on the anthropic package."""
        if self._client is not None:
            return self._client
        try:
            from anthropic import Anthropic
        except ImportError:
            logger.warning(
                "[claude_vision] anthropic package not installed. "
                "Add 'anthropic' to requirements.txt to enable this strategy."
            )
            return None

        api_key = os.environ.get("ANTHROPIC_API_KEY")
        if not api_key:
            logger.info("[claude_vision] ANTHROPIC_API_KEY not set — strategy disabled")
            return None

        self._client = Anthropic(api_key=api_key)
        return self._client

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        client = self._get_client()
        if client is None:
            return None

        h, w = image_array.shape[:2]

        # Downsample to MAX_RES on the long side. Anthropic's vision docs recommend
        # keeping the long edge at 1568 or below to avoid unnecessary token spend.
        downsampled_bytes, scale = self._downsample(image_data, w, h)
        if downsampled_bytes is None:
            return None

        disp_w = int(round(w * scale))
        disp_h = int(round(h * scale))

        # HYBRID PROMPT — v1 simplicity + operator's casting-edge correction.
        #
        # Tested 3x on job 4c3c7a6d (operator-verified truth=662):
        #   v1 simple alone:    649, 650, 650  (12-13 px off, uses casting START)
        #   v3 verbose+temp=0:  801            (139 px off, locks onto cargo)
        #   THIS hybrid:        661, 663, 661  (1 px off, stable, uses casting END)
        #
        # The key insight from the operator: "635 is the beginning of the
        # casting. it needs to take the END of the casting as the end of
        # the container which is x=658." Adding just this one correction
        # to the simple prompt produces 1-pixel accuracy.
        #
        # DO NOT add more instructions — verbose prompts cause Claude to
        # over-think and lock onto wrong features. Keep it short.
        prompt = (
            "You are analyzing an X-ray scan of two shipping containers placed "
            "side by side on a freight trailer. This is a 2D X-ray projection, "
            "not a photograph. Your job is to identify the EXACT X coordinate "
            "(pixel column) of the vertical line that best separates the left "
            "container from the right container.\n\n"
            "IMPORTANT: Look at the TOP 25% of the image first — that is where "
            "the corner castings (dark rectangular steel blocks at each corner of "
            "each container) are visible without cargo interference.\n\n"
            "CRITICAL RULE: A corner casting is 20-30 px wide. The container "
            "boundary is at the OUTER EDGE of the casting, NOT the inner edge. "
            "If you see a casting from x=637 to x=661, the container ENDS at "
            "x=661, NOT at x=637. The split is the midpoint between the OUTER "
            "edges of the two inner castings.\n\n"
            f"Image: {disp_w}px wide x {disp_h}px tall. Return split_x in THIS "
            "downsampled coordinate system.\n\n"
            "Return a single JSON object and nothing else:\n"
            '{"split_x": <integer pixel column>, "confidence": <float 0..1>, '
            '"reasoning": "<one sentence explaining your choice>"}'
        )

        # DO NOT set temperature=0 — Claude's default temperature produces
        # more reliable results on this prompt. temperature=0 caused bimodal
        # locking onto wrong features (observed: 801 on 4c3c7a6d at temp=0
        # vs 661 at default temp).

        b64 = base64.standard_b64encode(downsampled_bytes).decode("ascii")
        started_at = time.monotonic()

        try:
            # Run the sync client in a thread so we don't block the event loop.
            import asyncio
            def _call():
                return client.messages.create(
                    model=DEFAULT_MODEL,
                    max_tokens=800,
                    # temperature intentionally NOT set (uses Claude's default).
                                       # bimodal variance observed on 4c3c7a6d
                                       # (runs 1&2 gave 802, run 3 gave 663 —
                                       # different feature clusters selected).
                    messages=[
                        {
                            "role": "user",
                            "content": [
                                {
                                    "type": "image",
                                    "source": {
                                        "type": "base64",
                                        "media_type": "image/jpeg",
                                        "data": b64,
                                    },
                                },
                                {"type": "text", "text": prompt},
                            ],
                        }
                    ],
                )
            response = await asyncio.to_thread(_call)
            elapsed_ms = int((time.monotonic() - started_at) * 1000)
        except Exception as e:
            logger.error(f"[claude_vision] API call failed: {e}", exc_info=True)
            return None

        text = ""
        try:
            for block in response.content:
                if getattr(block, "type", None) == "text":
                    text += block.text
        except Exception:
            text = str(response)

        parsed = self._extract_json(text)
        if parsed is None:
            logger.warning(f"[claude_vision] Could not extract JSON from response: {text[:200]}")
            return None

        try:
            downsampled_split_x = int(parsed["split_x"])
            claude_confidence = float(parsed.get("confidence", 0.8))
            reasoning = str(parsed.get("reasoning", ""))[:500]
            # 1.20.x — operator-verified casting edge fields. These are the
            # OUTER edges of the two inner corner castings that define the
            # inter-container gap. We capture them for post-hoc analysis
            # (did split_x actually = (c1_right + c2_left)/2?) and for the
            # /diagnose UI.
            c1_right_casting_x_end = parsed.get("c1_right_casting_x_end")
            c2_left_casting_x_start = parsed.get("c2_left_casting_x_start")
        except (KeyError, TypeError, ValueError) as e:
            logger.warning(f"[claude_vision] Malformed JSON: {parsed} ({e})")
            return None

        # Scale split_x back to the original image coordinate system
        split_x = int(round(downsampled_split_x / scale))
        if split_x <= 0 or split_x >= w:
            logger.warning(f"[claude_vision] split_x {split_x} out of bounds [0, {w}]")
            return None

        # Hybrid prompt with operator's casting-edge correction produces 1-pixel
        # accuracy (tested 3x on 4c3c7a6d: 661, 663, 661 vs truth 662).
        # The Claude verifier in the orchestrator is now the primary decision-maker
        # and always runs, so the confidence value here doesn't need to be boosted
        # to "win" the sort — the verifier picks the best candidate regardless.
        # Use the original 0.95 haircut so the confidence reflects Claude's actual
        # uncertainty.
        confidence = max(0.0, min(1.0, claude_confidence * 0.95))

        # Token usage for cost audit
        usage = {}
        try:
            if hasattr(response, "usage") and response.usage is not None:
                usage = {
                    "input_tokens": response.usage.input_tokens,
                    "output_tokens": response.usage.output_tokens,
                }
        except Exception:
            pass

        return SplitResult(
            strategy_name=self.name,
            split_x=split_x,
            confidence=confidence,
            processing_ms=elapsed_ms,
            metadata={
                "claude_raw_split_x": downsampled_split_x,
                "claude_confidence": claude_confidence,
                "reasoning": reasoning,
                "c1_right_casting_x_end": c1_right_casting_x_end,
                "c2_left_casting_x_start": c2_left_casting_x_start,
                "downsampled_width": int(round(w * scale)),
                "downsampled_height": int(round(h * scale)),
                "downsample_scale": round(scale, 4),
                "model": DEFAULT_MODEL,
                "usage": usage,
            },
        )

    @staticmethod
    def _downsample(image_data: bytes, orig_w: int, orig_h: int):
        """Return (downsampled JPEG bytes, scale) where scale < 1 means the image was shrunk."""
        long_edge = max(orig_w, orig_h)
        if long_edge <= MAX_RES:
            return image_data, 1.0
        scale = MAX_RES / long_edge
        try:
            img = Image.open(io.BytesIO(image_data))
            if img.mode not in ("RGB", "L"):
                img = img.convert("RGB")
            new_size = (int(round(orig_w * scale)), int(round(orig_h * scale)))
            img = img.resize(new_size, Image.LANCZOS)
            buf = io.BytesIO()
            img.save(buf, format="JPEG", quality=88)
            return buf.getvalue(), scale
        except Exception as e:
            logger.error(f"[claude_vision] Downsample failed: {e}")
            return None, 1.0

    @staticmethod
    def _extract_json(text: str):
        """Extract the first JSON object from a response string."""
        if not text:
            return None
        # Find the first { and matching }
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
