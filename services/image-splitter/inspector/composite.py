"""
Dual-energy compositing and material discrimination.

Implements the vendor-style colorized X-ray view for FS6000 (low + high +
material → BGR image) and for ASE tri-panel images (ch=3 with panels 0=low,
1=high, 2=material). The material LUT maps each discrete material class to
a color in the classic customs-scanner palette:

    organic / low-Z  →  orange / brown
    inorganic / mid  →  green
    metal / high-Z   →  blue

This is an approximation — the vendor DLLs use proprietary LUTs with
per-deployment calibration. Our approximation is "good enough for visual
analysis" and can be tuned by shipping alternate LUTs as JSON.
"""
from __future__ import annotations

from typing import Optional

import cv2
import numpy as np

# Canonical FS6000 / standard customs X-ray color palette.
# 256-entry LUT indexed by material class byte. The FS6000 vendor render is
# dominated by a saturated blue cast for dense materials (metals, high-Z
# objects, steel container walls), with warmer orange/brown tints reserved
# for low-density organics. Values near 0 are transparent (the grayscale
# luminance shows through unmodified).
def build_default_material_lut() -> np.ndarray:
    """Build a (256, 3) BGR lookup table matching the FS6000 vendor palette."""
    lut = np.zeros((256, 3), dtype=np.uint8)
    # 0 = background / no classification → pass-through (luminance only)
    lut[0] = (255, 255, 255)
    # 1..40 = low-attenuation noise band → nearly neutral (slight cool tint)
    for i in range(1, 41):
        t = i / 40.0
        lut[i] = (
            int(220 + 30 * t),   # B
            int(220 + 20 * t),   # G
            int(220 + 20 * t),   # R
        )
    # 41..120 = mid-density organic → warm orange/brown (BGR: high R, mid G, low B)
    for i in range(41, 121):
        t = (i - 41) / 79.0
        lut[i] = (
            int(80 - 60 * t),    # B: decreasing
            int(140 + 30 * t),   # G: mid
            int(220 + 30 * t),   # R: rising to bright orange
        )
    # 121..255 = dense / metallic → vibrant blue (BGR: high B, mid-low G, low R)
    for i in range(121, 256):
        t = (i - 121) / 134.0
        lut[i] = (
            int(255),                         # B: maxed
            int(80 - 40 * t),                 # G: desaturate
            int(30 * (1.0 - t)),              # R: drop toward zero
        )
    return lut


DEFAULT_MATERIAL_LUT = build_default_material_lut()


def _normalize_energy_to_luminance(energy: np.ndarray, lo_pct: float = 1.0, hi_pct: float = 99.5) -> np.ndarray:
    """Map a 16-bit energy channel to 8-bit luminance with percentile clipping."""
    a = energy.astype(np.float32)
    lo = float(np.percentile(a, lo_pct))
    hi = float(np.percentile(a, hi_pct))
    if hi <= lo:
        return np.zeros_like(a, dtype=np.uint8)
    clipped = np.clip(a, lo, hi)
    return ((clipped - lo) / (hi - lo) * 255.0).astype(np.uint8)


def composite_fs6000_color(
    low: np.ndarray,
    high: np.ndarray,
    material: np.ndarray,
    lut: Optional[np.ndarray] = None,
    luminance_source: str = "high",
    material_strength: float = 0.65,
) -> np.ndarray:
    """
    Build a BGR uint8 composite from FS6000's three channels.

    Args:
        low:      (h, w) uint16 low-energy channel
        high:     (h, w) uint16 high-energy channel
        material: (h, w) uint8 material class map
        lut:      optional (256, 3) BGR LUT; uses DEFAULT_MATERIAL_LUT if None
        luminance_source: "high", "low", or "avg"
        material_strength: how much the material color tints the luminance
                           (0.0 = pure grayscale, 1.0 = pure color map)
    """
    if lut is None:
        lut = DEFAULT_MATERIAL_LUT

    if luminance_source == "high":
        luminance = _normalize_energy_to_luminance(high)
    elif luminance_source == "low":
        luminance = _normalize_energy_to_luminance(low)
    elif luminance_source == "avg":
        avg = ((high.astype(np.float32) + low.astype(np.float32)) * 0.5).astype(np.uint16)
        luminance = _normalize_energy_to_luminance(avg)
    else:
        raise ValueError(f"unknown luminance_source: {luminance_source!r}")

    # X-ray display convention: white = air (high transmission), dark = dense.
    # Our normalize-to-luminance returns bright = dense, so invert for a
    # vendor-matching look before compositing.
    luminance = 255 - luminance

    # Index the LUT by material class → per-pixel color
    material_color = lut[material]  # (h, w, 3) uint8 BGR

    lum_f = luminance.astype(np.float32) / 255.0
    color_f = material_color.astype(np.float32) / 255.0
    gray_f = np.stack([lum_f, lum_f, lum_f], axis=-1)

    # Overlay-style blend: the LUT color darkens toward black as luminance
    # darkens (dense regions), and stays near-white (background) where
    # luminance is high. This matches the vendor's look where blue metal
    # objects sit against a bright white backdrop.
    blended = gray_f * (1.0 - material_strength) + color_f * gray_f * material_strength
    # Boost overall brightness so mid-tones don't sag
    blended = np.clip(blended * 1.15, 0, 1) ** 0.9
    return np.clip(blended * 255.0, 0, 255).astype(np.uint8)


def composite_ase_tri_panel(
    ase_image,
    lut: Optional[np.ndarray] = None,
    material_strength: float = 0.5,
) -> np.ndarray:
    """
    Build a BGR composite from an ASE tri-panel image (line_data_type == 3).

    Panel 0 = low-energy, Panel 1 = high-energy, Panel 2 = material/Z overlay.
    """
    if not ase_image.is_multi_panel:
        raise ValueError("composite_ase_tri_panel requires line_data_type == 3")
    low = ase_image.panel(0)
    high = ase_image.panel(1)
    mat_16 = ase_image.panel(2)
    # ASE panel 2 is 16-bit but sparse; compress to 8-bit material class indices
    mat_8 = np.clip((mat_16.astype(np.float32) / max(mat_16.max(), 1) * 255.0), 0, 255).astype(np.uint8)
    return composite_fs6000_color(
        low=low,
        high=high,
        material=mat_8,
        lut=lut,
        luminance_source="high",
        material_strength=material_strength,
    )


def dual_energy_difference(low: np.ndarray, high: np.ndarray) -> np.ndarray:
    """
    Compute (high - low) as a signed difference mapped to 0..255. Useful for
    visualizing material-dependent attenuation ratios: dense organic materials
    attenuate both energies similarly (middle gray), while metals attenuate
    low energy much more (bright), and plastics the opposite (dark).
    """
    diff = high.astype(np.int32) - low.astype(np.int32)
    d_min, d_max = int(diff.min()), int(diff.max())
    if d_max <= d_min:
        return np.full_like(low, 128, dtype=np.uint8)
    return ((diff - d_min) / (d_max - d_min) * 255.0).astype(np.uint8)
