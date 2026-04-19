"""
05_prototype_decoder.py

Pure-Python decoder for the ASE (.ase) scanner image format. Does NOT use the
vendor Ase.Image.dll. Produces an 8-bit grayscale PNG from the raw 16-bit
pixel data stored in the blob.

Format (reverse-engineered from 10/10 samples — see FINDINGS.md):

  offset  size  field
  ------  ----  -----
  0       4     signature "IM\\0\\0"  (corresponds to ImgFileHeader.m_signature)
  4       2     width  u16 LE         (ImgFileHeader.m_imageWidth)
  6       2     height u16 LE         (ImgFileHeader.m_imageHeight)
  8       2     lineDataType u16 LE   (2 = DualEnergyBitmap, 3 = ParcelDualEnergyBitmap)
  10      2     (unknown / reserved)
  12      2     (unknown — always 0x0002 in observed samples; likely bitDepth enum
                 or m_fileType; confirmed bytes-per-pixel=2 empirically)
  14      2     (unknown / reserved)
  16..(16 + w*h*2)   raw 16-bit grayscale pixel data, little-endian
  next 48 bytes      unknown trailer block (usually zeros; treat as padding)
  final 668 bytes    UTF-16 LE XML metadata <Metadata>...</Metadata>

For ch=3 (ParcelDualEnergyBitmap), the image is three panels tiled horizontally,
each (width/3) pixels wide. Panel 0 and Panel 1 are the low/high energy views;
Panel 2 is a sparse material-discrimination overlay.

Display rendering: X-ray scanners display attenuation as darkness (more dense =
darker). We normalize and invert the raw 16-bit values for a human-viewable PNG.
This matches the typical vendor DLL output visually, though exact gamma and
dual-energy color mapping are not yet replicated.
"""
from __future__ import annotations

import argparse
import base64
import re
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

try:
    import cv2  # needed for CLAHE; already in image-splitter requirements
    _HAS_CV2 = True
except ImportError:
    _HAS_CV2 = False

HERE = Path(__file__).resolve().parent
SAMPLES_DIR = HERE / "samples"

MAGIC = b"IM\x00\x00"
XML_PROLOG = b"<\x00?\x00x\x00m\x00l\x00"
HEADER_LEN = 16
BYTES_PER_PIXEL = 2


@dataclass
class AseImage:
    width: int
    height: int
    line_data_type: int
    pixels: np.ndarray             # (height, width), uint16
    trailer_padding: bytes         # the ~48-byte gap between pixels and XML
    xml_metadata: Optional[str]    # UTF-16 LE decoded XML, or None
    container_info_raw: Optional[bytes]

    @property
    def channels(self) -> int:
        return self.line_data_type

    def panels(self) -> list[np.ndarray]:
        """Split the image into per-channel panels.

        Only `ParcelDualEnergyBitmap` (line_data_type=3) actually tiles multiple
        sub-images horizontally — each panel is width/3 pixels wide. For
        line_data_type=2 (`DualEnergyBitmap`), the entire width is a single
        view (the dual-energy encoding is not a horizontal tile).
        """
        if self.line_data_type == 3 and self.width % 3 == 0:
            pw = self.width // 3
            return [self.pixels[:, i * pw:(i + 1) * pw] for i in range(3)]
        return [self.pixels]


