"""
Strategy: Air Gap Detection (v2)

Physical basis:
  In an X-ray image of two side-by-side containers, the gap between the
  inner steel walls transmits almost all X-rays (it's just air) and appears
  as a BRIGHT vertical band. Container steel walls absorb X-rays -> DARK.

  Key challenges:
    1. Background air (beyond the right container) is also very bright —
       must not be mistaken for the gap between containers.
    2. Some cargo (hollow machinery, light materials) creates internal bright
       bands that can be mistaken for the gap.

  Solution:
    - Auto-detect the active container extent (exclude bright background)
    - Use column MAXIMUM (90th percentile) with small smoothing so that even
      a narrow air gap registers as a sharp bright peak
    - Require the peak to be a genuine local maximum (flanked by darker material)
    - Cross-validate: the best candidate is the one with highest combined
      prominence AND steepest dark-bright-dark sandwich pattern

Algorithm:
  1. Convert to grayscale; work on cargo zone (20%-80% image height)
  2. Compute column 90th-percentile brightness (robust max)
  3. Detect container extent: trim bright background from right edge
  4. Gaussian smooth (small sigma) the profile within container extent
  5. Find LOCAL MAXIMA in the trimmed search region
  6. Score each by: prominence + sandwich score (dark on both sides)
  7. Select best candidate; derive confidence from scores
"""

import time
from typing import Optional, Tuple
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks
import cv2

from strategies.base import BaseSplitStrategy, SplitResult

# Column is considered "background" (not container) if its 90th-pct brightness
# exceeds this level in the cargo zone.
BACKGROUND_BRIGHTNESS_THRESHOLD = 200


class AirGapStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "air_gap"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        # Convert to grayscale
        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY).astype(np.float64)
        else:
            gray = image_array.astype(np.float64)

        h, w = gray.shape
        if w < 200 or h < 50:
            return None

        # ── Step 1: Cargo zone ─────────────────────────────────────────────
        cargo_top    = int(h * 0.20)
        cargo_bottom = int(h * 0.80)
        cargo = gray[cargo_top:cargo_bottom, :]
        if cargo.shape[0] < 10:
            return None

        # ── Step 2: Column 90th-percentile brightness ─────────────────────
        # 90th-pct captures bright pixels (air gap) without being dominated
        # by the absolute maximum (which could be a sensor artifact).
        col_p90 = np.percentile(cargo, 90, axis=0)   # shape (w,)

        # ── Step 3: Detect container extent (trim background) ─────────────
        # Scan from the right: find where the column consistently drops below
        # the background threshold — that's the right edge of the right container.
        # We search for the gap only INSIDE the container extent.
        content_right = w - 1
        for x in range(w - 1, int(w * 0.50), -1):
            if col_p90[x] < BACKGROUND_BRIGHTNESS_THRESHOLD:
                content_right = x
                break

        # Also determine content_left (trim dead black left border if any)
        content_left = 0
        for x in range(0, int(w * 0.30)):
            if col_p90[x] < BACKGROUND_BRIGHTNESS_THRESHOLD:
                content_left = x
                break

        # Effective container span
        container_width = content_right - content_left
        if container_width < 100:
            return None

        # ── Step 4: Search region within container extent ─────────────────
        # Search the inner 20%-80% of the container span (avoid outer walls)
        margin_left  = content_left + int(container_width * 0.20)
        margin_right = content_left + int(container_width * 0.80)

        # Also apply absolute image-width constraint (never go past 85%)
        search_left  = max(int(w * 0.10), margin_left)
        search_right = min(int(w * 0.85), margin_right)

        if search_right - search_left < 50:
            return None

        # ── Step 5: Smooth (small sigma to preserve narrow gap) ───────────
        sigma = max(3, container_width // 150)
        smoothed = gaussian_filter1d(col_p90, sigma=sigma)

        search_region = smoothed[search_left:search_right]

        # ── Step 6: Find local maxima ─────────────────────────────────────
        peaks, props = find_peaks(
            search_region,
            prominence=3.0,
            distance=max(8, container_width // 40),
        )

        if len(peaks) == 0:
            # Fallback: absolute maximum in search region
            fallback_local = int(np.argmax(search_region))
            split_x = search_left + fallback_local
            peak_brightness = float(smoothed[split_x])
            peak_prominence = 0.0
            sandwich_score  = 0.0
            used_fallback   = True
        else:
            # ── Step 7: Score each candidate by prominence + sandwich ─────
            # Sandwich: a genuine gap should be FLANKED by darker material.
            # For each peak, measure mean brightness 30-80px on each side
            # and check that the peak is brighter than both flanks.
            best_score = -1.0
            best_peak_local = int(peaks[0])
            peak_brightness = 0.0
            peak_prominence = 0.0
            sandwich_score  = 0.0

            for pi, pk in enumerate(peaks):
                px = search_left + int(pk)
                prom = float(props["prominences"][pi])
                pbright = float(smoothed[px])

                # Sandwich: mean brightness 30-80px to left and right
                l_start = max(0, px - 80)
                l_end   = max(0, px - 30)
                r_start = min(w - 1, px + 30)
                r_end   = min(w - 1, px + 80)

                left_dark  = float(np.mean(smoothed[l_start:l_end]))   if l_end > l_start else pbright
                right_dark = float(np.mean(smoothed[r_start:r_end]))   if r_end > r_start else pbright

                # How much brighter is the peak than its flanks?
                sandwich = pbright - max(left_dark, right_dark)

                combined = prom * 0.5 + max(sandwich, 0) * 0.5
                if combined > best_score:
                    best_score = combined
                    best_peak_local = int(pk)
                    peak_brightness = pbright
                    peak_prominence = prom
                    sandwich_score  = sandwich

            split_x = search_left + best_peak_local
            used_fallback = False

        # Final sanity: must be in 10%-85% of image
        if split_x < int(w * 0.10) or split_x > int(w * 0.85):
            return None

        # ── Step 8: Confidence ─────────────────────────────────────────────
        # 1. Prominence (how much the peak stands above neighbours)
        prom_score = min(peak_prominence / 25.0, 1.0)

        # 2. Sandwich score (flanked by darker material — confirms it's a gap)
        sandwich_norm = min(max(sandwich_score, 0) / 20.0, 1.0)

        # 3. Contrast within region
        region_mean = float(np.mean(search_region))
        region_std  = float(np.std(search_region))
        contrast_score = 0.0
        if region_std > 0:
            z = (peak_brightness - region_mean) / region_std
            contrast_score = min(max(z / 3.0, 0.0), 1.0)

        # 4. Centrality within container span (gentle)
        center_of_containers = (content_left + content_right) / 2.0
        dist_from_center = abs(split_x - center_of_containers) / max(container_width / 2.0, 1)
        centrality = 1.0 - dist_from_center * 0.4

        # Fallback penalty
        fallback_penalty = 0.35 if used_fallback else 0.0

        confidence = (
            0.40 * prom_score     +
            0.35 * sandwich_norm  +
            0.15 * contrast_score +
            0.10 * centrality
        ) - fallback_penalty

        confidence = float(max(0.0, min(confidence, 1.0)))

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(split_x),
            confidence=confidence,
            processing_ms=elapsed_ms,
            metadata={
                "peak_brightness":       round(peak_brightness, 2),
                "peak_prominence":       round(peak_prominence, 2),
                "sandwich_score":        round(sandwich_score, 2),
                "region_mean":           round(region_mean, 2),
                "region_std":            round(region_std, 2),
                "content_left":          content_left,
                "content_right":         content_right,
                "container_width_px":    container_width,
                "search_left":           search_left,
                "search_right":          search_right,
                "used_fallback":         used_fallback,
                "sigma":                 sigma,
                "image_width":           w,
                "image_height":          h,
            }
        )
