"""
Inner corner casting pair detector (1.20.x).

Finds the two adjacent TOP corner castings that mark the inter-container gap
via pixel-level signal processing on the top strip of the image.

Unlike the LLM-based strategies, this is a pure CV algorithm with
deterministic sub-pixel-level precision. Intended to complement the
claude_verifier by providing an ultra-precise candidate that the verifier
can rank alongside the hand-engineered strategies.

Algorithm:
    1. Crop the TOP STRIP (upper 25% of image) at native resolution.
       Corner castings live here; cargo lives below.
    2. Convert to grayscale.
    3. Compute column-mean darkness (255 - mean pixel value per x column).
       This collapses the 2D strip into a 1D "darkness profile".
    4. Smooth the darkness profile with a small Gaussian kernel to remove
       noise while preserving casting peaks.
    5. Find prominent local MAXIMA in the darkness profile (dark vertical
       bands in the image). Corner castings are solid steel blocks that
       produce strong dark columns.
    6. A two-container trailer produces AT LEAST 4 prominent peaks:
         - Far-left: C1 left casting (near image left edge)
         - Inner-left: C1 right casting (near image center)
         - Inner-right: C2 left casting (near image center, close to inner-left)
         - Far-right: C2 right casting (near image right edge)
    7. The INNER PAIR is the two closest adjacent peaks whose midpoint is
       closest to image center (excluding peaks near the outer edges).
    8. The split_x is the midpoint between the inner pair's OUTER edges.
       We take the peak x positions as the CENTERS of the castings and add
       half the typical casting width (~12 px) to get the outer edges.

This is O(W + H) where W × H is the image dimensions, so it runs in
milliseconds.
"""
import logging
from typing import Optional

import numpy as np
from PIL import Image
import io

from strategies.base import BaseSplitStrategy, SplitResult

logger = logging.getLogger(__name__)


