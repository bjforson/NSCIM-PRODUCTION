"""
Pipeline Orchestrator

Runs all available splitting strategies on an image, scores and compares
results, and selects the best split point using consensus scoring.

Consensus: If multiple strategies agree on a split point (within threshold),
they each get a confidence bonus. This rewards convergent results.
"""

import logging
from typing import List, Optional
import numpy as np

from strategies.base import BaseSplitStrategy, SplitResult
from strategies.steel_wall_midpoint import SteelWallMidpointStrategy
from strategies.corner_fitting import CornerFittingStrategy
from strategies.container_gap import ContainerGapStrategy
from strategies.density_profile import DensityProfileStrategy
from strategies.edge_detection import EdgeDetectionStrategy
from strategies.ocr_geometry import OCRGeometryStrategy
from strategies.claude_vision import ClaudeVisionStrategy
from strategies.inner_casting_detector import InnerCastingPairStrategy
from pipeline.image_utils import decode_image, crop_and_encode
from pipeline.claude_verifier import verify_candidates_with_claude
from config import AGREEMENT_THRESHOLD_PX, AGREEMENT_BONUS, MIN_CONFIDENCE

logger = logging.getLogger(__name__)


def get_all_strategies() -> List[BaseSplitStrategy]:
    """Return all available splitting strategies.

    claude_vision       — 1.19.0: Anthropic Claude Vision API. When an API key
                          is configured, runs first and short-circuits the
                          hand-engineered strategies. Requires ANTHROPIC_API_KEY
                          env var; returns None silently when unset.
    steel_wall_midpoint — PRIMARY (legacy): finds the two darkest (outermost)
                          vertical bands in each half of the image — the outer
                          steel walls — and splits at their midpoint.
    corner_fitting      — SECONDARY: ISO corner casting detection in the top strip.
    density_profile     — Legacy valley detection fallback.
    edge_detection      — Canny edge fallback.
    ocr_geometry        — OCR-guided fallback (requires Tesseract).
    """
    return [
        InnerCastingPairStrategy(),    # 1.20.x: pure CV top-strip casting pair
                                        # detector — pixel-accurate on operator
                                        # test set (2 px error on 4c3c7a6d)
        ClaudeVisionStrategy(),        # 1.19.0: API-backed vision oracle (teacher)
        SteelWallMidpointStrategy(),   # Legacy primary: argmin outer wall midpoint
        CornerFittingStrategy(),       # Secondary: ISO corner castings (ZIMU-type)
        ContainerGapStrategy(),        # Tertiary: narrow bright gap in col-min
        DensityProfileStrategy(),      # Legacy fallback
        EdgeDetectionStrategy(),       # Canny edge fallback
        OCRGeometryStrategy(),         # OCR fallback
    ]


