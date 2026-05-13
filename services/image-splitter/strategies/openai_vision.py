"""
OpenAI vision teacher provider for the image splitter.

This provider is disabled by default and intended for shadow/advisory use.
It never writes to persistence directly and returns None unless both
OPENAI_VISION_ENABLED and OPENAI_API_KEY are present. Callers that include
its metadata in production results must keep the deterministic splitter as
the source of truth.
"""

import asyncio
import base64
import io
import json
import logging
import math
import os
import time
from dataclasses import dataclass
from typing import Any, Optional, Sequence

import numpy as np
from PIL import Image

from config import (
    OPENAI_VISION_ENABLED,
    OPENAI_VISION_MAX_RES,
    OPENAI_VISION_MODEL,
    OPENAI_VISION_TIMEOUT_SECONDS,
)
from strategies.base import BaseSplitStrategy, SplitResult

logger = logging.getLogger(__name__)


ASSESSMENT_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "properties": {
        "is_two_container_image": {"type": "boolean"},
        "split_x": {
            "anyOf": [
                {"type": "integer"},
                {"type": "null"},
            ]
        },
        "confidence": {
            "type": "number",
            "minimum": 0,
            "maximum": 1,
        },
        "reasoning": {"type": "string"},
        "should_require_human_review": {"type": "boolean"},
    },
    "required": [
        "is_two_container_image",
        "split_x",
        "confidence",
        "reasoning",
        "should_require_human_review",
    ],
}


@dataclass(frozen=True)
class OpenAIVisionAssessment:
    """Validated OpenAI advisory assessment.

    split_x is scaled back to original image coordinates. raw_split_x is the
    coordinate returned by OpenAI in the downsampled/displayed image.
    """

    is_two_container_image: bool
    split_x: Optional[int]
    raw_split_x: Optional[int]
    confidence: float
    reasoning: str
    should_require_human_review: bool
    model: str
    latency_ms: int
    usage: dict
    downsampled_width: int
    downsampled_height: int
    downsample_scale: float
    purpose: str

    def to_metadata(self) -> dict:
        return {
            "provider": "openai",
            "model": self.model,
            "purpose": self.purpose,
            "is_two_container_image": self.is_two_container_image,
            "split_x": self.split_x,
            "raw_split_x": self.raw_split_x,
            "confidence": round(float(self.confidence), 4),
            "reasoning": self.reasoning,
            "should_require_human_review": self.should_require_human_review,
            "latency_ms": self.latency_ms,
            "usage": self.usage,
            "downsampled_width": self.downsampled_width,
            "downsampled_height": self.downsampled_height,
            "downsample_scale": round(float(self.downsample_scale), 6),
            "advisory_only": True,
            "affects_deterministic_split": False,
        }


class OpenAIVisionStrategy(BaseSplitStrategy):
    """Direct OpenAI split assessment strategy.

    The orchestrator keeps this provider shadow-only. This class exists for
    direct teacher calls and future offline labeling utilities.
    """

    @property
    def name(self) -> str:
        return "openai_vision"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        assessment = await assess_image_with_openai(
            image_data,
            image_array,
            purpose="teacher",
        )
        if assessment is None:
            return None
        if not assessment.is_two_container_image or assessment.split_x is None:
            logger.info("[openai_vision] image not assessed as a two-container split candidate")
            return None

        return SplitResult(
            strategy_name=self.name,
            split_x=assessment.split_x,
            confidence=assessment.confidence,
            processing_ms=assessment.latency_ms,
            metadata=assessment.to_metadata(),
        )


def is_openai_vision_enabled() -> bool:
    return bool(OPENAI_VISION_ENABLED and os.environ.get("OPENAI_API_KEY"))


