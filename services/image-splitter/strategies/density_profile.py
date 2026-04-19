"""
Strategy 1: Vertical Density Profile Analysis

X-ray scan images have distinctive signatures at container boundaries:
- Steel container walls appear as high-density vertical bands
- The gap between containers shows as a narrow low-density valley between peaks
- Pattern: [contents] → [PEAK: wall] → [DIP: gap] → [PEAK: wall] → [contents]

This strategy:
1. Converts to grayscale
2. Sums pixel intensities per column → 1D density profile
3. Smooths with Gaussian filter to reduce noise
4. Finds prominent peaks (potential steel walls)
5. Looks for "double peak with valley" patterns
6. Selects the best candidate split point at the valley center
"""

import time
from typing import Optional
import numpy as np
from scipy.signal import find_peaks
from scipy.ndimage import gaussian_filter1d
import cv2

from strategies.base import BaseSplitStrategy, SplitResult


class DensityProfileStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "density_profile"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        # Convert to grayscale if needed
        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY)
        else:
            gray = image_array

        h, w = gray.shape

        # Skip if image is too narrow for two containers
        if w < 200:
            return None

        # Step 1: Compute column density profile (sum intensities per column)
        density = np.sum(gray.astype(np.float64), axis=0)

        # Step 2: Normalize to 0-1 range
        d_min, d_max = density.min(), density.max()
        if d_max - d_min < 1:
            return None
        density_norm = (density - d_min) / (d_max - d_min)

        # Step 3: Smooth to reduce noise
        sigma = max(5, w // 100)  # adaptive smoothing
        density_smooth = gaussian_filter1d(density_norm, sigma=sigma)

        # Step 4: Find peaks (potential steel walls)
        # Only look in the middle 80% of the image (containers won't split at edges)
        margin = int(w * 0.1)
        search_region = density_smooth[margin:w - margin]

        # Peaks should be prominent (high density = steel walls)
        peak_height = np.percentile(search_region, 70)
        peaks, properties = find_peaks(
            search_region,
            height=peak_height,
            distance=max(20, w // 20),  # min distance between peaks
            prominence=0.05
        )

        # Adjust peak indices back to full image coordinates
        peaks = peaks + margin

        if len(peaks) < 2:
            # Fallback: try inverted profile (look for density DIP in center)
            return self._fallback_valley_detection(density_smooth, w, h, start)

        # Step 5: Find the best "double peak with valley" pattern
        best_split = None
        best_score = 0.0

        for i in range(len(peaks) - 1):
            p1, p2 = peaks[i], peaks[i + 1]

            # Valley is the minimum between two consecutive peaks
            valley_region = density_smooth[p1:p2]
            if len(valley_region) == 0:
                continue

            valley_min_idx = p1 + np.argmin(valley_region)
            valley_depth = density_smooth[p1] - density_smooth[valley_min_idx]
            peak_avg = (density_smooth[p1] + density_smooth[p2]) / 2

            # Score: deeper valley + more central position = better
            center_dist = abs(valley_min_idx - w / 2) / (w / 2)
            centrality_score = 1.0 - center_dist  # 1.0 = dead center
            depth_score = min(valley_depth / (peak_avg + 1e-6), 1.0)

            # Combined score
            score = 0.6 * depth_score + 0.4 * centrality_score

            if score > best_score:
                best_score = score
                best_split = valley_min_idx

        if best_split is None:
            return self._fallback_valley_detection(density_smooth, w, h, start)

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(best_split),
            confidence=min(best_score, 1.0),
            processing_ms=elapsed_ms,
            metadata={
                "peaks_found": len(peaks),
                "peak_positions": [int(p) for p in peaks],
                "method": "double_peak_valley",
                "image_width": w,
                "image_height": h,
                "smoothing_sigma": sigma
            }
        )

    def _fallback_valley_detection(
        self, density_smooth: np.ndarray, w: int, h: int, start: float
    ) -> Optional[SplitResult]:
        """
        Fallback: Look for the deepest valley in the central region.
        Used when peak detection doesn't find a clear double-peak pattern.
        """
        # Search the central 60% of the image
        left = int(w * 0.2)
        right = int(w * 0.8)
        center_region = density_smooth[left:right]

        if len(center_region) == 0:
            return None

        # Find local minima (valleys)
        inverted = -center_region
        valleys, properties = find_peaks(
            inverted,
            distance=max(20, w // 20),
            prominence=0.03
        )

        if len(valleys) == 0:
            return None

        # Pick the most prominent valley closest to center
        best_idx = None
        best_score = 0.0

        for v_idx in valleys:
            abs_idx = v_idx + left
            center_dist = abs(abs_idx - w / 2) / (w / 2)
            centrality = 1.0 - center_dist

            prominence = properties["prominences"][list(valleys).index(v_idx)]
            score = 0.5 * prominence + 0.5 * centrality

            if score > best_score:
                best_score = score
                best_idx = abs_idx

        if best_idx is None:
            return None

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(best_idx),
            confidence=min(best_score * 0.7, 1.0),  # lower confidence for fallback
            processing_ms=elapsed_ms,
            metadata={
                "method": "fallback_valley",
                "valleys_found": len(valleys),
                "image_width": w,
                "image_height": h
            }
        )
