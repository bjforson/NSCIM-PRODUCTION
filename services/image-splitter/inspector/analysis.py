"""
Image-analysis operations used by the X-Ray Inspector: ROI statistics,
histograms, edge detection, thresholding, connected-component object
detection, line profiles.

All functions accept native numpy pixel arrays (16-bit or 8-bit) and
return plain-Python / JSON-serializable structures or numpy mask arrays.
"""
from __future__ import annotations

import math
from typing import Optional

import cv2
import numpy as np


# ── ROI helpers ──────────────────────────────────────────────────────

def _polygon_mask(shape: tuple[int, int], polygon: list[list[float]]) -> np.ndarray:
    """
    Rasterize a polygon to a boolean mask matching the given (h, w) shape.
    Polygon is a list of [x, y] points in image coordinates.
    """
    if not polygon or len(polygon) < 3:
        raise ValueError("polygon requires at least 3 points")
    pts = np.array([[int(round(p[0])), int(round(p[1]))] for p in polygon], dtype=np.int32)
    mask = np.zeros(shape, dtype=np.uint8)
    cv2.fillPoly(mask, [pts], 1)
    return mask.astype(bool)


def _rect_mask(shape: tuple[int, int], rect: dict) -> np.ndarray:
    x = int(max(0, rect["x"]))
    y = int(max(0, rect["y"]))
    w = int(max(0, rect["w"]))
    h = int(max(0, rect["h"]))
    mask = np.zeros(shape, dtype=bool)
    mask[y:y + h, x:x + w] = True
    return mask


def _ellipse_mask(shape: tuple[int, int], ellipse: dict) -> np.ndarray:
    cx, cy = float(ellipse["cx"]), float(ellipse["cy"])
    rx, ry = float(ellipse["rx"]), float(ellipse["ry"])
    h, w = shape
    ys, xs = np.ogrid[:h, :w]
    return (((xs - cx) / max(rx, 1)) ** 2 + ((ys - cy) / max(ry, 1)) ** 2) <= 1.0


def build_mask(shape: tuple[int, int], roi: dict) -> np.ndarray:
    """
    Build a boolean mask from a JSON-ish ROI spec:
        {"kind": "rect",    "x": ..., "y": ..., "w": ..., "h": ...}
        {"kind": "ellipse", "cx": ..., "cy": ..., "rx": ..., "ry": ...}
        {"kind": "polygon", "points": [[x,y], ...]}
        {"kind": "whole"}
    """
    kind = (roi or {}).get("kind", "whole")
    if kind == "whole":
        return np.ones(shape, dtype=bool)
    if kind == "rect":
        return _rect_mask(shape, roi)
    if kind == "ellipse":
        return _ellipse_mask(shape, roi)
    if kind == "polygon":
        return _polygon_mask(shape, roi.get("points", []))
    raise ValueError(f"unknown ROI kind: {kind!r}")


# ── Statistics ───────────────────────────────────────────────────────

def roi_stats(pixels: np.ndarray, mask: np.ndarray, bins: int = 64) -> dict:
    """
    Per-ROI pixel statistics with a histogram.

    Histogram is computed across `bins` equal-width buckets spanning the
    ROI's min..max range. For uint16 inputs, this preserves the full
    16-bit dynamic range per bucket.
    """
    if pixels.shape != mask.shape:
        raise ValueError("pixels and mask shape mismatch")
    roi = pixels[mask]
    if roi.size == 0:
        return {
            "count": 0,
            "min": None, "max": None, "mean": None, "std": None,
            "median": None, "p01": None, "p99": None,
            "histogram": {"edges": [], "counts": []},
        }
    roi_f = roi.astype(np.float64)
    hist, edges = np.histogram(roi, bins=bins)
    return {
        "count": int(roi.size),
        "min": int(roi.min()) if np.issubdtype(roi.dtype, np.integer) else float(roi.min()),
        "max": int(roi.max()) if np.issubdtype(roi.dtype, np.integer) else float(roi.max()),
        "mean": round(float(roi_f.mean()), 3),
        "std": round(float(roi_f.std()), 3),
        "median": float(np.median(roi_f)),
        "p01": float(np.percentile(roi_f, 1)),
        "p99": float(np.percentile(roi_f, 99)),
        "histogram": {
            "edges": [float(e) for e in edges.tolist()],
            "counts": [int(c) for c in hist.tolist()],
        },
    }


def global_histogram(pixels: np.ndarray, bins: int = 256) -> dict:
    hist, edges = np.histogram(pixels, bins=bins)
    return {
        "edges": [float(e) for e in edges.tolist()],
        "counts": [int(c) for c in hist.tolist()],
    }


