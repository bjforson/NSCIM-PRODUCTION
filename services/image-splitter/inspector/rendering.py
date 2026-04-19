"""
Rendering helpers — turn decoded numpy pixel arrays into PNG/JPEG bytes.

Default bit depth follows the project-wide rule (see
`feedback_xray_rendering_raw16.md`):

    - 16-bit channels (ASE main, FS6000 high/low) default to 16-bit PNG
    - 8-bit channels (FS6000 material) default to 8-bit PNG
    - Cooked 8-bit variants (CLAHE, percentile, log, gamma, windowlevel) are
      opt-in via the `transform` argument

All outputs are returned as raw `bytes` ready to stream back to the HTTP client.
"""
from __future__ import annotations

import io
from typing import Optional

import cv2
import numpy as np
from PIL import Image

# ── 16-bit lossless path (default) ────────────────────────────────────

def render_raw_binary(arr: np.ndarray, rotate_deg: int = 0) -> bytes:
    """
    Pack pixels into a tiny binary envelope for direct client-side rendering.

    Layout (little-endian):
        offset  size  field
        0       4     magic 'XRAY'
        4       1     bit_depth   (8 or 16)
        5       1     channels    (1 — reserved for future RGB)
        6       2     flags       (reserved; currently always 0)
        8       4     width u32
        12      4     height u32
        16      ...   width*height*(bit_depth/8) bytes of pixel data

    The payload is raw numpy bytes — no compression, no PNG overhead. This
    is the wire format used by the canvas viewer's interactive
    window/level path so the browser can keep a true uint16 buffer in
    memory and re-render at drag speed.
    """
    if rotate_deg:
        # Rotate using numpy for consistency with render_raw_png
        from PIL import Image as _Image
        pil = _Image.fromarray(arr)
        pil = pil.rotate(rotate_deg, expand=True)
        arr = np.asarray(pil)

    if arr.dtype == np.uint16:
        bit_depth = 16
        payload = np.ascontiguousarray(arr, dtype="<u2").tobytes()
    elif arr.dtype == np.uint8:
        bit_depth = 8
        payload = np.ascontiguousarray(arr, dtype="u1").tobytes()
    else:
        raise ValueError(f"render_raw_binary requires uint8/uint16, got {arr.dtype}")

    h, w = arr.shape[:2]
    import struct
    header = b"XRAY" + struct.pack("<BBHII", bit_depth, 1, 0, int(w), int(h))
    return header + payload


def render_raw_png(arr: np.ndarray, rotate_deg: int = 0) -> bytes:
    """
    Lossless PNG of the input pixel array at its native bit depth.

        - uint16 input  → 16-bit PNG (mode "I;16")
        - uint8  input  →  8-bit PNG (mode "L")

    Nothing is clipped, normalized, or transformed. This is the default
    path for every inspector pixel response.
    """
    if arr.dtype == np.uint16:
        img = Image.fromarray(arr, "I;16")
    elif arr.dtype == np.uint8:
        img = Image.fromarray(arr, "L")
    else:
        # Downcast anything else by reinterpreting — but warn via exception
        # so upstream knows to pre-convert if it cares about precision.
        if np.issubdtype(arr.dtype, np.floating):
            raise ValueError("render_raw_png requires integer dtype; caller must discretize floats first")
        img = Image.fromarray(arr.astype(np.uint16), "I;16")

    if rotate_deg:
        img = img.rotate(rotate_deg, expand=True)

    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=False, compress_level=1)
    return buf.getvalue()


# ── Cooked 8-bit transforms (opt-in) ──────────────────────────────────

def _normalize_percentile(arr: np.ndarray, lo_pct: float, hi_pct: float) -> np.ndarray:
    a = arr.astype(np.float32)
    lo = float(np.percentile(a, lo_pct))
    hi = float(np.percentile(a, hi_pct))
    if hi <= lo:
        return np.zeros_like(a, dtype=np.uint8)
    clipped = np.clip(a, lo, hi)
    return ((clipped - lo) / (hi - lo) * 255.0).astype(np.uint8)


def _normalize_clahe(arr: np.ndarray, clip_limit: float = 2.0, tile: int = 8) -> np.ndarray:
    """CLAHE on either 8-bit or 16-bit input. Returns 8-bit."""
    clahe = cv2.createCLAHE(clipLimit=clip_limit, tileGridSize=(tile, tile))
    if arr.dtype == np.uint16:
        equalized_16 = clahe.apply(arr)
        return (equalized_16 >> 8).astype(np.uint8)
    return clahe.apply(arr.astype(np.uint8))


