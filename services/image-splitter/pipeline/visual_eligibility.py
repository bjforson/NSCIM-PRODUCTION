"""Independent visual eligibility gate for container image splitting.

This module decides whether an image visually contains two containers before
the splitter runs any candidate split strategies. It intentionally does not
import or consume strategy results; all evidence comes from image pixels.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, replace
import time
from typing import Literal, Optional

import cv2
import numpy as np

from pipeline.image_utils import decode_image


VisualEligibilityLabel = Literal["dual_container", "single_container", "uncertain"]


@dataclass(frozen=True)
class FrameCandidate:
    """A vertical structure that may represent a container frame edge."""

    x: int
    normalized_x: float
    score: float
    prominence: float
    width_px: int
    source: str
    role: Optional[str] = None

    def to_metadata(self) -> dict:
        return {
            "x": int(self.x),
            "normalized_x": round(float(self.normalized_x), 5),
            "score": round(float(self.score), 4),
            "prominence": round(float(self.prominence), 4),
            "width_px": int(self.width_px),
            "source": self.source,
            "role": self.role,
        }


@dataclass(frozen=True)
class VisualEligibilityResult:
    """Serializable result from the visual eligibility classifier."""

    label: VisualEligibilityLabel
    confidence: float
    reason_codes: tuple[str, ...]
    candidate_frame_positions: tuple[FrameCandidate, ...]
    image_width: int
    image_height: int
    scanner_type: Optional[str]
    processing_ms: int
    metrics: dict

    @property
    def should_split(self) -> bool:
        return self.label == "dual_container"

    def to_metadata(self) -> dict:
        return {
            "visual_eligibility": self.label,
            "visual_eligibility_confidence": round(float(self.confidence), 4),
            "visual_eligibility_should_split": self.should_split,
            "visual_eligibility_reason_codes": list(self.reason_codes),
            "candidate_frame_positions": [
                candidate.to_metadata()
                for candidate in self.candidate_frame_positions
            ],
            "image_width": int(self.image_width),
            "image_height": int(self.image_height),
            "scanner_type": self.scanner_type,
            "processing_ms": int(self.processing_ms),
            "metrics": _json_safe(self.metrics),
        }


@dataclass(frozen=True)
class _ForegroundAnalysis:
    occupancy: np.ndarray
    active_runs: tuple[tuple[int, int], ...]
    component_count: int
    broad_component_count: int
    largest_component_width: int
    object_span_start: int
    object_span_end: int
    object_span_ratio: float
    foreground_coverage: float
    foreground_is_dark: bool
    otsu_threshold: float
    central_valley_x: Optional[int]
    central_valley_drop: float
    central_valley_occupancy: float
    active_threshold: float


@dataclass(frozen=True)
class _InnerPair:
    left: FrameCandidate
    right: FrameCandidate
    midpoint_x: int
    separation_px: int
    score: float
    left_outer_x: Optional[int]
    right_outer_x: Optional[int]
    support_score: float
    outer_score: float
    center_score: float


def classify_visual_eligibility(
    image_data: bytes,
    scanner_type: Optional[str] = None,
) -> VisualEligibilityResult:
    """Classify an image as dual, single, or uncertain from pixels only.

    This is the intended call point before ``run_pipeline`` in ``main.py``:

        gate = classify_visual_eligibility(job.image_data, job.scanner_type)
        if gate.label != "dual_container":
            ...

    The function is synchronous and CPU-local. It returns a result with
    confidence, reason codes, and candidate frame positions suitable for JSON
    metadata or logs.
    """

    start = time.time()
    scanner_type_norm = scanner_type.upper() if scanner_type else None

    try:
        image_array = decode_image(image_data)
    except Exception as exc:
        return VisualEligibilityResult(
            label="uncertain",
            confidence=0.99,
            reason_codes=("decode_failed",),
            candidate_frame_positions=(),
            image_width=0,
            image_height=0,
            scanner_type=scanner_type_norm,
            processing_ms=int((time.time() - start) * 1000),
            metrics={"error": str(exc)},
        )

    gray = _to_gray_uint8(image_array)
    gray = _robust_contrast_stretch(gray)
    height, width = gray.shape

    if width < 180 or height < 80:
        return VisualEligibilityResult(
            label="uncertain",
            confidence=0.95,
            reason_codes=("image_too_small",),
            candidate_frame_positions=(),
            image_width=width,
            image_height=height,
            scanner_type=scanner_type_norm,
            processing_ms=int((time.time() - start) * 1000),
            metrics={"width": width, "height": height},
        )

    foreground = _analyse_foreground(gray)
    candidates = _find_frame_candidates(gray, foreground.foreground_is_dark)
    inner_pair = _find_best_inner_pair(candidates, foreground, width)
    candidates = _assign_roles(candidates, inner_pair)

    strong_candidates = [
        c for c in candidates
        if c.score >= 0.42 and 0.04 <= c.normalized_x <= 0.96
    ]
    central_candidates = [
        c for c in strong_candidates
        if 0.16 <= c.normalized_x <= 0.84
    ]

    pair_score = inner_pair.score if inner_pair else 0.0
    dual_score = _dual_score(pair_score, foreground, strong_candidates, inner_pair)
    single_score = _single_score(pair_score, foreground, strong_candidates)
    label, confidence, reason_codes = _choose_label(
        dual_score=dual_score,
        single_score=single_score,
        pair_score=pair_score,
        foreground=foreground,
        strong_count=len(strong_candidates),
        central_count=len(central_candidates),
        inner_pair=inner_pair,
        scanner_type_norm=scanner_type_norm,
    )

    metrics = {
        "dual_score": round(float(dual_score), 4),
        "single_score": round(float(single_score), 4),
        "inner_pair_score": round(float(pair_score), 4),
        "strong_frame_count": len(strong_candidates),
        "central_frame_count": len(central_candidates),
        "foreground_component_count": foreground.component_count,
        "broad_component_count": foreground.broad_component_count,
        "largest_component_width": foreground.largest_component_width,
        "object_span_start": foreground.object_span_start,
        "object_span_end": foreground.object_span_end,
        "object_span_ratio": round(float(foreground.object_span_ratio), 4),
        "foreground_coverage": round(float(foreground.foreground_coverage), 4),
        "foreground_is_dark": foreground.foreground_is_dark,
        "otsu_threshold": round(float(foreground.otsu_threshold), 2),
        "central_valley_x": foreground.central_valley_x,
        "central_valley_drop": round(float(foreground.central_valley_drop), 4),
        "central_valley_occupancy": round(float(foreground.central_valley_occupancy), 4),
        "active_threshold": round(float(foreground.active_threshold), 4),
    }
    if inner_pair is not None:
        metrics["inner_pair"] = {
            "left_x": inner_pair.left.x,
            "right_x": inner_pair.right.x,
            "midpoint_x": inner_pair.midpoint_x,
            "separation_px": inner_pair.separation_px,
            "left_outer_x": inner_pair.left_outer_x,
            "right_outer_x": inner_pair.right_outer_x,
            "support_score": round(float(inner_pair.support_score), 4),
            "outer_score": round(float(inner_pair.outer_score), 4),
            "center_score": round(float(inner_pair.center_score), 4),
        }

    return VisualEligibilityResult(
        label=label,
        confidence=confidence,
        reason_codes=reason_codes,
        candidate_frame_positions=tuple(candidates[:12]),
        image_width=width,
        image_height=height,
        scanner_type=scanner_type_norm,
        processing_ms=int((time.time() - start) * 1000),
        metrics=metrics,
    )


def visual_eligibility_gate(
    image_data: bytes,
    scanner_type: Optional[str] = None,
) -> VisualEligibilityResult:
    """Alias kept intentionally short for call sites."""

    return classify_visual_eligibility(image_data, scanner_type)


def _to_gray_uint8(image_array: np.ndarray) -> np.ndarray:
    if image_array.ndim == 3:
        gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY)
    else:
        gray = image_array

    if gray.dtype == np.uint8:
        return gray

    gray_f = gray.astype(np.float32)
    min_v = float(np.min(gray_f))
    max_v = float(np.max(gray_f))
    if max_v <= min_v:
        return np.zeros_like(gray_f, dtype=np.uint8)
    return np.clip((gray_f - min_v) * 255.0 / (max_v - min_v), 0, 255).astype(np.uint8)


def _robust_contrast_stretch(gray: np.ndarray) -> np.ndarray:
    lo = float(np.percentile(gray, 0.5))
    hi = float(np.percentile(gray, 99.5))
    if hi <= lo + 1:
        return gray.astype(np.uint8, copy=False)
    stretched = (gray.astype(np.float32) - lo) * 255.0 / (hi - lo)
    return np.clip(stretched, 0, 255).astype(np.uint8)


def _analyse_foreground(gray: np.ndarray) -> _ForegroundAnalysis:
    height, width = gray.shape
    y0 = int(height * 0.08)
    y1 = max(y0 + 20, int(height * 0.92))
    band = gray[y0:y1, :]

    threshold, _ = cv2.threshold(band, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    dark_mask = band <= threshold
    bright_mask = band > threshold

    dark_score = _score_foreground_mask(dark_mask)
    bright_score = _score_foreground_mask(bright_mask)
    foreground_is_dark = dark_score >= bright_score
    mask = dark_mask if foreground_is_dark else bright_mask

    kernel_h = max(3, int(round(height * 0.018)))
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, kernel_h))
    mask_u8 = mask.astype(np.uint8)
    mask_u8 = cv2.morphologyEx(mask_u8, cv2.MORPH_CLOSE, kernel)
    mask_u8 = cv2.morphologyEx(mask_u8, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))

    occupancy = mask_u8.mean(axis=0).astype(np.float32)
    occupancy = _smooth_1d(occupancy, max(5, int(round(width * 0.012))))

    p50 = float(np.percentile(occupancy, 50))
    p90 = float(np.percentile(occupancy, 90))
    active_threshold = max(0.08, min(0.42, p50 + 0.32 * max(0.0, p90 - p50)))
    active = occupancy >= active_threshold
    active = _fill_small_gaps(active, max(3, int(round(width * 0.018))))

    min_component_width = max(12, int(round(width * 0.025)))
    broad_component_width = max(30, int(round(width * 0.12)))
    runs = tuple(
        (start, end)
        for start, end in _boolean_runs(active)
        if end - start >= min_component_width
    )
    broad_runs = tuple((start, end) for start, end in runs if end - start >= broad_component_width)

    if runs:
        span_start = min(start for start, _ in runs)
        span_end = max(end for _, end in runs)
        largest_width = max(end - start for start, end in runs)
    else:
        span_start = 0
        span_end = 0
        largest_width = 0

    valley_x, valley_drop, valley_occ = _central_valley(occupancy, width)

    return _ForegroundAnalysis(
        occupancy=occupancy,
        active_runs=runs,
        component_count=len(runs),
        broad_component_count=len(broad_runs),
        largest_component_width=largest_width,
        object_span_start=span_start,
        object_span_end=span_end,
        object_span_ratio=(span_end - span_start) / max(1.0, float(width)),
        foreground_coverage=float(mask_u8.mean()),
        foreground_is_dark=foreground_is_dark,
        otsu_threshold=float(threshold),
        central_valley_x=valley_x,
        central_valley_drop=float(valley_drop),
        central_valley_occupancy=float(valley_occ),
        active_threshold=float(active_threshold),
    )


def _score_foreground_mask(mask: np.ndarray) -> float:
    coverage = float(mask.mean())
    occupancy = mask.mean(axis=0)
    active = occupancy > max(0.06, float(np.percentile(occupancy, 60)))
    runs = _boolean_runs(active)
    if runs:
        span = max(end for _, end in runs) - min(start for start, _ in runs)
        largest = max(end - start for start, end in runs)
    else:
        span = 0
        largest = 0

    width = mask.shape[1]
    coverage_score = 1.0 - min(abs(coverage - 0.38) / 0.38, 1.0)
    span_score = min(span / max(1.0, width * 0.55), 1.0)
    largest_score = min(largest / max(1.0, width * 0.30), 1.0)
    edge_width = max(1, width // 25)
    edge_occupancy = float((occupancy[:edge_width].mean() + occupancy[-edge_width:].mean()) / 2)
    edge_score = 1.0 - min(edge_occupancy / 0.75, 1.0)
    return 0.38 * coverage_score + 0.26 * span_score + 0.20 * largest_score + 0.16 * edge_score


def _central_valley(occupancy: np.ndarray, width: int) -> tuple[Optional[int], float, float]:
    search_start = int(width * 0.18)
    search_end = int(width * 0.82)
    if search_end <= search_start + 5:
        return None, 0.0, 0.0

    search = occupancy[search_start:search_end]
    local_idx = int(np.argmin(search))
    valley_x = search_start + local_idx

    support_inner = max(6, int(round(width * 0.035)))
    support_outer = max(support_inner + 8, int(round(width * 0.16)))
    left = occupancy[max(0, valley_x - support_outer) : max(0, valley_x - support_inner)]
    right = occupancy[min(width, valley_x + support_inner) : min(width, valley_x + support_outer)]
    if left.size == 0 or right.size == 0:
        return valley_x, 0.0, float(occupancy[valley_x])

    support = min(float(np.percentile(left, 80)), float(np.percentile(right, 80)))
    valley_occ = float(occupancy[valley_x])
    return valley_x, max(0.0, support - valley_occ), valley_occ


def _find_frame_candidates(gray: np.ndarray, foreground_is_dark: bool) -> list[FrameCandidate]:
    height, width = gray.shape
    top = gray[: max(20, int(round(height * 0.30))), :]
    middle = gray[int(height * 0.12) : max(int(height * 0.12) + 20, int(height * 0.88)), :]

    blurred = cv2.GaussianBlur(gray, (3, 3), 0)
    sobel = np.abs(cv2.Sobel(blurred, cv2.CV_32F, 1, 0, ksize=3))
    top_edge = sobel[: top.shape[0], :].mean(axis=0)
    mid_edge = sobel[int(height * 0.12) : max(int(height * 0.12) + 20, int(height * 0.88)), :].mean(axis=0)

    top_mean = top.mean(axis=0).astype(np.float32)
    mid_mean = middle.mean(axis=0).astype(np.float32)
    top_band = 255.0 - top_mean if foreground_is_dark else top_mean
    mid_band = 255.0 - mid_mean if foreground_is_dark else mid_mean

    profile = (
        0.36 * _normalise_1d(top_edge)
        + 0.28 * _normalise_1d(mid_edge)
        + 0.24 * _normalise_1d(top_band)
        + 0.12 * _normalise_1d(mid_band)
    )
    profile = _smooth_1d(profile, max(5, int(round(width * 0.006))))

    p50 = float(np.percentile(profile, 50))
    p90 = float(np.percentile(profile, 90))
    p98 = float(np.percentile(profile, 98))
    if p98 - p50 < 0.025:
        return []

    threshold = max(p50 + 0.28 * (p90 - p50), p90 * 0.72)
    min_distance = max(7, int(round(width * 0.012)))

    peaks = _find_local_peaks(profile, threshold=threshold, min_distance=min_distance)
    if not peaks:
        threshold = p50 + 0.20 * (p90 - p50)
        peaks = _find_local_peaks(profile, threshold=threshold, min_distance=min_distance)

    candidates: list[FrameCandidate] = []
    norm_denominator = max(0.001, p98 - p50)
    for x, value in peaks:
        left_min, right_min = _local_shoulders(profile, x, max(8, int(round(width * 0.028))))
        prominence = max(0.0, float(value - max(left_min, right_min)))
        width_px = _peak_width(profile, x, max(left_min, right_min), value)

        x0 = max(0, x - 3)
        x1 = min(width, x + 4)
        column_edges = sobel[:, x0:x1].max(axis=1)
        continuity_threshold = max(float(np.percentile(sobel, 88)), 1.0)
        continuity = float((column_edges >= continuity_threshold).mean())

        strength = max(0.0, min(1.0, (float(value) - p50) / norm_denominator))
        prominence_score = max(0.0, min(1.0, prominence / max(0.001, p98 - p50)))
        continuity_score = max(0.0, min(1.0, continuity / 0.45))
        narrow_score = 1.0 - min(width_px / max(12.0, width * 0.045), 1.0)
        score = (
            0.42 * strength
            + 0.25 * prominence_score
            + 0.22 * continuity_score
            + 0.11 * narrow_score
        )

        candidates.append(
            FrameCandidate(
                x=int(x),
                normalized_x=float(x / max(1, width)),
                score=float(max(0.0, min(1.0, score))),
                prominence=float(prominence),
                width_px=int(width_px),
                source="top_mid_vertical_profile",
            )
        )

    candidates = [
        candidate for candidate in candidates
        if candidate.score >= 0.12 and candidate.prominence >= 0.01
    ]
    candidates.sort(key=lambda candidate: candidate.score, reverse=True)
    candidates = _dedupe_candidates(candidates, min_distance=max(5, int(round(width * 0.01))))
    candidates.sort(key=lambda candidate: candidate.x)
    return candidates[:20]


def _find_best_inner_pair(
    candidates: list[FrameCandidate],
    foreground: _ForegroundAnalysis,
    width: int,
) -> Optional[_InnerPair]:
    if len(candidates) < 2:
        return None

    strongish = [c for c in candidates if c.score >= 0.30]
    if len(strongish) < 2:
        return None

    min_sep = max(8, int(round(width * 0.012)))
    max_sep = max(min_sep + 8, int(round(width * 0.13)))
    best: Optional[_InnerPair] = None

    for idx, left in enumerate(strongish[:-1]):
        for right in strongish[idx + 1 :]:
            sep = right.x - left.x
            if sep < min_sep:
                continue
            if sep > max_sep:
                break

            midpoint = int(round((left.x + right.x) / 2))
            midpoint_norm = midpoint / max(1.0, float(width))
            # A valid two-container joint can be off-center for 20ft/40ft
            # combinations, but it should not sit near the outer quarter of
            # the whole scan. Far-side frame pairs are usually a single
            # container's end frame plus trailer structure.
            if midpoint_norm < 0.22 or midpoint_norm > 0.78:
                continue

            left_outer = _nearest_outer(strongish, left.x, width, side="left")
            right_outer = _nearest_outer(strongish, right.x, width, side="right")
            outer_count = int(left_outer is not None) + int(right_outer is not None)
            outer_score = outer_count / 2.0

            support_score = _pair_support_score(foreground.occupancy, left.x, right.x, width)
            if support_score < 0.15 and outer_score < 0.5:
                continue
            if sep < width * 0.02 and support_score < 0.40 and outer_score < 1.0:
                continue

            center_score = max(0.0, 1.0 - abs(midpoint_norm - 0.50) / 0.38)
            sep_ideal = max(min_sep, width * 0.045)
            separation_score = max(0.0, 1.0 - abs(sep - sep_ideal) / max(sep_ideal * 1.75, 1.0))
            candidate_score = min(left.score, right.score)

            score = (
                0.40 * candidate_score
                + 0.20 * support_score
                + 0.18 * outer_score
                + 0.12 * separation_score
                + 0.10 * center_score
            )

            pair = _InnerPair(
                left=left,
                right=right,
                midpoint_x=midpoint,
                separation_px=sep,
                score=float(max(0.0, min(1.0, score))),
                left_outer_x=left_outer.x if left_outer is not None else None,
                right_outer_x=right_outer.x if right_outer is not None else None,
                support_score=support_score,
                outer_score=outer_score,
                center_score=center_score,
            )
            if best is None or pair.score > best.score:
                best = pair

    return best


def _nearest_outer(
    candidates: list[FrameCandidate],
    anchor_x: int,
    width: int,
    side: Literal["left", "right"],
) -> Optional[FrameCandidate]:
    min_offset = max(20, int(round(width * 0.055)))
    if side == "left":
        pool = [c for c in candidates if c.x <= anchor_x - min_offset and c.score >= 0.34]
        return max(pool, key=lambda c: c.x, default=None)
    pool = [c for c in candidates if c.x >= anchor_x + min_offset and c.score >= 0.34]
    return min(pool, key=lambda c: c.x, default=None)


def _pair_support_score(occupancy: np.ndarray, left_x: int, right_x: int, width: int) -> float:
    inner = max(5, int(round(width * 0.025)))
    outer = max(inner + 8, int(round(width * 0.18)))
    left_region = occupancy[max(0, left_x - outer) : max(0, left_x - inner)]
    right_region = occupancy[min(width, right_x + inner) : min(width, right_x + outer)]
    if left_region.size == 0 or right_region.size == 0:
        return 0.0
    support = min(float(np.percentile(left_region, 75)), float(np.percentile(right_region, 75)))
    return max(0.0, min(1.0, support / 0.32))


def _assign_roles(candidates: list[FrameCandidate], inner_pair: Optional[_InnerPair]) -> list[FrameCandidate]:
    if inner_pair is None:
        return candidates

    assigned: list[FrameCandidate] = []
    for candidate in candidates:
        role = candidate.role
        if candidate.x == inner_pair.left.x:
            role = "inner_left_candidate"
        elif candidate.x == inner_pair.right.x:
            role = "inner_right_candidate"
        elif inner_pair.left_outer_x is not None and candidate.x == inner_pair.left_outer_x:
            role = "left_outer_candidate"
        elif inner_pair.right_outer_x is not None and candidate.x == inner_pair.right_outer_x:
            role = "right_outer_candidate"
        assigned.append(replace(candidate, role=role))

    assigned.sort(key=lambda candidate: (0 if candidate.role else 1, -candidate.score, candidate.x))
    return assigned


def _dual_score(
    pair_score: float,
    foreground: _ForegroundAnalysis,
    strong_candidates: list[FrameCandidate],
    inner_pair: Optional[_InnerPair],
) -> float:
    frame_score = min(len(strong_candidates) / 4.0, 1.0)
    component_score = 1.0 if foreground.broad_component_count >= 2 else 0.0
    valley_score = min(foreground.central_valley_drop / 0.18, 1.0)
    span_score = min(max(foreground.object_span_ratio - 0.35, 0.0) / 0.45, 1.0)
    outer_score = inner_pair.outer_score if inner_pair else 0.0
    return max(
        0.0,
        min(
            1.0,
            0.48 * pair_score
            + 0.17 * frame_score
            + 0.13 * component_score
            + 0.10 * valley_score
            + 0.07 * span_score
            + 0.05 * outer_score,
        ),
    )


def _single_score(
    pair_score: float,
    foreground: _ForegroundAnalysis,
    strong_candidates: list[FrameCandidate],
) -> float:
    no_pair_score = 1.0 - min(pair_score / 0.55, 1.0)
    few_frames_score = 1.0 - min(max(0, len(strong_candidates) - 2) / 3.0, 1.0)
    one_mass_score = 1.0 if foreground.broad_component_count <= 1 else 0.0
    low_valley_score = 1.0 - min(foreground.central_valley_drop / 0.16, 1.0)
    span_score = 1.0 if foreground.object_span_ratio >= 0.25 else foreground.object_span_ratio / 0.25
    return max(
        0.0,
        min(
            1.0,
            0.35 * no_pair_score
            + 0.23 * few_frames_score
            + 0.20 * one_mass_score
            + 0.12 * low_valley_score
            + 0.10 * span_score,
        ),
    )


def _choose_label(
    dual_score: float,
    single_score: float,
    pair_score: float,
    foreground: _ForegroundAnalysis,
    strong_count: int,
    central_count: int,
    inner_pair: Optional[_InnerPair],
    scanner_type_norm: Optional[str],
) -> tuple[VisualEligibilityLabel, float, tuple[str, ...]]:
    is_fs6000 = scanner_type_norm == "FS6000"
    reasons: list[str] = []

    if foreground.object_span_ratio < 0.18 or foreground.foreground_coverage < 0.015:
        reasons.extend(("insufficient_foreground", "low_visual_signal"))
        return "uncertain", 0.86, tuple(reasons)

    has_usable_inner_pair = (
        inner_pair is not None
        and inner_pair.center_score >= (0.40 if is_fs6000 else 0.45)
    )

    if has_usable_inner_pair and pair_score >= (0.48 if is_fs6000 else 0.52) and dual_score >= 0.46:
        reasons.append("inner_frame_pair_detected")
        if inner_pair.outer_score >= 0.5:
            reasons.append("outer_frame_support")
        if strong_count >= 4:
            reasons.append("four_or_more_frame_candidates")
        if foreground.central_valley_drop >= 0.12:
            reasons.append("central_foreground_valley")
        confidence = min(0.94, max(0.55, 0.48 + dual_score * 0.42 + pair_score * 0.12))
        return "dual_container", round(float(confidence), 4), tuple(reasons)

    if (
        single_score >= 0.64
        and dual_score < 0.42
        and pair_score < 0.44
        and foreground.broad_component_count <= 1
    ):
        reasons.append("single_foreground_mass")
        if strong_count <= 2:
            reasons.append("few_frame_candidates")
        else:
            reasons.append("no_plausible_inner_frame_pair")
        if foreground.central_valley_drop < 0.08:
            reasons.append("no_central_foreground_valley")
        confidence = min(0.90, max(0.52, 0.46 + single_score * 0.38 + (1.0 - dual_score) * 0.08))
        return "single_container", round(float(confidence), 4), tuple(reasons)

    if inner_pair is not None and pair_score >= 0.38:
        reasons.append("weak_inner_frame_pair")
    if strong_count >= 3:
        reasons.append("multiple_frame_candidates")
    if central_count == 0:
        reasons.append("no_central_frame_candidates")
    if foreground.broad_component_count >= 2:
        reasons.append("multiple_foreground_masses")
    if foreground.central_valley_drop >= 0.10:
        reasons.append("weak_central_foreground_valley")
    if not reasons:
        reasons.append("mixed_visual_evidence")

    margin = abs(dual_score - single_score)
    confidence = min(0.88, max(0.50, 0.72 - margin * 0.30))
    return "uncertain", round(float(confidence), 4), tuple(reasons)


def _find_local_peaks(signal: np.ndarray, threshold: float, min_distance: int) -> list[tuple[int, float]]:
    if signal.size < 3:
        return []

    candidates = [
        (idx, float(signal[idx]))
        for idx in range(1, signal.size - 1)
        if signal[idx] >= threshold and signal[idx] >= signal[idx - 1] and signal[idx] >= signal[idx + 1]
    ]
    candidates.sort(key=lambda item: item[1], reverse=True)

    accepted: list[tuple[int, float]] = []
    for idx, value in candidates:
        if all(abs(idx - accepted_idx) >= min_distance for accepted_idx, _ in accepted):
            accepted.append((idx, value))
    accepted.sort(key=lambda item: item[0])
    return accepted


def _local_shoulders(signal: np.ndarray, peak_x: int, radius: int) -> tuple[float, float]:
    left = signal[max(0, peak_x - radius) : peak_x]
    right = signal[peak_x + 1 : min(signal.size, peak_x + radius + 1)]
    left_min = float(np.min(left)) if left.size else float(signal[peak_x])
    right_min = float(np.min(right)) if right.size else float(signal[peak_x])
    return left_min, right_min


def _peak_width(signal: np.ndarray, peak_x: int, shoulder: float, peak_value: float) -> int:
    half = shoulder + (peak_value - shoulder) * 0.5
    left = peak_x
    while left > 0 and signal[left] >= half:
        left -= 1
    right = peak_x
    while right < signal.size - 1 and signal[right] >= half:
        right += 1
    return max(1, right - left)


def _dedupe_candidates(candidates: list[FrameCandidate], min_distance: int) -> list[FrameCandidate]:
    accepted: list[FrameCandidate] = []
    for candidate in candidates:
        if all(abs(candidate.x - existing.x) >= min_distance for existing in accepted):
            accepted.append(candidate)
    return accepted


def _smooth_1d(signal: np.ndarray, window: int) -> np.ndarray:
    if signal.size == 0:
        return signal
    if window < 3:
        return signal.astype(np.float32, copy=False)
    if window % 2 == 0:
        window += 1
    window = min(window, signal.size if signal.size % 2 == 1 else signal.size - 1)
    if window < 3:
        return signal.astype(np.float32, copy=False)
    kernel = np.ones(window, dtype=np.float32) / float(window)
    pad = window // 2
    padded = np.pad(signal.astype(np.float32), pad_width=pad, mode="reflect")
    return np.convolve(padded, kernel, mode="valid")


def _normalise_1d(signal: np.ndarray) -> np.ndarray:
    signal = signal.astype(np.float32, copy=False)
    p05 = float(np.percentile(signal, 5))
    p95 = float(np.percentile(signal, 95))
    if p95 <= p05 + 1e-6:
        p95 = float(np.percentile(signal, 99))
    if p95 <= p05 + 1e-6:
        p95 = float(np.max(signal))
    if p95 <= p05 + 1e-6:
        return np.zeros_like(signal, dtype=np.float32)
    return np.clip((signal - p05) / (p95 - p05), 0.0, 1.0)


def _fill_small_gaps(mask: np.ndarray, max_gap: int) -> np.ndarray:
    filled = mask.astype(bool).copy()
    runs = _boolean_runs(~filled)
    for start, end in runs:
        if start == 0 or end == filled.size:
            continue
        if end - start <= max_gap:
            filled[start:end] = True
    return filled


def _boolean_runs(mask: np.ndarray) -> list[tuple[int, int]]:
    if mask.size == 0:
        return []
    runs: list[tuple[int, int]] = []
    in_run = False
    start = 0
    for idx, value in enumerate(mask.astype(bool)):
        if value and not in_run:
            start = idx
            in_run = True
        elif not value and in_run:
            runs.append((start, idx))
            in_run = False
    if in_run:
        runs.append((start, mask.size))
    return runs


def _json_safe(value):
    if isinstance(value, dict):
        return {str(k): _json_safe(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [_json_safe(v) for v in value]
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, np.ndarray):
        return value.tolist()
    if hasattr(value, "__dataclass_fields__"):
        return _json_safe(asdict(value))
    return value
