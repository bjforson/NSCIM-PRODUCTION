"""
FS6000 scanner image decoder — pure Python, no vendor DLL.

Format (reverse-engineered from Z:\\23301FS01\\2018\\0919\\0012 sample):

    offset  size  field                example (BE)
    ------  ----  -----                ------------
    0       2     ??                   0x0064 = 100
    2       2     width u16 BE         0x08c6 = 2246
    4       2     height u16 BE        0x0562 = 1378
    6       2     reserved             0x0001
    8       2     reserved             0x0001
    10      2     0xFFFF
    12      2     0x0000
    14      2     bit depth u16 BE     0x0010 = 16  (high/low)   or 0x0008 = 8 (material)
    16      2     0x0001
    18..24        zeros
    24      2     year                 2018
    26      2     month                9
    28      2     day                  19
    30      2     hour                 9
    32      2     minute
    34      2     second
    36..          pixel data           width * height * (bit_depth/8) bytes, big-endian u16

An FS6000 scan consists of THREE files in a single scan folder:

    {stem}high.img      — high-energy 16-bit BE grayscale
    {stem}low.img       — low-energy  16-bit BE grayscale
    {stem}material.img  — material classification, 8-bit (native, NOT padded)

Plus a vendor-rendered colorized JPEG ({stem}.jpg), a thumbnail
({stem}_icon.jpg), and an XML metadata file ({stem}.xml) with scanner
parameters (DIAS2RIP_XML).
"""
from __future__ import annotations

import struct
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Optional

import numpy as np

HEADER_LEN = 36


class Fs6000DecodeError(Exception):
    """Raised when an FS6000 .img file cannot be parsed."""


@dataclass
class Fs6000Paths:
    """File paths for a single FS6000 scan."""
    folder: Path
    stem: str               # e.g. "23301FS01201809190012"
    high: Path
    low: Path
    material: Path
    xml: Optional[Path] = None
    jpeg: Optional[Path] = None
    icon: Optional[Path] = None

    @classmethod
    def from_folder(cls, folder: Path | str) -> "Fs6000Paths":
        """
        Build an Fs6000Paths from a scan folder. The folder must contain
        at least one *high.img file — the stem is inferred from its name.
        """
        folder = Path(folder)
        if not folder.is_dir():
            raise Fs6000DecodeError(f"not a directory: {folder}")
        highs = list(folder.glob("*high.img"))
        if not highs:
            raise Fs6000DecodeError(f"no *high.img found in {folder}")
        high = highs[0]
        stem = high.name[: -len("high.img")]
        low = folder / f"{stem}low.img"
        material = folder / f"{stem}material.img"
        if not low.exists():
            raise Fs6000DecodeError(f"missing {low.name}")
        if not material.exists():
            raise Fs6000DecodeError(f"missing {material.name}")
        xml_path = folder / f"{stem}.xml"
        jpeg_path = folder / f"{stem}.jpg"
        icon_path = folder / f"{stem}_icon.jpg"
        return cls(
            folder=folder,
            stem=stem,
            high=high,
            low=low,
            material=material,
            xml=xml_path if xml_path.exists() else None,
            jpeg=jpeg_path if jpeg_path.exists() else None,
            icon=icon_path if icon_path.exists() else None,
        )


@dataclass
class Fs6000Header:
    width: int
    height: int
    bit_depth: int
    timestamp: Optional[datetime]

    @classmethod
    def parse(cls, data: bytes) -> "Fs6000Header":
        if len(data) < HEADER_LEN:
            raise Fs6000DecodeError(f"header too short: {len(data)} bytes")
        width = struct.unpack_from(">H", data, 2)[0]
        height = struct.unpack_from(">H", data, 4)[0]
        bit_depth = struct.unpack_from(">H", data, 14)[0]
        year = struct.unpack_from(">H", data, 24)[0]
        month = struct.unpack_from(">H", data, 26)[0]
        day = struct.unpack_from(">H", data, 28)[0]
        hour = struct.unpack_from(">H", data, 30)[0]
        minute = struct.unpack_from(">H", data, 32)[0]
        second = struct.unpack_from(">H", data, 34)[0]
        if bit_depth not in (8, 16):
            raise Fs6000DecodeError(f"unexpected bit depth: {bit_depth}")
        if width == 0 or height == 0:
            raise Fs6000DecodeError(f"invalid dimensions: {width}x{height}")

        timestamp: Optional[datetime] = None
        try:
            if 2000 <= year <= 2100:
                timestamp = datetime(year, month, day, hour, minute, second)
        except ValueError:
            timestamp = None

        return cls(width=width, height=height, bit_depth=bit_depth, timestamp=timestamp)


