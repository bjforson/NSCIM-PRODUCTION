"""
Strategy: Steel Wall Midpoint  (with Gap-Peak Refinement)

X-ray physics:
  - Dense material (steel container walls, cargo) ABSORBS X-rays → appears DARK (low pixel value)
  - Air (low density) TRANSMITS X-rays → appears BRIGHT/WHITE (high pixel value)

Column profile normalization:
  norm = (profile - p_min) / (p_max - p_min)
  After normalization: 0 = darkest column (dense cargo/steel), 1 = brightest column (background air)

Image structure:
  These scanner images typically have:
    - LEFT side: container starts at or very near the left image edge (NO visible background)
    - RIGHT side: always has a clearly bright background air region
  Therefore, the left and right walls are detected differently.

Left wall algorithm (argmin + cargo detection):
  Use argmin in the left 40% to find the darkest column (outer left wall).
  If the argmin result is more than LEFT_WALL_MAX_FRACTION into the image width, it likely
  found dense cargo rather than the thin outer steel wall — fall back to the image border.

Right wall algorithm (edge-scan from right):
  The right background is always bright and clearly visible (smoothed ≈ 0.94).
  Scan RIGHT→LEFT from the right border and stop at the first column that is
  significantly darker than the background (below right_bg - WALL_DELTA).
  This finds the right outer container wall from the outside, not the densest
  cargo from the inside — regardless of how dense the right cargo is.

Gap-peak refinement (why midpoint alone is insufficient):
  The midpoint of (left_wall, right_wall) is correct only when both containers are
  equal width. When the right container is wider (common: 20-70px wider), the
  midpoint lands inside the right container, causing the left image to capture
  ~5% of the right container.

  Fix: The physical air gap is the ONLY image region where EVERY row is bright
  (full-column air). col_min per column (min pixel across all rows) is therefore
  high exclusively at the gap and at the right background — not at bright cargo
  features, which are bright in only SOME rows.

  After computing the midpoint, search for the col_min peak within ±GAP_SEARCH_RADIUS
  pixels of the midpoint. If that peak is bright enough to be air, use it as split_x.
  Otherwise fall back to the midpoint.

Why this is better than pure argmin:
  argmin finds the DARKEST column in the search zone. When heavy cargo
  (dense metal, machinery) is present, argmin finds cargo at e.g. rw=980
  instead of the real right outer wall at rw=1330. The edge-scan approach
  always finds the boundary from the outside.
"""

import time
from typing import Optional
import numpy as np
from scipy.ndimage import gaussian_filter1d
import cv2

from strategies.base import BaseSplitStrategy, SplitResult

# Right-wall edge scan: how much darker than the right background a column
# must be to be considered the start of the container (not background).
WALL_DELTA = 0.25

# If argmin places the left wall further than this fraction of image width
# from the left, it likely found cargo inside the container rather than the
# outer steel wall. Fall back to the image border in that case.
# Kept deliberately tight (6%): the left container always starts at or very
# near the left image edge (no visible background on the left). Any dark
# column found more than 6% from the edge is almost certainly internal cargo
# (engine block, dense freight, structural beam) — not the outer steel wall.
LEFT_WALL_MAX_FRACTION = 0.06


class SteelWallMidpointStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "steel_wall_midpoint"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        # Convert to grayscale
        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY).astype(np.float64)
        else:
            gray = image_array.astype(np.float64)

        h, w = gray.shape
        if w < 200:
            return None

        # ── Column darkness profile ───────────────────────────────────────
        col_min  = np.min(gray,  axis=0)
        col_mean = np.mean(gray, axis=0)
        profile  = 0.7 * col_min + 0.3 * col_mean

        p_min, p_max = profile.min(), profile.max()
        if p_max - p_min < 1:
            return None
        # norm: 0 = darkest column (dense material), 1 = brightest column (air)
        norm = (profile - p_min) / (p_max - p_min)

        sigma = max(4, w // 120)
        smoothed = gaussian_filter1d(norm, sigma=sigma)

        border      = max(5, int(w * 0.03))
        left_end    = int(w * 0.40)
        right_start = int(w * 0.60)

        # ── Left wall: argmin in first 40% with cargo-detection fallback ──
        left_region = smoothed[border:left_end]
        if len(left_region) == 0:
            return None

        lw_argmin   = border + int(np.argmin(left_region))
        left_max_px = int(w * LEFT_WALL_MAX_FRACTION)

        if lw_argmin > left_max_px:
            left_wall_x  = border
            left_method  = "border_fallback"
        else:
            left_wall_x  = lw_argmin
            left_method  = "argmin"

        left_wall_darkness = 1.0 - float(smoothed[left_wall_x])

        # ── Right wall: edge-scan from right background ───────────────────
        right_bg     = float(np.mean(smoothed[w - border:]))
        right_thresh = right_bg - WALL_DELTA

        right_wall_x = None
        right_method = "argmin"

        if right_bg > 0.60:
            for x in range(w - border - 1, right_start - 1, -1):
                if smoothed[x] <= right_thresh:
                    refine_lo = max(right_start, x - 80)
                    refine_hi = min(w - border, x + 25)
                    local_argmin = int(np.argmin(smoothed[refine_lo:refine_hi]))
                    right_wall_x = refine_lo + local_argmin
                    right_method = "edge_scan"
                    break

        if right_wall_x is None:
            right_region = smoothed[right_start: w - border]
            if len(right_region) == 0:
                return None
            right_wall_x = right_start + int(np.argmin(right_region))

        right_wall_darkness = 1.0 - float(smoothed[right_wall_x])

        # ── Validate positions ────────────────────────────────────────────
        if left_wall_x >= left_end or right_wall_x <= right_start:
            return None

        # ── Midpoint of outer walls (baseline estimate) ───────────────────
        midpoint     = (left_wall_x + right_wall_x) // 2
        split_x      = midpoint
        split_method = "midpoint"

        # ── Corner casting pair refinement ─────────────────────────────────
        # Container corner castings (twist locks) are standardized ISO
        # steel blocks — the densest structural feature in the scan.
        # In the top ~20% of the image, they appear as dark peaks in
        # col_min. The inner pair flanks the gap between containers.
        # Split = midpoint of the inner pair.
        #
        # Algorithm:
        #   1. Take the top 20% strip of the image
        #   2. Compute col_min (darkest pixel per column across strip rows)
        #   3. Find peaks in the inverted profile (peaks = dark features)
        #   4. Look for the strongest peak LEFT and RIGHT of the midpoint
        #      within ±60px — these are the inner castings
        #   5. Split at (left_peak + right_peak) / 2
        #   6. Fall back to midpoint if pair not found
        from scipy.signal import find_peaks as _find_peaks

        casting_left  = None
        casting_right = None

        strip_h = int(h * 0.20)
        if strip_h > 10:
            top_strip   = gray[:strip_h, :]
            col_min_top = np.min(top_strip, axis=0)
            smooth_top  = gaussian_filter1d(col_min_top, sigma=8)
            inverted    = smooth_top.max() - smooth_top

            peaks, _ = _find_peaks(inverted, distance=15, prominence=3)

            CASTING_SEARCH = 60  # search ±60px from midpoint

            left_peaks  = [p for p in peaks if midpoint - CASTING_SEARCH <= p < midpoint]
            right_peaks = [p for p in peaks if midpoint < p <= midpoint + CASTING_SEARCH]

            if left_peaks:
                casting_left = int(max(left_peaks, key=lambda p: inverted[p]))
            if right_peaks:
                casting_right = int(max(right_peaks, key=lambda p: inverted[p]))

            if casting_left is not None and casting_right is not None:
                split_x      = (casting_left + casting_right) // 2
                split_method = "casting_pair"

        if split_x < int(w * 0.20) or split_x > int(w * 0.80):
            return None

        span_px = right_wall_x - left_wall_x

        # ── Confidence ────────────────────────────────────────────────────
        wall_darkness_score = (left_wall_darkness + right_wall_darkness) / 2.0

        center_dist      = abs(split_x - w / 2) / (w / 2)
        centrality_score = 1.0 - center_dist

        span_px    = right_wall_x - left_wall_x
        span_ratio = span_px / w
        span_score = min(span_ratio / 0.70, 1.0)

        # Bonus when right wall was detected by edge-scan (more reliable)
        edge_bonus = 0.05 if right_method == "edge_scan" else 0.0

        confidence = (
            0.50 * wall_darkness_score +
            0.25 * span_score          +
            0.20 * centrality_score
        ) + edge_bonus

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(split_x),
            confidence=min(confidence, 1.0),
            processing_ms=elapsed_ms,
            metadata={
                "left_wall_x":          int(left_wall_x),
                "right_wall_x":         int(right_wall_x),
                "left_wall_darkness":   round(left_wall_darkness, 3),
                "right_wall_darkness":  round(right_wall_darkness, 3),
                "left_method":          left_method,
                "right_method":         right_method,
                "split_method":         split_method,
                "midpoint":             int(midpoint),
                "right_bg":             round(right_bg, 3),
                "right_threshold":      round(right_thresh, 3),
                "container_span_px":    int(span_px),
                "span_ratio":           round(span_ratio, 3),
                "casting_left":         casting_left,
                "casting_right":        casting_right,
                "sigma":                sigma,
                "image_width":          w,
                "image_height":         h,
            }
        )
