"""
Polarity-aware foreground seam strategy.

The older gap strategies assume one X-ray polarity: air is bright and dense
cargo is dark. ASE-derived JPEGs often arrive with the opposite visual polarity,
so a strategy can look confident while cutting inside the second container.

This strategy first classifies foreground vs background/cargo-air polarity from
the image itself, then searches for a vertical seam where foreground occupancy
drops between two broad foreground masses.
"""

import time
from typing import Optional

import cv2
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks, peak_widths

from strategies.base import BaseSplitStrategy, SplitResult


class ForegroundSeamStrategy(BaseSplitStrategy):
    """Find the low-foreground seam between two containers."""

    @property
    def name(self) -> str:
        return "foreground_seam"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        if image_array.ndim == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY)
        else:
            gray = image_array

        gray = gray.astype(np.uint8)
        h, w = gray.shape
        if w < 200 or h < 80:
            return None

        # Use the middle band: enough cargo/container body to identify two
        # masses, while avoiding scanner head and undercarriage artifacts.
        y0 = int(h * 0.12)
        y1 = int(h * 0.88)
        band = gray[y0:y1, :]
        if band.shape[0] < 20:
            return None

        threshold, _ = cv2.threshold(
            band, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU
        )

        global_median = float(np.median(gray))
        left_strip = gray[:, : max(1, int(w * 0.04))]
        right_strip = gray[:, int(w * 0.94) :]
        left_edge = float(np.median(left_strip))
        right_edge = float(np.median(right_strip))
        left_std = float(np.std(left_strip))
        right_std = float(np.std(right_strip))

        # Pick the side edge that is most background-like. The clean air strip
        # usually has much lower variance than the edge that still contains
        # truck/container structure. If both edges are similarly smooth, use the
        # edge that differs most from the full-image median.
        if min(left_std, right_std) < max(left_std, right_std) * 0.75:
            background_level = left_edge if left_std < right_std else right_edge
        else:
            left_delta = abs(left_edge - global_median)
            right_delta = abs(right_edge - global_median)
            background_level = right_edge if right_delta >= left_delta else left_edge
        foreground_is_dark = background_level > global_median

        if foreground_is_dark:
            foreground = (band < threshold).astype(np.uint8)
        else:
            foreground = (band > threshold).astype(np.uint8)

        # Bridge thin vertical breaks in cargo/walls and remove small speckles.
        kernel_h = max(3, int(h * 0.02))
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, kernel_h))
        foreground = cv2.morphologyEx(foreground, cv2.MORPH_CLOSE, kernel)
        foreground = cv2.morphologyEx(foreground, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))

        occupancy = foreground.mean(axis=0)
        smoothed = gaussian_filter1d(occupancy, sigma=max(2, w // 250))

        search_left = int(w * 0.18)
        search_right = int(w * 0.82)
        search = smoothed[search_left:search_right]
        if search.size < 50:
            return None

        valleys, props = find_peaks(
            -search,
            prominence=0.04,
            distance=max(12, w // 80),
        )
        if len(valleys) == 0:
            return None

        widths = peak_widths(-search, valleys, rel_height=0.5)[0]
        candidates = []
        support_inner = max(8, int(w * 0.035))
        support_outer = max(support_inner + 5, int(w * 0.18))
        max_good_width = max(24.0, w * 0.09)

        for i, local_x in enumerate(valleys):
            split_x = search_left + int(local_x)
            left_start = max(0, split_x - support_outer)
            left_end = max(0, split_x - support_inner)
            right_start = min(w, split_x + support_inner)
            right_end = min(w, split_x + support_outer)
            if left_end <= left_start or right_end <= right_start:
                continue

            left_support = float(np.percentile(smoothed[left_start:left_end], 75))
            right_support = float(np.percentile(smoothed[right_start:right_end], 75))
            seam_occupancy = float(smoothed[split_x])
            support = min(left_support, right_support)
            drop = max(0.0, support - seam_occupancy)
            width_px = float(widths[i])
            width_score = max(0.0, 1.0 - min(width_px / max_good_width, 1.0))
            center_score = 1.0 - abs(split_x - w / 2.0) / (w / 2.0)
            prominence = float(props["prominences"][i])

            # Prefer seams that are narrow, supported by foreground on both
            # sides, and visibly drop from those side masses.
            score = (
                0.45 * drop
                + 0.25 * support
                + 0.15 * width_score
                + 0.10 * center_score
                + 0.05 * min(prominence / 0.4, 1.0)
            )
            candidates.append(
                {
                    "score": float(score),
                    "split_x": int(split_x),
                    "seam_occupancy": seam_occupancy,
                    "side_support": support,
                    "drop": float(drop),
                    "width_px": width_px,
                    "prominence": prominence,
                    "center_score": float(center_score),
                }
            )

        if not candidates:
            return None

        candidates.sort(key=lambda item: item["score"], reverse=True)
        best = candidates[0]

        # The score is a heuristic; cap confidence so this new candidate can
        # win by consensus/ranking, not by raw confidence alone.
        confidence = max(0.30, min(0.82, 0.30 + best["score"] * 0.85))

        elapsed_ms = int((time.time() - start) * 1000)
        return SplitResult(
            strategy_name=self.name,
            split_x=best["split_x"],
            confidence=float(confidence),
            processing_ms=elapsed_ms,
            metadata={
                "method": "polarity_aware_foreground_seam",
                "foreground_is_dark": foreground_is_dark,
                "otsu_threshold": round(float(threshold), 2),
                "background_level": round(float(background_level), 2),
                "global_median": round(global_median, 2),
                "left_edge_median": round(left_edge, 2),
                "right_edge_median": round(right_edge, 2),
                "left_edge_std": round(left_std, 2),
                "right_edge_std": round(right_std, 2),
                "seam_occupancy": round(best["seam_occupancy"], 4),
                "side_support": round(best["side_support"], 4),
                "seam_drop": round(best["drop"], 4),
                "seam_width_px": round(best["width_px"], 1),
                "seam_prominence": round(best["prominence"], 4),
                "ranked_candidates": [
                    {
                        "split_x": c["split_x"],
                        "score": round(c["score"], 4),
                        "drop": round(c["drop"], 4),
                        "width_px": round(c["width_px"], 1),
                    }
                    for c in candidates[:5]
                ],
                "image_width": w,
                "image_height": h,
            },
        )