def decode(data: bytes) -> AseImage:
    if not data.startswith(MAGIC):
        raise ValueError(f"not an ASE image (bad magic: {data[:4].hex()})")

    width, height, line_data_type = struct.unpack_from("<HHH", data, 4)
    if width == 0 or height == 0:
        raise ValueError(f"invalid dims: {width}x{height}")

    pixels_start = HEADER_LEN
    pixels_end = pixels_start + width * height * BYTES_PER_PIXEL
    if pixels_end > len(data):
        raise ValueError(
            f"pixel region exceeds blob length: need {pixels_end} have {len(data)}"
        )

    arr = np.frombuffer(data[pixels_start:pixels_end], dtype="<u2").reshape(height, width)

    xml_start = data.find(XML_PROLOG, pixels_end)
    trailer_padding = data[pixels_end:xml_start] if xml_start >= 0 else b""
    xml_metadata = None
    container_info_raw = None
    if xml_start >= 0:
        try:
            xml_metadata = data[xml_start:].decode("utf-16-le", errors="replace")
            m = re.search(
                r"<Value>([A-Za-z0-9+/=]+)</Value>\s*<Name>ContainerInfo</Name>",
                xml_metadata,
            )
            if m:
                container_info_raw = base64.b64decode(m.group(1))
        except Exception:
            pass

    return AseImage(
        width=width,
        height=height,
        line_data_type=line_data_type,
        pixels=arr,
        trailer_padding=trailer_padding,
        xml_metadata=xml_metadata,
        container_info_raw=container_info_raw,
    )


def _normalize_percentile(arr: np.ndarray, lo_pct: float, hi_pct: float) -> np.ndarray:
    """Clip to [lo_pct, hi_pct] percentiles, then linearly map to 0..255.

    Simple and fast, but crushes the densest 1% of pixels (engine blocks,
    wheel hubs, chassis steel) to pure black and loses real structural detail.
    Use `clahe` or `raw16` for detail-preserving output.
    """
    a = arr.astype(np.float32)
    lo = float(np.percentile(a, lo_pct))
    hi = float(np.percentile(a, hi_pct))
    if hi <= lo:
        return np.zeros_like(a, dtype=np.uint8)
    clipped = np.clip(a, lo, hi)
    return ((clipped - lo) / (hi - lo) * 255.0).astype(np.uint8)


def _normalize_clahe(arr: np.ndarray, clip_limit: float = 2.0, tile: int = 8) -> np.ndarray:
    """Contrast-Limited Adaptive Histogram Equalization on the 16-bit array.

    Runs directly on the uint16 pixel data (OpenCV CLAHE is 16-bit capable)
    and returns an 8-bit normalized result. Reveals detail in both very dark
    (dense) and very bright (sparse) regions simultaneously. This is the
    default for the prototype because it preserves more security-relevant
    structure than naive percentile clipping.
    """
    if not _HAS_CV2:
        # Graceful fallback: percentile clip
        return _normalize_percentile(arr, 1.0, 99.5)
    clahe = cv2.createCLAHE(clipLimit=clip_limit, tileGridSize=(tile, tile))
    equalized_16 = clahe.apply(arr.astype(np.uint16))
    return (equalized_16 >> 8).astype(np.uint8)


def _apply_common(
    arr8: np.ndarray, invert: bool, rotate_to_dll: bool
) -> Image.Image:
    if invert:
        arr8 = 255 - arr8
    pil = Image.fromarray(arr8, "L")
    if rotate_to_dll:
        pil = pil.rotate(90, expand=True)
    return pil


def render_png(
    img: AseImage,
    transform: str = "clahe",
    invert: bool = False,
    rotate_to_dll: bool = True,
    lo_pct: float = 1.0,
    hi_pct: float = 99.5,
) -> Image.Image:
    """Render the raw 16-bit pixels as an 8-bit PNG for human viewing.

    transform:
      "clahe"       - CLAHE on 16-bit data (default, detail-preserving)
      "percentile"  - linear stretch of [lo_pct, hi_pct] range (faster, matches
                      vendor DLL's appearance more closely)
    """
    if transform == "clahe":
        arr8 = _normalize_clahe(img.pixels)
    elif transform == "percentile":
        arr8 = _normalize_percentile(img.pixels, lo_pct, hi_pct)
    else:
        raise ValueError(f"unknown transform: {transform!r}")
    return _apply_common(arr8, invert, rotate_to_dll)


def render_png_raw16(img: AseImage, rotate_to_dll: bool = True) -> Image.Image:
    """Emit a LOSSLESS 16-bit PNG of the raw pixel array.

    No clipping, no normalization, no precision loss. All 62k+ distinct
    pixel values are preserved. Image viewers that only understand 8-bit
    will auto-convert for display; analysis pipelines can read the full
    uint16 values back via `np.asarray(Image.open(...), dtype=np.uint16)`.
    """
    pil = Image.fromarray(img.pixels.astype(np.uint16), "I;16")
    if rotate_to_dll:
        pil = pil.rotate(90, expand=True)
    return pil