def _normalize_log(arr: np.ndarray) -> np.ndarray:
    a = arr.astype(np.float32)
    hi = float(a.max())
    if hi <= 0:
        return np.zeros_like(a, dtype=np.uint8)
    # log compression of attenuation: dense = bright
    a_log = np.log1p(hi - a)
    a_log = (a_log - a_log.min()) / (a_log.max() - a_log.min() + 1e-9) * 255.0
    return a_log.astype(np.uint8)


def _normalize_gamma(arr: np.ndarray, gamma: float) -> np.ndarray:
    a = arr.astype(np.float32)
    lo, hi = float(a.min()), float(a.max())
    if hi <= lo:
        return np.zeros_like(a, dtype=np.uint8)
    norm = (a - lo) / (hi - lo)
    return (np.clip(norm, 0, 1) ** gamma * 255.0).astype(np.uint8)


def _normalize_window_level(arr: np.ndarray, window_lo: float, window_hi: float) -> np.ndarray:
    if window_hi <= window_lo:
        return np.zeros_like(arr, dtype=np.uint8)
    clipped = np.clip(arr.astype(np.float32), window_lo, window_hi)
    return ((clipped - window_lo) / (window_hi - window_lo) * 255.0).astype(np.uint8)


def render_cooked_png(
    arr: np.ndarray,
    transform: str = "clahe",
    *,
    lo_pct: float = 1.0,
    hi_pct: float = 99.5,
    gamma: float = 1.0,
    window_lo: Optional[float] = None,
    window_hi: Optional[float] = None,
    invert: bool = False,
    rotate_deg: int = 0,
) -> bytes:
    """
    Render a cooked 8-bit PNG using the requested display transform.

    Supported transforms:
        - "percentile"   : linear stretch of [lo_pct, hi_pct]
        - "clahe"        : CLAHE on native bit depth
        - "log"          : logarithmic attenuation compression
        - "gamma"        : gamma curve on linear percentile stretch
        - "window"       : fixed window/level (window_lo, window_hi required)
    """
    if transform == "percentile":
        out = _normalize_percentile(arr, lo_pct, hi_pct)
    elif transform == "clahe":
        out = _normalize_clahe(arr)
    elif transform == "log":
        out = _normalize_log(arr)
    elif transform == "gamma":
        base = _normalize_percentile(arr, lo_pct, hi_pct).astype(np.float32) / 255.0
        out = (np.clip(base, 0, 1) ** gamma * 255.0).astype(np.uint8)
    elif transform == "window":
        if window_lo is None or window_hi is None:
            raise ValueError("transform='window' requires window_lo and window_hi")
        out = _normalize_window_level(arr, window_lo, window_hi)
    else:
        raise ValueError(f"unknown transform: {transform!r}")

    if invert:
        out = 255 - out

    img = Image.fromarray(out, "L")
    if rotate_deg:
        img = img.rotate(rotate_deg, expand=True)

    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=False, compress_level=1)
    return buf.getvalue()


# ── Pseudocolor maps ──────────────────────────────────────────────────

_COLORMAPS = {
    "none":    None,
    "hot":     cv2.COLORMAP_HOT,
    "bone":    cv2.COLORMAP_BONE,
    "jet":     cv2.COLORMAP_JET,
    "viridis": cv2.COLORMAP_VIRIDIS,
    "inferno": cv2.COLORMAP_INFERNO,
    "plasma":  cv2.COLORMAP_PLASMA,
}


def apply_colormap(arr8: np.ndarray, colormap: str) -> np.ndarray:
    """Apply an OpenCV colormap to an 8-bit grayscale image. Returns BGR uint8."""
    if colormap == "none" or colormap not in _COLORMAPS or _COLORMAPS[colormap] is None:
        return cv2.cvtColor(arr8, cv2.COLOR_GRAY2BGR)
    return cv2.applyColorMap(arr8, _COLORMAPS[colormap])


def encode_bgr_png(bgr: np.ndarray) -> bytes:
    """Encode a BGR uint8 image as PNG bytes (via OpenCV to avoid colorspace confusion)."""
    ok, buf = cv2.imencode(".png", bgr, [cv2.IMWRITE_PNG_COMPRESSION, 1])
    if not ok:
        raise RuntimeError("PNG encoding failed")
    return bytes(buf)