async def assess_image_with_openai(
    image_data: bytes,
    image_array: Optional[np.ndarray] = None,
    *,
    candidates: Optional[Sequence[tuple[str, int]]] = None,
    deterministic_split_x: Optional[int] = None,
    purpose: str = "teacher",
) -> Optional[OpenAIVisionAssessment]:
    """Ask OpenAI Vision for a strict JSON split assessment.

    Returns None for every unavailable or unsafe state: disabled flag, missing
    API key, missing SDK, API failure, malformed JSON, or out-of-bounds split.
    """

    if not OPENAI_VISION_ENABLED:
        logger.debug("[openai_vision] OPENAI_VISION_ENABLED is false; skipping")
        return None

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        logger.info("[openai_vision] OPENAI_API_KEY not set; skipping")
        return None

    try:
        from openai import OpenAI
    except ImportError:
        logger.warning("[openai_vision] openai package not installed; skipping")
        return None

    try:
        prepared = _prepare_image(image_data, image_array)
    except Exception as exc:
        logger.warning("[openai_vision] could not prepare image: %s", exc)
        return None

    prompt = _build_prompt(
        original_width=prepared["original_width"],
        original_height=prepared["original_height"],
        display_width=prepared["display_width"],
        display_height=prepared["display_height"],
        scale=prepared["scale"],
        candidates=candidates,
        deterministic_split_x=deterministic_split_x,
        purpose=purpose,
    )
    data_url = _jpeg_data_url(prepared["image_bytes"])
    client = OpenAI(api_key=api_key, timeout=OPENAI_VISION_TIMEOUT_SECONDS)
    started = time.monotonic()

    try:
        response = await asyncio.to_thread(
            _create_chat_completion,
            client,
            OPENAI_VISION_MODEL,
            prompt,
            data_url,
        )
    except Exception as exc:
        logger.error("[openai_vision] API call failed: %s", exc, exc_info=True)
        return None

    elapsed_ms = int((time.monotonic() - started) * 1000)
    text = _extract_response_text(response)
    parsed = _extract_json(text)
    if parsed is None:
        logger.warning("[openai_vision] could not extract JSON from response: %s", text[:300])
        return None

    assessment = _validate_assessment(
        parsed,
        original_width=prepared["original_width"],
        display_width=prepared["display_width"],
        scale=prepared["scale"],
        model=OPENAI_VISION_MODEL,
        latency_ms=elapsed_ms,
        usage=_extract_usage(response),
        downsampled_width=prepared["display_width"],
        downsampled_height=prepared["display_height"],
        purpose=purpose,
    )
    if assessment is None:
        logger.warning("[openai_vision] rejected malformed or unsafe JSON: %s", parsed)
        return None
    return assessment


def _create_chat_completion(client: Any, model: str, prompt: str, data_url: str) -> Any:
    return client.chat.completions.create(
        model=model,
        temperature=0,
        max_tokens=700,
        response_format={
            "type": "json_schema",
            "json_schema": {
                "name": "container_split_assessment",
                "strict": True,
                "schema": ASSESSMENT_SCHEMA,
            },
        },
        messages=[
            {
                "role": "system",
                "content": (
                    "You are a conservative quality-control reviewer for cargo "
                    "X-ray container splitting. Return only JSON matching the "
                    "provided schema."
                ),
            },
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": prompt},
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": data_url,
                            "detail": "high",
                        },
                    },
                ],
            },
        ],
    )


def _build_prompt(
    *,
    original_width: int,
    original_height: int,
    display_width: int,
    display_height: int,
    scale: float,
    candidates: Optional[Sequence[tuple[str, int]]],
    deterministic_split_x: Optional[int],
    purpose: str,
) -> str:
    candidate_text = ""
    if candidates:
        lines = []
        for strategy_name, split_x in candidates:
            display_x = int(round(int(split_x) * scale))
            lines.append(
                f"- {strategy_name}: original_x={int(split_x)}, displayed_x={display_x}"
            )
        candidate_text = (
            "\nDeterministic splitter candidates for context only:\n"
            + "\n".join(lines)
            + "\n"
        )

    selected_text = ""
    if deterministic_split_x is not None:
        selected_text = (
            "\nThe deterministic production split currently selected is "
            f"original_x={int(deterministic_split_x)}, displayed_x="
            f"{int(round(int(deterministic_split_x) * scale))}. "
            "Your answer is advisory and must not assume this split is correct.\n"
        )

    return (
        "Review this cargo X-ray scan for dual-container splitting. The image "
        "you see may be downsampled. Coordinates in your JSON split_x MUST be "
        "in the displayed image coordinate system, not the original coordinate "
        "system.\n\n"
        f"Original image: {original_width}px wide x {original_height}px high.\n"
        f"Displayed image: {display_width}px wide x {display_height}px high.\n"
        f"Displayed scale: {scale:.6f} of original.\n"
        f"Purpose: {purpose}.\n"
        f"{candidate_text}"
        f"{selected_text}\n"
        "Look first at the top 25-30% of the scan. The true split is the "
        "vertical line through the physical gap between the outer edge of the "
        "left container's right corner casting and the outer edge of the right "
        "container's left corner casting. Cargo density in the middle of a "
        "container is not a boundary.\n\n"
        "Return exactly one JSON object with these fields and no extra keys:\n"
        "- is_two_container_image: boolean. True only when two side-by-side "
        "containers are visible enough to split.\n"
        "- split_x: integer displayed-image x coordinate for the best split, "
        "or null if is_two_container_image is false.\n"
        "- confidence: number from 0 to 1. Prefer lower confidence over a "
        "confident wrong split.\n"
        "- reasoning: one or two concise sentences tied to visible corner "
        "castings, roof lines, or the absence of a reliable two-container gap.\n"
        "- should_require_human_review: boolean. True if confidence is below "
        "0.75, if the image may be single-container, if the gap is obscured, "
        "or if candidates disagree with visible structure."
    )


