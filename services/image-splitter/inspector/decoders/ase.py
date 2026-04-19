"""
ASE scanner image decoder — pure Python, no vendor DLL.

Format (reverse-engineered, see ase_format_research/FINDINGS.md):

    offset  size  field
    ------  ----  -----
    0       4     magic "IM\\0\\0"
    4       2     width  u16 LE
    6       2     height u16 LE
    8       2     line_data_type u16 LE  (2 = DualEnergyBitmap, 3 = ParcelDualEnergyBitmap)
    10      6     reserved / bit-depth enum / reserved
    16      ...   width*height*2 bytes of raw 16-bit grayscale little-endian pixels
    +48           opaque trailer padding (usually zeros)
    +668          UTF-16 LE XML <Metadata> trailer
"""
from __future__ import annotations

import base64
import re
import struct
from dataclasses import dataclass, field
from typing import Optional

import numpy as np

MAGIC = b"IM\x00\x00"
HEADER_LEN = 16
BYTES_PER_PIXEL = 2
XML_PROLOG_UTF16 = b"<\x00?\x00x\x00m\x00l\x00"


class AseDecodeError(Exception):
    """Raised when an ASE blob cannot be parsed."""


@dataclass
class AseImage:
    width: int
    height: int
    line_data_type: int          # 2 = single-view dual-energy, 3 = tri-panel
    pixels: np.ndarray           # (height, width) uint16
    trailer_padding: bytes
    xml_metadata: Optional[str] = None
    container_info_raw: Optional[bytes] = None

    @property
    def bit_depth(self) -> int:
        return 16

    @property
    def channel_count(self) -> int:
        """Number of tiled sub-panels (only ch=3 is tiled; ch=2 is a single view)."""
        return 3 if self.line_data_type == 3 else 1

    @property
    def is_multi_panel(self) -> bool:
        return self.line_data_type == 3 and self.width % 3 == 0

    def panel(self, index: int) -> np.ndarray:
        """
        Return one panel of a tiled multi-panel image. Panel 0 = low energy,
        panel 1 = high energy, panel 2 = material / Z-effective overlay.
        For non-tiled images, any index returns the full pixel array.
        """
        if not self.is_multi_panel:
            return self.pixels
        pw = self.width // 3
        if index < 0 or index > 2:
            raise IndexError(f"panel index {index} out of range [0..2]")
        return self.pixels[:, index * pw:(index + 1) * pw]

    def panels(self) -> list[np.ndarray]:
        if self.is_multi_panel:
            return [self.panel(i) for i in range(3)]
        return [self.pixels]

    def metadata_dict(self) -> dict:
        """Structured metadata suitable for JSON serialization."""
        info = {
            "scanner": "ase",
            "width": self.width,
            "height": self.height,
            "bit_depth": 16,
            "line_data_type": self.line_data_type,
            "layout": "tri-panel" if self.is_multi_panel else "single",
            "channel_count": self.channel_count,
            "pixel_min": int(self.pixels.min()),
            "pixel_max": int(self.pixels.max()),
            "pixel_mean": round(float(self.pixels.mean()), 2),
            "pixel_std": round(float(self.pixels.std()), 2),
        }
        if self.xml_metadata:
            info["xml_metadata_preview"] = self.xml_metadata[:2000]
        if self.container_info_raw:
            info["container_info_hex"] = self.container_info_raw.hex()
            info["container_info_length"] = len(self.container_info_raw)
        return info


def decode_ase(data: bytes) -> AseImage:
    """
    Parse an ASE blob into an AseImage. Raises AseDecodeError on malformed input.
    """
    if len(data) < HEADER_LEN:
        raise AseDecodeError(f"blob too small: {len(data)} bytes")
    if not data.startswith(MAGIC):
        raise AseDecodeError(
            f"not an ASE image (bad magic: {data[:4].hex()}, expected 494d0000)"
        )

    width, height, line_data_type = struct.unpack_from("<HHH", data, 4)
    if width == 0 or height == 0:
        raise AseDecodeError(f"invalid dimensions: {width}x{height}")

    pixels_start = HEADER_LEN
    pixels_end = pixels_start + width * height * BYTES_PER_PIXEL
    if pixels_end > len(data):
        raise AseDecodeError(
            f"pixel region exceeds blob length: need {pixels_end} bytes, have {len(data)}"
        )

    arr = np.frombuffer(data[pixels_start:pixels_end], dtype="<u2").reshape(height, width)

    xml_start = data.find(XML_PROLOG_UTF16, pixels_end)
    trailer_padding = data[pixels_end:xml_start] if xml_start >= 0 else b""
    xml_metadata: Optional[str] = None
    container_info_raw: Optional[bytes] = None
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
            xml_metadata = None

    return AseImage(
        width=width,
        height=height,
        line_data_type=line_data_type,
        pixels=arr,
        trailer_padding=trailer_padding,
        xml_metadata=xml_metadata,
        container_info_raw=container_info_raw,
    )