def render_per_channel(
    img: AseImage,
    transform: str = "clahe",
    invert: bool = False,
    rotate_to_dll: bool = True,
    lo_pct: float = 1.0,
    hi_pct: float = 99.5,
) -> list[Image.Image]:
    """For multi-panel images (ch=3), render each panel separately."""
    out = []
    for panel in img.panels():
        if transform == "clahe":
            arr8 = _normalize_clahe(panel)
        elif transform == "percentile":
            arr8 = _normalize_percentile(panel, lo_pct, hi_pct)
        else:
            raise ValueError(f"unknown transform: {transform!r}")
        out.append(_apply_common(arr8, invert, rotate_to_dll))
    return out


def main():
    ap = argparse.ArgumentParser(description="Decode ASE scanner images without the vendor DLL")
    ap.add_argument(
        "paths",
        nargs="*",
        help="Specific .ase files (default: all files in ./samples/)",
    )
    ap.add_argument(
        "--out",
        default=str(SAMPLES_DIR),
        help="Output directory for PNGs (default: samples/)",
    )
    ap.add_argument(
        "--eight-bit",
        action="store_true",
        help="Also emit a cooked 8-bit PNG (opt-in). Raw 16-bit is always written.",
    )
    ap.add_argument(
        "--transform",
        choices=["clahe", "percentile"],
        default="clahe",
        help="Display transform for the 8-bit PNG when --eight-bit is set (default: clahe)",
    )
    ap.add_argument(
        "--invert",
        action="store_true",
        help="Invert pixel values (dense = dark, opposite of vendor DLL convention)",
    )
    ap.add_argument(
        "--split-panels",
        action="store_true",
        help="For ch>=2, also emit per-panel PNGs",
    )
    args = ap.parse_args()

    paths = [Path(p) for p in args.paths] if args.paths else sorted(SAMPLES_DIR.glob("*.ase"))
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"Decoding {len(paths)} file(s) -> {out_dir}")
    print()
    print(f"{'file':<42} {'w':>6} {'h':>6} {'ch':>3} {'status':<20}")
    print("-" * 90)

    ok = 0
    for p in paths:
        try:
            data = p.read_bytes()
            img = decode(data)

            # Primary output: lossless 16-bit PNG (all 62k+ distinct values preserved)
            raw16 = render_png_raw16(img)
            out_16bit = out_dir / f"{p.stem}.raw16.png"
            raw16.save(out_16bit)
            note = f"raw16 {out_16bit.stat().st_size // 1024} KB"

            # Opt-in: cooked 8-bit PNG for quick human viewing
            if args.eight_bit:
                png = render_png(img, transform=args.transform, invert=args.invert)
                out_8bit = out_dir / f"{p.stem}.decoded.png"
                png.save(out_8bit)
                note += f" + 8bit-{args.transform} {out_8bit.stat().st_size // 1024} KB"

            status = f"ok ({note})"
            ok += 1

            if args.split_panels and img.line_data_type == 3:
                # split-panel output is always raw16 (no cooking)
                panels_16 = [
                    Image.fromarray(panel.astype(np.uint16), "I;16").rotate(90, expand=True)
                    for panel in img.panels()
                ]
                for i, panel_img in enumerate(panels_16):
                    panel_img.save(out_dir / f"{p.stem}.panel{i}.raw16.png")
        except Exception as e:
            status = f"FAIL: {e}"
            img = None

        w = img.width if img else 0
        h = img.height if img else 0
        ch = img.channels if img else 0
        print(f"{p.name:<42} {w:>6} {h:>6} {ch:>3} {status}")

    print()
    print(f"Decoded {ok}/{len(paths)} files successfully.")


if __name__ == "__main__":
    main()
