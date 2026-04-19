"""
Strategy: Container Gap Detection

Physical basis:
  The gap between two adjacent shipping containers is filled with AIR only.
  Air transmits X-rays completely -> every pixel row in that column is BRIGHT.

  Container cargo (even light/hollow cargo) will have at least some X-ray
  absorption in some rows. Dense cargo absorbs in ALL rows.

  Key distinction:
    • Inter-container gap: column MINIMUM (across cargo-zone rows) is HIGH
                          because every row sees only air.
    • Cargo (even light):  column MINIMUM is LOW because some rows see material.
    • Background air:      column MINIMUM is HIGH, but it spans a WIDE region.

  Therefore: find a NARROW peak in the column-minimum profile where the
  minimum brightness is HIGH. This isolates the physical gap from both cargo
  (low minimum) and background (wide region).

Algorithm:
  1. Restrict to cargo zone (20%-80% height) to avoid scanner head / undercarriage
  2. Compute column MINIMUM across cargo zone rows
  3. Gaussian smooth (small sigma to preserve narrow gap)
  4. Find LOCAL MAXIMA in the column-minimum profile in the search region (20-80% width)
  5. Prefer NARROW peaks (width < 200px at half-prominence) — gaps are narrow;
     background is wide
  6. Among qualifying peaks, select the most prominent (deepest into bright gap)
  7. Confidence from: peak prominence, how high the minimum is, narrowness
"""

import time
from typing import Optional
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks, peak_widths
import cv2

from strategies.base import BaseSplitStrategy, SplitResult

# Maximum width (in pixels) of a valid gap peak at half-prominence.
# The background air region is very wide (200-400px); the container gap is narrow.
MAX_GAP_WIDTH_PX = 250


class ContainerGapStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "container_gap"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY).astype(np.float64)
        else:
            gray = image_array.astype(np.float64)

        h, w = gray.shape
        if w < 200 or h < 50:
            return None

        # ── Cargo zone ────────────────────────────────────────────────────
        cargo_top    = int(h * 0.20)
        cargo_bottom = int(h * 0.80)
        cargo = gray[cargo_top:cargo_bottom, :]
        if cargo.shape[0] < 10:
            return None

        # ── Column minimum (the key signal) ──────────────────────────────
        # HIGH minimum = every row in this column is bright = air gap
        # LOW minimum  = at least one row has dark material = container content
        col_min = np.min(cargo, axis=0)   # shape (w,)

        # ── Smooth (small sigma — gap is narrow) ─────────────────────────
        sigma = max(3, w // 200)
        smoothed = gaussian_filter1d(col_min, sigma=sigma)

        # ── Search region ─────────────────────────────────────────────────
        search_left  = int(w * 0.15)
        search_right = int(w * 0.80)
        search_region = smoothed[search_left:search_right]

        if len(search_region) < 50:
            return None

        # ── Find local maxima ─────────────────────────────────────────────
        # Minimum prominence: 10 grey levels (gap should clearly stand above cargo)
        peaks, props = find_peaks(
            search_region,
            prominence=10.0,
            distance=max(10, w // 60),
        )

        if len(peaks) == 0:
            return None

        # ── Filter by width: keep only narrow peaks ───────────────────────
        # Compute width at half-prominence for each peak
        widths, _, _, _ = peak_widths(search_region, peaks, rel_height=0.5)

        # Select peaks narrower than MAX_GAP_WIDTH_PX
        valid = [i for i, ww in enumerate(widths) if ww <= MAX_GAP_WIDTH_PX]

        if not valid:
            # Relax: take the narrowest peak regardless of width threshold
            valid = [int(np.argmin(widths))]
            used_relaxed_width = True
        else:
            used_relaxed_width = False

        # Among valid peaks, pick the most prominent
        best_i = max(valid, key=lambda i: props["prominences"][i])
        best_peak = peaks[best_i]
        peak_prominence = float(props["prominences"][best_i])
        peak_width_px   = float(widths[best_i])
        peak_brightness = float(smoothed[search_left + int(best_peak)])

        split_x = search_left + int(best_peak)

        # Final sanity check
        if split_x < int(w * 0.15) or split_x > int(w * 0.80):
            return None

        # ── Confidence ────────────────────────────────────────────────────
        # 1. Prominence: how clearly the gap stands above surrounding cargo
        prom_score = min(peak_prominence / 30.0, 1.0)

        # 2. Absolute brightness: the gap should be genuinely bright
        #    (minimum ~150+ means even the darkest row is bright)
        brightness_score = min(peak_brightness / 180.0, 1.0)

        # 3. Narrowness: narrower = more gap-like, less background-like
        narrowness = max(0.0, 1.0 - peak_width_px / MAX_GAP_WIDTH_PX)

        # 4. Centrality (gentle)
        center_dist = abs(split_x - w / 2) / (w / 2)
        centrality  = 1.0 - center_dist * 0.4

        # Penalty if we had to relax the width constraint
        relax_penalty = 0.20 if used_relaxed_width else 0.0

        confidence = (
            0.40 * prom_score      +
            0.30 * brightness_score +
            0.20 * narrowness       +
            0.10 * centrality
        ) - relax_penalty

        confidence = float(max(0.0, min(confidence, 1.0)))

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(split_x),
            confidence=confidence,
            processing_ms=elapsed_ms,
            metadata={
                "peak_brightness":    round(peak_brightness, 2),
                "peak_prominence":    round(peak_prominence, 2),
                "peak_width_px":      round(peak_width_px, 1),
                "used_relaxed_width": used_relaxed_width,
                "search_left":        search_left,
                "search_right":       search_right,
                "sigma":              sigma,
                "cargo_top_px":       cargo_top,
                "cargo_bottom_px":    cargo_bottom,
                "image_width":        w,
                "image_height":       h,
            }
        )