@dataclass
class Fs6000Image:
    width: int
    height: int
    high: np.ndarray          # (h, w) uint16, big-endian-interpreted, stored native-endian
    low: np.ndarray           # (h, w) uint16
    material: np.ndarray      # (h, w) uint8
    timestamp: Optional[datetime]
    xml_metadata: Optional[str] = None
    paths: Optional[Fs6000Paths] = None

    @property
    def bit_depth_high_low(self) -> int:
        return 16

    @property
    def bit_depth_material(self) -> int:
        return 8

    def metadata_dict(self) -> dict:
        info: dict = {
            "scanner": "fs6000",
            "width": self.width,
            "height": self.height,
            "bit_depth_energies": 16,
            "bit_depth_material": 8,
            "channel_count": 3,
            "channels": ["high", "low", "material"],
            "high_stats": {
                "min": int(self.high.min()),
                "max": int(self.high.max()),
                "mean": round(float(self.high.mean()), 2),
                "std": round(float(self.high.std()), 2),
            },
            "low_stats": {
                "min": int(self.low.min()),
                "max": int(self.low.max()),
                "mean": round(float(self.low.mean()), 2),
                "std": round(float(self.low.std()), 2),
            },
            "material_stats": {
                "min": int(self.material.min()),
                "max": int(self.material.max()),
                "distinct_values": int(np.unique(self.material).size),
            },
            "timestamp": self.timestamp.isoformat() if self.timestamp else None,
        }
        if self.xml_metadata:
            info["xml_metadata_preview"] = self.xml_metadata[:4000]
        return info


def _decode_channel_16bit(data: bytes, width: int, height: int) -> np.ndarray:
    expected = HEADER_LEN + width * height * 2
    if len(data) < expected:
        raise Fs6000DecodeError(
            f"16-bit channel too small: {len(data)} bytes, need {expected}"
        )
    # Big-endian interpretation is the correct one per the reverse-engineered
    # format (header fields are also BE).
    return np.frombuffer(data[HEADER_LEN:expected], dtype=">u2").reshape(height, width)


def _decode_channel_8bit(data: bytes, width: int, height: int) -> np.ndarray:
    expected = HEADER_LEN + width * height
    if len(data) < expected:
        raise Fs6000DecodeError(
            f"8-bit channel too small: {len(data)} bytes, need {expected}"
        )
    return np.frombuffer(data[HEADER_LEN:expected], dtype="u1").reshape(height, width)


