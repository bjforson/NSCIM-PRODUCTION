"""
Strategy 3: Edge Detection

Container walls create strong continuous vertical edges in X-ray images.
The boundary between two containers produces the strongest vertical edge
that spans most of the image height.

This strategy:
1. Applies Canny edge detection
2. Projects edges vertically (sum per column)
3. Finds columns with the highest edge density
4. Filters for edges that span >50% of image height (continuous vertical lines)
5. Selects the best candidate in the central region
"""

import time
from typing import Optional
import numpy as np
import cv2
from scipy.signal import find_peaks
from scipy.ndimage import gaussian_filter1d

from strategies.base import BaseSplitStrategy, SplitResult


class EdgeDetectionStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "edge_detection"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        # Convert to grayscale
        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY)
        else:
            gray = image_array

        h, w = gray.shape

        if w < 200:
            return None

        # Step 1: Apply bilateral filter to reduce noise while preserving edges
        filtered = cv2.bilateralFilter(gray, d=9, sigmaColor=75, sigmaSpace=75)

        # Step 2: Canny edge detection with adaptive thresholds
        median_val = np.median(filtered)
        lower = int(max(0, 0.5 * median_val))
        upper = int(min(255, 1.5 * median_val))
        edges = cv2.Canny(filtered, lower, upper)

        # Step 3: Enhance vertical edges using Sobel X
        sobel_x = cv2.Sobel(filtered, cv2.CV_64F, 1, 0, ksize=3)
        sobel_x = np.abs(sobel_x)
        sobel_x = (sobel_x / sobel_x.max() * 255).astype(np.uint8) if sobel_x.max() > 0 else sobel_x

        # Combine Canny and Sobel for stronger vertical edge detection
        combined = cv2.addWeighted(edges, 0.5, sobel_x, 0.5, 0)

        # Step 4: Vertical projection (sum per column)
        vert_projection = np.sum(combined.astype(np.float64), axis=0)

        # Normalize
        vp_max = vert_projection.max()
        if vp_max < 1:
            return None
        vert_norm = vert_projection / vp_max

        # Smooth
        sigma = max(3, w // 150)
        vert_smooth = gaussian_filter1d(vert_norm, sigma=sigma)

        # Step 5: Find peaks in the central 70% of the image
        margin = int(w * 0.15)
        search = vert_smooth[margin:w - margin]

        peaks, properties = find_peaks(
            search,
            height=np.percentile(search, 75),
            distance=max(10, w // 30),
            prominence=0.05
        )

        peaks = peaks + margin  # adjust to full image coordinates

        if len(peaks) == 0:
            return None

        # Step 6: For each peak, check vertical continuity
        # A real container wall spans most of the image height
        best_peak = None
        best_score = 0.0

        for peak_x in peaks:
            # Check how many rows have an edge at this X position (+/- 3px tolerance)
            x_min = max(0, peak_x - 3)
            x_max = min(w, peak_x + 4)
            edge_column = np.max(edges[:, x_min:x_max], axis=1)
            continuity = np.sum(edge_column > 0) / h  # fraction of height with edges

            # Score: continuity * centrality * prominence
            center_dist = abs(peak_x - w / 2) / (w / 2)
            centrality = 1.0 - center_dist
            prominence = properties['prominences'][list(peaks - margin).tolist().index(peak_x - margin)] if (peak_x - margin) in (peaks - margin) else 0.1

            score = continuity * 0.5 + centrality * 0.3 + min(prominence, 1.0) * 0.2

            if score > best_score and continuity > 0.3:  # at least 30% height coverage
                best_score = score
                best_peak = peak_x

        if best_peak is None:
            # Fallback: just use the strongest peak in the center
            center_peaks = [(p, vert_smooth[p]) for p in peaks if abs(p - w / 2) < w * 0.3]
            if center_peaks:
                best_peak = max(center_peaks, key=lambda x: x[1])[0]
                best_score = 0.3
            else:
                return None

        elapsed_ms = int((time.time() - start) * 1000)

        return SplitResult(
            strategy_name=self.name,
            split_x=int(best_peak),
            confidence=min(best_score, 1.0),
            processing_ms=elapsed_ms,
            metadata={
                "method": "canny_sobel_vertical",
                "peaks_found": len(peaks),
                "peak_positions": [int(p) for p in peaks[:10]],
                "canny_lower": lower,
                "canny_upper": upper,
                "image_width": w,
                "image_height": h
            }
        )
