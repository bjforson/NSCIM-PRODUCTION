"""
Strategy: ISO Container Corner Fitting Detection

Observation (from domain expert):
  Every ISO shipping container has standardized corner castings (lock hooks/twist-locks)
  at its four top corners. In a dual-container side-by-side X-ray scan:

    [LEFT OUTER]              [LEFT INNER | RIGHT INNER]              [RIGHT OUTER]
         ●                          ●              ●                         ●
    ─────┬──────── left container ──┴──────────────┴── right container ──────┬─────
         │                                                                    │

  The split point lies between the two INNER corner fittings, which is also
  the midpoint between the two OUTER corner fittings.

X-ray appearance:
  - Corner castings are thick forged steel → very dark (high X-ray absorption)
  - They appear as concentrated dark spots in the TOP STRIP of the image
  - Air above containers → bright/light
  - Container contents → variable gray, but below the top strip

Algorithm:
  1. Crop to the top 15% of the image — this is where corner fittings appear
  2. Build a column-minimum profile of that strip (minimum pixel value per column)
     The minimum picks up the darkest part of each casting even if it only
     covers a few rows
  3. Smooth to merge adjacent dark pixels belonging to the same fitting
  4. Find the darkest local minimum in the LEFT quarter  → left outer corner
  5. Find the darkest local minimum in the RIGHT quarter → right outer corner
  6. Split = midpoint between left_corner_x and right_corner_x

This is robust because ISO corner castings are standardised hardware at fixed
positions regardless of cargo, so the top-strip signal is clean and unambiguous.
"""

import time
from typing import Optional
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks
import cv2

from strategies.base import BaseSplitStrategy, SplitResult


class CornerFittingStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "corner_fitting"

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

        # ── Step 1: Isolate the top strip ────────────────────────────────
        # Corner fittings appear in the top ~15% of the image.
        # Use up to 20% but at least 30px.
        strip_h = max(30, int(h * 0.15))
        top_strip = gray[:strip_h, :]   # shape (strip_h, w)

        # ── Step 2: Column-minimum profile ───────────────────────────────
        # Use minimum so that a casting spanning just a few rows still registers
        # as a very dark value for its column.
        col_min = np.min(top_strip, axis=0)    # shape (w,)  — lower = darker = denser

        # Normalize to 0–1 (0 = darkest)
        p_min, p_max = col_min.min(), col_min.max()
        if p_max - p_min < 1:
            return None
        norm = (col_min - p_min) / (p_max - p_min)

        # ── Step 3: Smooth ────────────────────────────────────────────────
        # Use a smaller sigma than the full-image strategy so we don't blur
        # two adjacent inner corner fittings into one blob.
        sigma = max(3, w // 200)
        smoothed = gaussian_filter1d(norm, sigma=sigma)

        # ── Step 4: Find corner fittings ─────────────────────────────────
        # Exclude outermost 2% (image border) from search.
        border = max(5, int(w * 0.02))

        # LEFT quarter: search left 35% for the leftmost corner fitting
        left_end   = int(w * 0.35)
        left_region = smoothed[border:left_end]
        if len(left_region) == 0:
            return None

        # Find local minima (inverted peaks) in the left region
        left_inv = -left_region
        left_peaks, left_props = find_peaks(
            left_inv,
            prominence=0.05,           # must stand out from local baseline
            distance=max(5, w // 50)
        )

        if len(left_peaks) > 0:
            # Among all minima found, take the one with highest prominence
            # (most distinct dark spot = most likely the corner fitting)
            best_left_local = int(np.argmax(left_props["prominences"]))
            best_left_idx = left_peaks[best_left_local]
            left_prominence = float(left_props["prominences"][best_left_local])
            left_corner_x = border + int(best_left_idx)
        else:
            # Fallback: absolute minimum in the left region
            left_corner_x = border + int(np.argmin(left_region))
            left_prominence = 0.02   # low prominence — no clear local minimum found

        # RIGHT quarter: search right 35% for the rightmost corner fitting
        right_start = int(w * 0.65)
        right_region = smoothed[right_start: w - border]
        if len(right_region) == 0:
            return None

        right_inv = -right_region
        right_peaks, right_props = find_peaks(
            right_inv,
            prominence=0.05,
            distance=max(5, w // 50)
        )

        if len(right_peaks) > 0:
            best_right_local = int(np.argmax(right_props["prominences"]))
            best_right_idx = right_peaks[best_right_local]
            right_prominence = float(right_props["prominences"][best_right_local])
            right_corner_x = right_start + int(best_right_idx)
        else:
            right_corner_x = right_start + int(np.argmin(right_region))
            right_prominence = 0.02

        # ── Step 5: Split at midpoint ─────────────────────────────────────
        split_x = (left_corner_x + right_corner_x) // 2

        # Sanity check: split must land in central 20–80% of image
        if split_x < int(w * 0.20) or split_x > int(w * 0.80):
            return None

        # ── Step 6: Confidence ────────────────────────────────────────────
        # Use LOCAL PROMINENCE rather than global darkness.
        # Prominence measures how much a local minimum stands out from its
        # neighbourhood — exactly the right metric for corner fitting detection.
        # Prominences are in the 0–1 normalized scale; clip to [0, 1].
        left_prom_score  = min(left_prominence  * 4.0, 1.0)   # scale: 0.25 prominence → 1.0
        right_prom_score = min(right_prominence * 4.0, 1.0)
        prominence_score = (left_prom_score + right_prom_score) / 2.0

        # Bonus when both corners are similarly prominent (symmetric fittings)
        symmetry_score = 1.0 - abs(left_prom_score - right_prom_score)

        # Centrality of split
        center_dist = abs(split_x - w / 2) / (w / 2)
        centrality = 1.0 - center_dist

        # Base confidence from physical reasoning: if we found clear local minima
        # in both outer regions the split is trustworthy
        has_both_peaks = (left_prominence > 0.02) and (right_prominence > 0.02)
        base = 0.65 if has_both_peaks else 0.40

        confidence = base * (
            0.50 * prominence_score +
            0.25 * symmetry_score +
            0.25 * centrality
        ) + (1.0 - base) * centrality   # fallback weight to centrality

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(split_x),
            confidence=min(confidence, 1.0),
            processing_ms=elapsed_ms,
            metadata={
                "left_corner_x":       int(left_corner_x),
                "right_corner_x":      int(right_corner_x),
                "left_prominence":     round(left_prominence, 3),
                "right_prominence":    round(right_prominence, 3),
                "left_peaks_found":    len(left_peaks) if len(left_peaks) > 0 else 0,
                "right_peaks_found":   len(right_peaks) if len(right_peaks) > 0 else 0,
                "corner_span_px":      int(right_corner_x - left_corner_x),
                "strip_height_px":     strip_h,
                "sigma":               sigma,
                "image_width":         w,
                "image_height":        h,
            }
        )