def _prepare_image(image_data: bytes, image_array: Optional[np.ndarray]) -> dict:
    img = Image.open(io.BytesIO(image_data))
    if img.mode not in ("RGB", "L"):
        img = img.convert("RGB")

    original_width, original_height = img.size
    if image_array is not None:
        try:
            arr_h, arr_w = image_array.shape[:2]
            if arr_w > 0 and arr_h > 0:
                original_width = int(arr_w)
                original_height = int(arr_h)
        except Exception:
            pass

    long_edge = max(original_width, original_height)
    scale = 1.0
    if long_edge > OPENAI_VISION_MAX_RES:
        scale = OPENAI_VISION_MAX_RES / long_edge
        new_size = (
            int(round(original_width * scale)),
            int(round(original_height * scale)),
        )
        img = img.resize(new_size, Image.LANCZOS)

    display_width, display_height = img.size
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=90)
    return {
        "image_bytes": buf.getvalue(),
        "original_width": int(original_width),
        "original_height": int(original_height),
        "display_width": int(display_width),
        "display_height": int(display_height),
        "scale": float(scale),
    }


def _jpeg_data_url(image_bytes: bytes) -> str:
    b64 = base64.standard_b64encode(image_bytes).decode("ascii")
    return f"data:image/jpeg;base64,{b64}"


def _validate_assessment(
    parsed: Any,
    *,
    original_width: int,
    display_width: int,
    scale: float,
    model: str,
    latency_ms: int,
    usage: dict,
    downsampled_width: int,
    downsampled_height: int,
    purpose: str,
) -> Optional[OpenAIVisionAssessment]:
    if not isinstance(parsed, dict):
        return None
    if set(parsed.keys()) != set(ASSESSMENT_SCHEMA["required"]):
        return None

    is_two = parsed.get("is_two_container_image")
    should_review = parsed.get("should_require_human_review")
    if type(is_two) is not bool or type(should_review) is not bool:
        return None

    confidence = parsed.get("confidence")
    if type(confidence) is bool or not isinstance(confidence, (int, float)):
        return None
    confidence = float(confidence)
    if not math.isfinite(confidence) or confidence < 0.0 or confidence > 1.0:
        return None

    reasoning = parsed.get("reasoning")
    if not isinstance(reasoning, str):
        return None
    reasoning = reasoning.strip()[:800]

    raw_split_x = parsed.get("split_x")
    split_x = None
    if raw_split_x is not None:
        if type(raw_split_x) is bool or not isinstance(raw_split_x, int):
            return None
        if raw_split_x <= 0 or raw_split_x >= display_width:
            return None
        split_x = int(round(raw_split_x / scale))
        if split_x <= 0 or split_x >= original_width:
            return None

    if is_two and split_x is None:
        return None
    if not is_two and raw_split_x is not None:
        return None

    return OpenAIVisionAssessment(
        is_two_container_image=is_two,
        split_x=split_x,
        raw_split_x=raw_split_x,
        confidence=confidence,
        reasoning=reasoning,
        should_require_human_review=should_review,
        model=model,
        latency_ms=latency_ms,
        usage=usage,
        downsampled_width=downsampled_width,
        downsampled_height=downsampled_height,
        downsample_scale=scale,
        purpose=purpose,
    )


def _extract_response_text(response: Any) -> str:
    try:
        message = response.choices[0].message
        content = getattr(message, "content", "")
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            parts = []
            for part in content:
                if isinstance(part, dict):
                    text = part.get("text")
                else:
                    text = getattr(part, "text", None)
                if text:
                    parts.append(str(text))
            return "".join(parts)
    except Exception:
        pass
    return str(response)


def _extract_json(text: str) -> Optional[dict]:
    if not text:
        return None
    start = text.find("{")
    if start == -1:
        return None
    depth = 0
    for idx in range(start, len(text)):
        char = text[idx]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                try:
                    return json.loads(text[start : idx + 1])
                except json.JSONDecodeError:
                    return None
    return None


def _extract_usage(response: Any) -> dict:
    usage = getattr(response, "usage", None)
    if usage is None:
        return {}

    data = {}
    for target, names in {
        "input_tokens": ("input_tokens", "prompt_tokens"),
        "output_tokens": ("output_tokens", "completion_tokens"),
        "total_tokens": ("total_tokens",),
    }.items():
        for name in names:
            value = getattr(usage, name, None)
            if value is not None:
                data[target] = value
                break
    return data
