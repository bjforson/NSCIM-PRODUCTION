"""
OpenAI vision verifier in shadow/advisory mode.

This module has no database dependency and performs no writes. It annotates
the already-selected SplitResult with advisory metadata only; deterministic
candidate ranking remains the production decision path.
"""

import logging
from typing import Any, Optional, Sequence

from config import OPENAI_VISION_REVIEW_DISAGREEMENT_PX
from strategies.openai_vision import OpenAIVisionAssessment, assess_image_with_openai

logger = logging.getLogger(__name__)

ADVISORY_METADATA_KEY = "openai_vision_advisory"
OPENAI_STRATEGY_NAME = "openai_vision"


async def verify_candidates_with_openai(
    image_data: bytes,
    results: Sequence[Any],
) -> Optional[OpenAIVisionAssessment]:
    """Return an OpenAI advisory assessment for deterministic candidates.

    The returned split is not a production split. It is used only for metadata
    and human-review hints.
    """

    selected = _selected_result(results)
    if selected is None:
        logger.debug("[openai_verifier] no deterministic result to verify")
        return None

    candidates = _candidate_list(results)
    if not candidates:
        logger.debug("[openai_verifier] no deterministic candidates to offer")
        return None

    return await assess_image_with_openai(
        image_data,
        None,
        candidates=candidates,
        deterministic_split_x=int(getattr(selected, "split_x")),
        purpose="verifier",
    )


async def attach_openai_advisory(
    image_data: bytes,
    results: Sequence[Any],
) -> Optional[OpenAIVisionAssessment]:
    """Attach OpenAI advisory metadata to the deterministic selected result.

    This function intentionally does not append an OpenAI result to the result
    set. That keeps OpenAI out of consensus scoring, candidate ranking, crop
    generation, and best-result selection.
    """

    selected = _selected_result(results)
    if selected is None:
        return None

    candidates = _candidate_list(results)
    assessment = await verify_candidates_with_openai(image_data, results)
    if assessment is None:
        return None

    selected_split_x = int(getattr(selected, "split_x"))
    metadata = assessment.to_metadata()
    metadata.update(
        {
            "deterministic_selected_strategy": getattr(selected, "strategy_name", None),
            "deterministic_split_x": selected_split_x,
            "candidate_count": len(candidates),
            "candidates_offered": [
                {"strategy": name, "split_x": int(split_x)}
                for name, split_x in candidates
            ],
            "review_disagreement_threshold_px": OPENAI_VISION_REVIEW_DISAGREEMENT_PX,
        }
    )

    computed_review = bool(assessment.should_require_human_review)
    if assessment.split_x is not None:
        disagreement_px = abs(int(assessment.split_x) - selected_split_x)
        metadata["disagreement_px"] = disagreement_px
        if disagreement_px > OPENAI_VISION_REVIEW_DISAGREEMENT_PX:
            computed_review = True
    else:
        metadata["disagreement_px"] = None
        computed_review = True

    metadata["computed_should_require_human_review"] = computed_review

    selected_meta = _ensure_metadata_dict(selected)
    selected_meta[ADVISORY_METADATA_KEY] = metadata
    selected_meta["openai_vision_shadow_mode"] = True
    selected_meta["openai_vision_affects_split"] = False
    selected_meta["openai_vision_computed_human_review"] = computed_review

    logger.info(
        "[openai_verifier] advisory attached to %s split_x=%s openai_split_x=%s "
        "confidence=%.2f human_review=%s",
        getattr(selected, "strategy_name", None),
        selected_split_x,
        assessment.split_x,
        assessment.confidence,
        computed_review,
    )
    return assessment


def _candidate_list(results: Sequence[Any]) -> list[tuple[str, int]]:
    candidates: list[tuple[str, int]] = []
    seen = set()

    for result in sorted(results, key=_candidate_sort_key, reverse=True):
        strategy_name = str(getattr(result, "strategy_name", "") or "")
        if not strategy_name or strategy_name == OPENAI_STRATEGY_NAME:
            continue
        split_x = getattr(result, "split_x", None)
        if type(split_x) is bool or not isinstance(split_x, int):
            continue
        key = (strategy_name, split_x)
        if key in seen:
            continue
        seen.add(key)
        candidates.append((strategy_name, split_x))
        if len(candidates) >= 8:
            break

    return candidates


def _selected_result(results: Sequence[Any]) -> Optional[Any]:
    deterministic = [
        result
        for result in results
        if getattr(result, "strategy_name", None) != OPENAI_STRATEGY_NAME
        and type(getattr(result, "split_x", None)) is int
    ]
    if not deterministic:
        return None

    selected = [
        result
        for result in deterministic
        if bool(_metadata_for_read(result).get("ranker_selected"))
    ]
    if selected:
        return max(selected, key=lambda result: float(_metadata_for_read(result).get("ranker_score", 0.0) or 0.0))

    return max(
        deterministic,
        key=lambda result: float(getattr(result, "confidence", 0.0) or 0.0),
    )


def _candidate_sort_key(result: Any) -> tuple[float, float, float]:
    meta = _metadata_for_read(result)
    return (
        1.0 if meta.get("ranker_selected") else 0.0,
        float(meta.get("ranker_score", 0.0) or 0.0),
        float(getattr(result, "confidence", 0.0) or 0.0),
    )


def _metadata_for_read(result: Any) -> dict:
    meta = getattr(result, "metadata", None)
    if isinstance(meta, dict):
        return meta
    meta = getattr(result, "strategy_metadata", None)
    if isinstance(meta, dict):
        return meta
    return {}


def _ensure_metadata_dict(result: Any) -> dict:
    meta = getattr(result, "metadata", None)
    if isinstance(meta, dict):
        return meta

    strategy_meta = getattr(result, "strategy_metadata", None)
    if isinstance(strategy_meta, dict):
        return strategy_meta

    meta = {}
    if hasattr(result, "metadata"):
        setattr(result, "metadata", meta)
    elif hasattr(result, "strategy_metadata"):
        setattr(result, "strategy_metadata", meta)
    return meta