def decode_fs6000(paths: Fs6000Paths) -> Fs6000Image:
    """
    Read high.img, low.img, material.img (and optionally the XML sidecar)
    from an Fs6000Paths bundle and return an Fs6000Image.
    """
    high_bytes = paths.high.read_bytes()
    low_bytes = paths.low.read_bytes()
    mat_bytes = paths.material.read_bytes()

    high_header = Fs6000Header.parse(high_bytes)
    low_header = Fs6000Header.parse(low_bytes)
    mat_header = Fs6000Header.parse(mat_bytes)

    # Dimensions must agree across all three channels.
    if (high_header.width, high_header.height) != (low_header.width, low_header.height):
        raise Fs6000DecodeError(
            f"high/low dimension mismatch: {high_header.width}x{high_header.height} "
            f"vs {low_header.width}x{low_header.height}"
        )
    if (high_header.width, high_header.height) != (mat_header.width, mat_header.height):
        raise Fs6000DecodeError(
            f"high/material dimension mismatch: {high_header.width}x{high_header.height} "
            f"vs {mat_header.width}x{mat_header.height}"
        )
    if high_header.bit_depth != 16 or low_header.bit_depth != 16:
        raise Fs6000DecodeError(
            f"expected 16-bit for high/low, got {high_header.bit_depth}/{low_header.bit_depth}"
        )
    if mat_header.bit_depth != 8:
        raise Fs6000DecodeError(
            f"expected 8-bit for material, got {mat_header.bit_depth}"
        )

    w, h = high_header.width, high_header.height
    high = _decode_channel_16bit(high_bytes, w, h)
    low = _decode_channel_16bit(low_bytes, w, h)
    material = _decode_channel_8bit(mat_bytes, w, h)

    xml_metadata: Optional[str] = None
    if paths.xml is not None and paths.xml.exists():
        try:
            raw = paths.xml.read_bytes()
            # XML is declared UTF-16 in the prolog
            if raw.startswith(b"\xff\xfe") or raw.startswith(b"\xfe\xff"):
                xml_metadata = raw.decode("utf-16", errors="replace")
            else:
                try:
                    xml_metadata = raw.decode("utf-16-le", errors="replace")
                except Exception:
                    xml_metadata = raw.decode("utf-8", errors="replace")
        except Exception:
            xml_metadata = None

    # The FS6000 scanner delivers pixel data with row 0 at the top of the
    # container but the vendor's display convention is bottom-at-top (truck
    # driving rightward with wheels along the lower edge). Flip the Y axis
    # so our default output matches vendor viewers without requiring every
    # caller to remember to flip.
    return Fs6000Image(
        width=w,
        height=h,
        high=np.ascontiguousarray(high[::-1, :]).astype(np.uint16, copy=False),
        low=np.ascontiguousarray(low[::-1, :]).astype(np.uint16, copy=False),
        material=np.ascontiguousarray(material[::-1, :]),
        timestamp=high_header.timestamp,
        xml_metadata=xml_metadata,
        paths=paths,
    )


def decode_fs6000_from_bytes(
    high_bytes: bytes,
    low_bytes: bytes,
    mat_bytes: bytes,
) -> Fs6000Image:
    """
    Decode an FS6000 scan from raw channel bytes (read from the database)
    rather than file paths. Uses the same header parsing and numpy decoding
    as ``decode_fs6000``.
    """
    high_header = Fs6000Header.parse(high_bytes)
    low_header = Fs6000Header.parse(low_bytes)
    mat_header = Fs6000Header.parse(mat_bytes)

    if (high_header.width, high_header.height) != (low_header.width, low_header.height):
        raise Fs6000DecodeError(
            f"high/low dimension mismatch: {high_header.width}x{high_header.height} "
            f"vs {low_header.width}x{low_header.height}"
        )
    if (high_header.width, high_header.height) != (mat_header.width, mat_header.height):
        raise Fs6000DecodeError(
            f"high/material dimension mismatch: {high_header.width}x{high_header.height} "
            f"vs {mat_header.width}x{mat_header.height}"
        )
    if high_header.bit_depth != 16 or low_header.bit_depth != 16:
        raise Fs6000DecodeError(
            f"expected 16-bit for high/low, got {high_header.bit_depth}/{low_header.bit_depth}"
        )
    if mat_header.bit_depth != 8:
        raise Fs6000DecodeError(
            f"expected 8-bit for material, got {mat_header.bit_depth}"
        )

    w, h = high_header.width, high_header.height
    high = _decode_channel_16bit(high_bytes, w, h)
    low = _decode_channel_16bit(low_bytes, w, h)
    material = _decode_channel_8bit(mat_bytes, w, h)

    return Fs6000Image(
        width=w,
        height=h,
        high=np.ascontiguousarray(high[::-1, :]).astype(np.uint16, copy=False),
        low=np.ascontiguousarray(low[::-1, :]).astype(np.uint16, copy=False),
        material=np.ascontiguousarray(material[::-1, :]),
        timestamp=high_header.timestamp,
        xml_metadata=None,
        paths=None,
    )