# ── Line profile ─────────────────────────────────────────────────────

def line_profile(
    pixels: np.ndarray,
    x0: float, y0: float, x1: float, y1: float,
    samples: int = 512,
) -> dict:
    """
    Sample pixel intensities along a line segment from (x0,y0) to (x1,y1).
    Uses bilinear interpolation, returns a list of {t, value} pairs.
    """
    h, w = pixels.shape
    n = max(2, int(samples))
    ts = np.linspace(0.0, 1.0, n)
    xs = x0 + (x1 - x0) * ts
    ys = y0 + (y1 - y0) * ts

    # Clamp + bilinear sample
    xs_c = np.clip(xs, 0, w - 1)
    ys_c = np.clip(ys, 0, h - 1)
    x_lo = np.floor(xs_c).astype(np.int32)
    y_lo = np.floor(ys_c).astype(np.int32)
    x_hi = np.minimum(x_lo + 1, w - 1)
    y_hi = np.minimum(y_lo + 1, h - 1)
    dx = (xs_c - x_lo).astype(np.float32)
    dy = (ys_c - y_lo).astype(np.float32)

    p = pixels.astype(np.float32)
    tl = p[y_lo, x_lo]
    tr = p[y_lo, x_hi]
    bl = p[y_hi, x_lo]
    br = p[y_hi, x_hi]
    values = (tl * (1 - dx) * (1 - dy)
              + tr * dx * (1 - dy)
              + bl * (1 - dx) * dy
              + br * dx * dy)
    length_px = math.hypot(x1 - x0, y1 - y0)
    return {
        "length_px": length_px,
        "samples": n,
        "positions": [float(t * length_px) for t in ts.tolist()],
        "values": [float(v) for v in values.tolist()],
        "min": float(values.min()),
        "max": float(values.max()),
        "mean": float(values.mean()),
    }


# ── Edge detection ───────────────────────────────────────────────────

def edge_canny(pixels: np.ndarray, low: float, high: float) -> np.ndarray:
    """Canny edges on 8-bit input. Returns a boolean mask."""
    if pixels.dtype == np.uint16:
        eight = (pixels >> 8).astype(np.uint8)
    else:
        eight = pixels.astype(np.uint8)
    edges = cv2.Canny(eight, int(low), int(high))
    return edges > 0


def edge_sobel(pixels: np.ndarray, ksize: int = 5) -> np.ndarray:
    """Sobel gradient magnitude, returned as uint8."""
    src = pixels.astype(np.float32)
    gx = cv2.Sobel(src, cv2.CV_32F, 1, 0, ksize=ksize)
    gy = cv2.Sobel(src, cv2.CV_32F, 0, 1, ksize=ksize)
    mag = np.sqrt(gx * gx + gy * gy)
    if mag.max() > 0:
        mag = mag / mag.max() * 255.0
    return mag.astype(np.uint8)


# ── Thresholding + object detection ──────────────────────────────────

def threshold_mask(
    pixels: np.ndarray,
    low: Optional[float] = None,
    high: Optional[float] = None,
) -> np.ndarray:
    """
    Binary mask of pixels where `low <= value <= high`. Either bound can be
    None to mean "no limit". Returns a boolean mask.
    """
    if low is None and high is None:
        raise ValueError("threshold_mask requires at least one of low/high")
    mask = np.ones(pixels.shape, dtype=bool)
    if low is not None:
        mask &= pixels >= low
    if high is not None:
        mask &= pixels <= high
    return mask


def find_objects(mask: np.ndarray, min_area: int = 25, max_objects: int = 500) -> list[dict]:
    """
    Run connected-component analysis on a boolean mask and return a list
    of object descriptors:

        {"bbox": [x, y, w, h], "area": int, "centroid": [cx, cy]}

    Sorted by area descending and truncated to `max_objects`.
    """
    num, labels = cv2.connectedComponents(mask.astype(np.uint8), connectivity=8)
    objects = []
    for label_id in range(1, num):
        ys, xs = np.where(labels == label_id)
        if ys.size < min_area:
            continue
        x0, x1 = int(xs.min()), int(xs.max())
        y0, y1 = int(ys.min()), int(ys.max())
        objects.append({
            "bbox": [x0, y0, x1 - x0 + 1, y1 - y0 + 1],
            "area": int(ys.size),
            "centroid": [float(xs.mean()), float(ys.mean())],
        })
    objects.sort(key=lambda o: o["area"], reverse=True)
    return objects[:max_objects]