class InnerCastingPairStrategy(BaseSplitStrategy):
    """Find the inner pair of top corner castings and split at their gap midpoint."""

    @property
    def name(self) -> str:
        return "inner_casting_pair"

    # Tunables — all SCALE-RELATIVE (fractions of image width) so the same
    # algorithm works on both 1600-px original scans and 544-px asescan-
    # decoded bitmaps. Absolute-px thresholds from the earlier version
    # failed on 63% of the 544-px batch because a 20 px separation at that
    # width is too large for physically adjacent corner castings (which
    # are only ~6-10 px apart on a 544-px trailer image).
    TOP_STRIP_FRACTION = 0.25
    SMOOTH_SIGMA_FRAC = 0.002       # ~3 px on 1600-w; ~1 px on 544-w
    MIN_CASTING_HALF_WIDTH_FRAC = 0.005  # 8 px on 1600w; 3 px on 544w
    MIN_PEAK_DISTANCE_FRAC = 0.010  # ~16 px on 1600w; ~5 px on 544w
    MAX_PEAK_DISTANCE_FRAC = 0.055  # ~88 px on 1600w; ~30 px on 544w
    # Edge exclusion was too aggressive (12%) — on 544-px asescan bitmaps the
    # inner casting pair often sits as close as 15–30% from the left edge when
    # the trailer tractor + front overhang is still visible in the scan.
    # Dropping to 6% (~33 px on 544w, ~96 px on 1600w) still rejects the
    # outermost container-end castings but keeps most inner pairs in scope.
    EDGE_EXCLUDE_PCT = 0.06

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        try:
            return self._analyze_sync(image_data, image_array)
        except Exception as e:
            logger.error(f"[inner_casting_pair] failed: {e}", exc_info=True)
            return None

    def _analyze_sync(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        # The splitter passes image_array as a numpy array; it may be RGB or
        # grayscale. Convert to grayscale if needed.
        if image_array.ndim == 3:
            gray = image_array.mean(axis=2)
        else:
            gray = image_array
        h, w = gray.shape
        if h < 80 or w < 200:
            logger.info(f"[inner_casting_pair] image too small: {w}x{h}")
            return None

        # Scale-relative tunables derived from image width.
        # Floors keep the algorithm functional on tiny crops while still
        # honouring the intended fractions on larger images.
        smooth_sigma_px = max(1.0, self.SMOOTH_SIGMA_FRAC * w)
        min_casting_half_w = max(2, int(round(self.MIN_CASTING_HALF_WIDTH_FRAC * w)))
        min_peak_dist = max(3, int(round(self.MIN_PEAK_DISTANCE_FRAC * w)))
        max_peak_dist = max(min_peak_dist + 5, int(round(self.MAX_PEAK_DISTANCE_FRAC * w)))

        # 1. Crop the TOP STRIP
        strip_h = int(round(h * self.TOP_STRIP_FRACTION))
        strip = gray[:strip_h, :]

        # 2. Compute column-mean darkness (255 - mean)
        col_mean = strip.mean(axis=0)
        darkness = 255.0 - col_mean

        # 3. Smooth with a simple box filter (no scipy dependency)
        smoothed = self._smooth_1d(darkness, window=max(3, int(round(smooth_sigma_px * 2 + 1))))

        # 4. Find prominent local maxima
        edge_exclude = int(round(w * self.EDGE_EXCLUDE_PCT))
        search_start = edge_exclude
        # Floor the usable search range at 60% of image width so very small
        # images (e.g. 500-px asescan bitmaps) can still be analysed.
        min_search_range = max(120, int(round(w * 0.60)))
        search_end = w - edge_exclude
        if search_end - search_start < min_search_range:
            logger.info("[inner_casting_pair] search range too narrow after edge exclusion")
            return None

        search_signal = smoothed[search_start:search_end]
        median_darkness = np.median(search_signal)
        # TWO-PASS peak detection: strict threshold first (1.3×) to avoid
        # false positives on clean images, then a looser pass (1.15×) if the
        # first pass finds no valid pair. This avoids the regression observed
        # when using 1.15× universally (9 peaks on 4c3c7a6d vs 3, wrong pair
        # selected, 127-pixel error instead of 2-pixel).
        peak_threshold_strict = median_darkness * 1.3
        peak_threshold_loose = median_darkness * 1.15

        peaks = self._find_peaks(
            search_signal,
            min_distance=min_peak_dist,
            threshold=peak_threshold_strict,
        )
        peak_threshold = peak_threshold_strict
        # Adjust peak x coords back to image coordinate system
        peaks_abs = [(px + search_start, h_val) for px, h_val in peaks]

        # 5. Identify the INNER PAIR from the strict peaks.
        center_x = w / 2

        def _find_best_pair(candidate_peaks):
            bp = None
            bs = float("inf")
            for i in range(len(candidate_peaks) - 1):
                p1_x, p1_h = candidate_peaks[i]
                p2_x, p2_h = candidate_peaks[i + 1]
                sep = p2_x - p1_x
                if sep < min_peak_dist or sep > max_peak_dist:
                    continue
                midpoint = (p1_x + p2_x) / 2
                dist_from_center = abs(midpoint - center_x)
                score = dist_from_center - 0.5 * (p1_h + p2_h)
                if score < bs:
                    bs = score
                    bp = (p1_x, p1_h, p2_x, p2_h)
            return bp

        best_pair = _find_best_pair(peaks_abs) if len(peaks_abs) >= 2 else None

        # FALLBACK: if strict pass (1.3×) found no valid pair, retry with a
        # looser threshold (1.15×). This recovers images where the inner
        # castings produce weaker peaks than the outer ones without
        # regressing on images where strict already works (those get the
        # strict pair and never hit this branch).
        if best_pair is None:
            logger.info(
                f"[inner_casting_pair] strict pass ({peak_threshold_strict:.1f}) "
                f"found {len(peaks_abs)} peak(s), no valid pair — trying loose "
                f"({peak_threshold_loose:.1f})"
            )
            peaks_loose = self._find_peaks(
                search_signal,
                min_distance=min_peak_dist,
                threshold=peak_threshold_loose,
            )
            peaks_abs = [(px + search_start, h_val) for px, h_val in peaks_loose]
            peak_threshold = peak_threshold_loose
            best_pair = _find_best_pair(peaks_abs) if len(peaks_abs) >= 2 else None

        if best_pair is None:
            logger.info(
                f"[inner_casting_pair] no adjacent peak pair with separation in "
                f"[{min_peak_dist}, {max_peak_dist}] px found (both passes)"
            )
            return None

        p1_x, p1_h, p2_x, p2_h = best_pair

        # 6. Refine the OUTER edges of each casting by walking outward from
        # the peak center until the darkness drops below half-peak. The walks
        # are BOUNDED at the midpoint between the two peaks so they can't
        # cross into each other's territory (which happens when the gap
        # between two close castings isn't a deep darkness dip).
        pair_midpoint = (p1_x + p2_x) // 2
        c1_right_edge = self._find_right_half_max(
            smoothed, p1_x, p1_h, stop_at=pair_midpoint
        )
        c2_left_edge = self._find_left_half_max(
            smoothed, p2_x, p2_h, stop_at=pair_midpoint
        )

        if c1_right_edge >= c2_left_edge:
            # Both half-max walks converged at (or past) the pair midpoint
            # without dropping below half-peak. Physical meaning: the two
            # castings are touching with no visible darkness dip between
            # them (common for containers sitting end-to-end on a chassis).
            # In that case the best split is simply the geometric midpoint
            # between the two peak centers.
            logger.info(
                f"[inner_casting_pair] edge refinement converged at midpoint "
                f"({c1_right_edge}={c2_left_edge}) — using peak-center midpoint"
            )
            split_x = (p1_x + p2_x) // 2
            # Reconstruct synthetic outer edges at ±half_width from each peak
            c1_right_edge = min(split_x, p1_x + min_casting_half_w)
            c2_left_edge = max(split_x, p2_x - min_casting_half_w)
            gap_width = max(0, c2_left_edge - c1_right_edge)
        else:
            split_x = int(round((c1_right_edge + c2_left_edge) / 2))
            gap_width = c2_left_edge - c1_right_edge

        # Confidence heuristic:
        # - strong peaks (high darkness relative to median)
        # - small gap (castings close together = real inner pair)
        # - symmetric placement of midpoint around image center
        peak_strength = (p1_h + p2_h) / (2 * median_darkness)
        gap_penalty = max(0, (gap_width - 15) / 30.0)  # gap > 15 px = less confident
        symmetry = 1.0 - min(1.0, abs(split_x - center_x) / (w / 4))
        confidence = float(
            min(1.0, max(0.3, 0.55 + 0.15 * (peak_strength - 1.3) - gap_penalty + 0.15 * symmetry))
        )

        logger.info(
            f"[inner_casting_pair] peaks=({p1_x}, {p2_x}) "
            f"outer_edges=({c1_right_edge}, {c2_left_edge}) "
            f"gap_width={gap_width} split_x={split_x} "
            f"peak_strength={peak_strength:.2f} conf={confidence:.3f}"
        )

        return SplitResult(
            strategy_name=self.name,
            split_x=split_x,
            confidence=confidence,
            processing_ms=0,
            metadata={
                "c1_right_peak_x": int(p1_x),
                "c1_right_peak_darkness": round(float(p1_h), 2),
                "c2_left_peak_x": int(p2_x),
                "c2_left_peak_darkness": round(float(p2_h), 2),
                "c1_right_casting_x_end": int(c1_right_edge),
                "c2_left_casting_x_start": int(c2_left_edge),
                "gap_width_px": int(gap_width),
                "peak_strength_ratio": round(float(peak_strength), 3),
                "median_darkness": round(float(median_darkness), 2),
                "peak_threshold": round(float(peak_threshold), 2),
                "top_strip_height_px": int(strip_h),
                "total_peaks_found": len(peaks_abs),
            },
        )

    @staticmethod
    def _smooth_1d(signal: np.ndarray, window: int) -> np.ndarray:
        """Simple box filter (moving average). window should be odd."""
        if window < 3:
            return signal
        if window % 2 == 0:
            window += 1
        kernel = np.ones(window) / window
        # Reflect padding so edges aren't attenuated
        pad = window // 2
        padded = np.concatenate([signal[pad:0:-1], signal, signal[-2:-pad - 2:-1]])
        return np.convolve(padded, kernel, mode="valid")

    @staticmethod
    def _find_peaks(signal: np.ndarray, min_distance: int, threshold: float):
        """Find local maxima above threshold, separated by at least min_distance.
        Returns [(index, height), ...] sorted by height descending, then re-sorted
        by index ascending at the end.
        """
        n = len(signal)
        if n < 3:
            return []
        # Scan for local maxima
        candidates = []
        for i in range(1, n - 1):
            if signal[i] >= threshold and signal[i] >= signal[i - 1] and signal[i] >= signal[i + 1]:
                candidates.append((i, float(signal[i])))
        if not candidates:
            return []
        # Greedy NMS: sort by height desc, accept peak if no accepted peak within min_distance
        candidates.sort(key=lambda t: t[1], reverse=True)
        accepted = []
        for idx, h in candidates:
            if all(abs(idx - a_idx) >= min_distance for a_idx, _ in accepted):
                accepted.append((idx, h))
        # Re-sort by index ascending
        accepted.sort(key=lambda t: t[0])
        return accepted

    @staticmethod
    def _find_right_half_max(signal: np.ndarray, peak_idx: int, peak_height: float, stop_at: int = None) -> int:
        """Walk right from peak until signal drops below half-peak (or we hit
        stop_at, whichever comes first). Returns x."""
        half = peak_height * 0.5
        n = len(signal)
        limit = min(n - 1, stop_at if stop_at is not None else n - 1)
        i = peak_idx
        while i < limit and signal[i] > half:
            i += 1
        return i

    @staticmethod
    def _find_left_half_max(signal: np.ndarray, peak_idx: int, peak_height: float, stop_at: int = None) -> int:
        """Walk left from peak until signal drops below half-peak (or we hit
        stop_at, whichever comes first). Returns x."""
        half = peak_height * 0.5
        limit = max(0, stop_at if stop_at is not None else 0)
        i = peak_idx
        while i > limit and signal[i] > half:
            i -= 1
        return i