async def run_pipeline(image_data: bytes) -> List[SplitResult]:
    """
    Run ALL splitting strategies; select the highest-confidence result.

    All strategies run independently. Consensus scoring rewards strategies
    that agree with each other (within AGREEMENT_THRESHOLD_PX pixels).
    The highest-confidence result (after bonuses) is selected as best.

    This approach lets the pipeline naturally adapt per image:
      - Most images: steel_wall_midpoint wins (high confidence, accurate gap)
      - Images with clear corner castings: corner_fitting wins (very high confidence)
      - Dense cargo images: corner_fitting may win if SW confidence drops due to
        narrow span (heavy cargo overrides outer wall detection)

    Args:
        image_data: Raw image bytes (JPEG/PNG)

    Returns:
        List of SplitResult, sorted by confidence descending
    """
    image_array = decode_image(image_data)
    h, w = image_array.shape[:2]
    logger.info(f"Running pipeline on {w}x{h} image ({len(image_data)} bytes)")

    strategies = get_all_strategies()
    results: List[SplitResult] = []

    # 1.19.0 — when Claude Vision is available and succeeds, use it as the primary.
    # Otherwise fall back to steel_wall_midpoint. We keep all the other strategies
    # running below so we can compare them against Claude in the metadata.
    CLAUDE_STRATEGY = "claude_vision"
    FALLBACK_PRIMARY = "steel_wall_midpoint"
    claude_succeeded = False
    fallback_succeeded = False

    for strategy in strategies:
        # If Claude already succeeded, we still run every strategy for comparison
        # metadata — that's the whole point of the teacher/student setup. But if
        # Claude is not configured and the fallback primary has succeeded, we
        # short-circuit the remaining strategies like the legacy behaviour.
        if not claude_succeeded and fallback_succeeded and strategy.name != FALLBACK_PRIMARY:
            logger.info(f"  Skipping {strategy.name} (fallback primary succeeded)")
            continue
        try:
            logger.info(f"Running strategy: {strategy.name}")
            result = await strategy.analyze(image_data, image_array)
            if result is not None and result.confidence >= MIN_CONFIDENCE:
                results.append(result)
                logger.info(f"  {strategy.name}: split_x={result.split_x}, conf={result.confidence:.3f}, {result.processing_ms}ms")
                if strategy.name == CLAUDE_STRATEGY:
                    claude_succeeded = True
                    logger.info(f"  Claude Vision succeeded — using as primary")
                elif strategy.name == FALLBACK_PRIMARY:
                    fallback_succeeded = True
                    if not claude_succeeded:
                        logger.info(f"  Fallback primary (steel_wall_midpoint) succeeded")
            elif result is not None:
                logger.info(f"  {strategy.name}: below threshold (conf={result.confidence:.3f})")
            else:
                logger.info(f"  {strategy.name}: no split detected")
        except Exception as e:
            logger.error(f"  {strategy.name} failed: {e}", exc_info=True)

    if not results:
        logger.warning("No strategies produced valid results")
        return []

    # Apply consensus scoring when neither primary succeeded and there are
    # multiple candidates to compare.
    if not claude_succeeded and not fallback_succeeded and len(results) > 1:
        results = apply_consensus_scoring(results)

    # 1.20.x — Disagreement guard against Claude Vision outliers.
    # Retained as a safety net even though the verifier below is the primary
    # selector. If claude_vision disagrees with steel_wall by more than 50 px,
    # claude's answer is suspicious; bury its confidence.
    DISAGREEMENT_FALLBACK_PX = 50
    claude_result = next((r for r in results if r.strategy_name == CLAUDE_STRATEGY), None)
    steel_result = next((r for r in results if r.strategy_name == FALLBACK_PRIMARY), None)
    if claude_result and steel_result:
        disagreement = abs(claude_result.split_x - steel_result.split_x)
        if disagreement > DISAGREEMENT_FALLBACK_PX:
            logger.warning(
                f"[disagreement_guard] claude_vision={claude_result.split_x} "
                f"steel_wall_midpoint={steel_result.split_x} "
                f"disagreement={disagreement}px > {DISAGREEMENT_FALLBACK_PX}px — "
                f"dropping claude confidence so steel_wall becomes primary"
            )
            claude_result.confidence = min(claude_result.confidence, steel_result.confidence - 0.01)
            claude_result.metadata["disagreement_with_steel_wall"] = disagreement
            claude_result.metadata["disagreement_guard_triggered"] = True

    # 1.20.x — Image-center bimodal failure guard.
    # claude_vision has a well-known failure mode where it returns the exact
    # image_width/2 on difficult images (observed 25/40 times on the fresh
    # 544-px batch). This is a "giveaway" signal: Claude is uncertain and
    # defaulting to the geometric center. Treat any CV result within ±3 px
    # of image center as suspicious and bury its confidence.
    if claude_result and image_array is not None:
        _img_h, _img_w = image_array.shape[:2]
        _center = _img_w / 2
        if abs(claude_result.split_x - _center) <= 3:
            logger.warning(
                f"[cv_center_guard] claude_vision={claude_result.split_x} "
                f"within 3px of image center ({_center:.0f}) — stuck at midpoint, "
                f"dropping confidence"
            )
            # Drop below MIN_CONFIDENCE so it's filtered out of best-sort entirely
            claude_result.confidence = MIN_CONFIDENCE - 0.01
            claude_result.metadata["cv_center_guard_triggered"] = True

    # 1.20.x — CLAUDE VISION VERIFIER IS THE PRIMARY DECISION-MAKER.
    #
    # The user's original directive: "use claude vision to investigate and
    # establish a way to split the images that are more accurate... take
    # feedback to do the splitting locally."
    #
    # The verifier sees the top-strip crop of the image with each
    # strategy's candidate drawn as a labeled colored line. It picks
    # the best candidate using few-shot examples from the consensus
    # corpus (operator-verified ground truth + ICP+SW consensus matches).
    #
    # The v3 prompt bakes in the user's corrections:
    #   - Container boundary = OUTER edge of corner casting (x_end, not x_start)
    #   - Scan the TOP STRIP first; cargo in the middle is misleading
    #   - X-ray scans are 3D→2D projections; y may drift across x
    #   - Concrete numeric example from operator hover measurements
    #
    # ICP, steel_wall, corner_fitting, density_profile are all CANDIDATES
    # that Claude evaluates. None of them override Claude's decision.
    # This ensures the learning loop stays active: every job gets Claude's
    # analysis with few-shot context, and the consensus corpus grows as
    # Claude+ICP+SW converge on new images.
    verifier_pick = None
    try:
        verifier_pick = await verify_candidates_with_claude(image_data, results)
    except Exception as e:
        logger.error(f"[claude_verifier] raised: {e}", exc_info=True)
        verifier_pick = None

    if verifier_pick is not None:
        logger.info(
            f"[claude_verifier] picked {verifier_pick.picked_strategy} "
            f"(label {verifier_pick.picked_label}) split_x={verifier_pick.picked_split_x} "
            f"confidence={verifier_pick.claude_confidence:.2f} "
            f"few_shot={verifier_pick.few_shot_count}"
        )
        for r in results:
            if r.strategy_name == verifier_pick.picked_strategy:
                r.confidence = 1.0
                r.metadata["verifier_picked"] = True
                r.metadata["verifier_label"] = verifier_pick.picked_label
                r.metadata["verifier_ranking"] = verifier_pick.ranking
                r.metadata["verifier_reasoning"] = verifier_pick.reasoning
                r.metadata["verifier_claude_confidence"] = verifier_pick.claude_confidence
                r.metadata["verifier_few_shot_count"] = verifier_pick.few_shot_count
                r.metadata["verifier_candidates_offered"] = [
                    {"strategy": c[0], "split_x": c[1], "label": c[2]}
                    for c in verifier_pick.candidates_offered
                ]
            else:
                r.metadata.setdefault("verifier_ranking", verifier_pick.ranking)
    else:
        # Fallback when Claude verifier can't run (no API key, < 2
        # candidates, API error). Use ICP+SW consensus if available,
        # otherwise let the confidence-sorted best win.
        logger.warning("[claude_verifier] did not return a pick — falling back to confidence sort")
        icp_result = next((r for r in results if r.strategy_name == "inner_casting_pair"), None)
        sw_result_fb = next((r for r in results if r.strategy_name == FALLBACK_PRIMARY), None)
        if icp_result and sw_result_fb and abs(icp_result.split_x - sw_result_fb.split_x) <= 10:
            icp_result.confidence = 0.99
            icp_result.metadata["fallback_consensus"] = True

    # Crop images for each result
    for result in results:
        try:
            left_bytes, right_bytes = crop_and_encode(image_data, result.split_x)
            result.left_image = left_bytes
            result.right_image = right_bytes
        except Exception as e:
            logger.error(f"Failed to crop for {result.strategy_name}: {e}")

    # Sort by confidence descending — the verifier's pick (confidence=1.0)
    # now wins naturally.
    results.sort(key=lambda r: r.confidence, reverse=True)

    return results


def apply_consensus_scoring(results: List[SplitResult]) -> List[SplitResult]:
    """
    Apply consensus bonus: strategies that agree on the split point
    (within AGREEMENT_THRESHOLD_PX pixels) get a confidence boost.
    """
    if len(results) < 2:
        return results

    split_points = [(r.split_x, i) for i, r in enumerate(results)]

    for i, r1 in enumerate(results):
        agreeing = 0
        for j, r2 in enumerate(results):
            if i == j:
                continue
            if abs(r1.split_x - r2.split_x) <= AGREEMENT_THRESHOLD_PX:
                agreeing += 1

        if agreeing > 0:
            bonus = AGREEMENT_BONUS * agreeing
            old_conf = r1.confidence
            r1.confidence = min(r1.confidence + bonus, 1.0)
            r1.metadata["consensus_bonus"] = round(bonus, 3)
            r1.metadata["agreeing_strategies"] = agreeing
            logger.info(
                f"  {r1.strategy_name}: consensus bonus +{bonus:.3f} "
                f"({agreeing} agreeing), {old_conf:.3f} -> {r1.confidence:.3f}"
            )

    return results


def get_best_result(results: List[SplitResult]) -> Optional[SplitResult]:
    """Get the highest-confidence result."""
    if not results:
        return None
    return max(results, key=lambda r: r.confidence)
