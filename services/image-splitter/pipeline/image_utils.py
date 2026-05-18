"""Image loading, cropping, and format conversion utilities."""

import io
import base64
from typing import Tuple, Optional
import numpy as np
import cv2
from PIL import Image


def decode_image(image_data: bytes) -> np.ndarray:
    """Decode image bytes (JPEG/PNG) to numpy array.

    Supports both 8-bit and 16-bit input. If the source is a 16-bit PNG
    (from the new ASE processing pipeline), the image is read at full
    depth and converted to 8-bit grayscale for the strategies that expect
    uint8 input. The 16-bit source preserves more dynamic range during
    the decode, which gives crisper corner-casting contrast even after
    the downconversion to 8-bit.
    """
    nparr = np.frombuffer(image_data, np.uint8)
    # Try reading at full depth first (IMREAD_UNCHANGED preserves 16-bit)
    img = cv2.imdecode(nparr, cv2.IMREAD_UNCHANGED)
    if img is None:
        raise ValueError("Failed to decode image data")

    # If 16-bit grayscale, normalize to 8-bit for downstream strategies
    if img.dtype == np.uint16:
        # Percentile stretch for better contrast than a simple /256 divide
        lo = np.percentile(img, 0.5)
        hi = np.percentile(img, 99.5)
        if hi > lo:
            img = np.clip((img.astype(np.float32) - lo) * 255.0 / (hi - lo), 0, 255).astype(np.uint8)
        else:
            img = (img >> 8).astype(np.uint8)
        # Convert grayscale to 3-channel BGR so downstream code that
        # expects color images doesn't break
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)
    elif img.ndim == 2:
        # 8-bit grayscale → 3-channel BGR
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)

    return img


def encode_image(image_array: np.ndarray, format: str = "jpeg", quality: int = 90) -> bytes:
    """Encode numpy array to image bytes."""
    if format.lower() in ("jpg", "jpeg"):
        _, buf = cv2.imencode('.jpg', image_array, [cv2.IMWRITE_JPEG_QUALITY, quality])
    elif format.lower() == "png":
        _, buf = cv2.imencode('.png', image_array)
    else:
        raise ValueError(f"Unsupported format: {format}")
    return buf.tobytes()


def is_16bit_grayscale_image(image_data: bytes) -> bool:
    """Return True when the encoded source is a 16-bit grayscale image."""
    try:
        with Image.open(io.BytesIO(image_data)) as img:
            return img.mode in ("I;16", "I;16B", "I;16L", "I")
    except Exception:
        return False


def invert_encoded_image(image_data: bytes, format: str = "jpeg", quality: int = 90) -> bytes:
    """Invert an encoded crop and re-encode it in the requested format."""
    img = decode_image(image_data)
    return encode_image(255 - img, format=format, quality=quality)


def crop_image(image_array: np.ndarray, split_x: int) -> Tuple[np.ndarray, np.ndarray]:
    """
    Split an image vertically at split_x.
    Returns (left_half, right_half) as numpy arrays.
    """
    h, w = image_array.shape[:2]
    split_x = max(1, min(split_x, w - 1))

    left = image_array[:, :split_x].copy()
    right = image_array[:, split_x:].copy()

    return left, right


def crop_and_encode(
    image_data: bytes,
    split_x: int,
    quality: int = 90,
    format: str = "jpeg"
) -> Tuple[bytes, bytes]:
    """Decode, split, and re-encode an image. Returns (left_bytes, right_bytes)."""
    img = decode_image(image_data)
    left, right = crop_image(img, split_x)
    return encode_image(left, format=format, quality=quality), encode_image(right, format=format, quality=quality)


def crop_side_and_encode(
    image_data: bytes,
    split_x: int,
    side: str,
    quality: int = 90,
    format: str = "png"
) -> bytes:
    """Decode, split, and encode a single crop side from the original image bytes."""
    if side not in ("left", "right"):
        raise ValueError("Side must be 'left' or 'right'")

    img = decode_image(image_data)
    left, right = crop_image(img, split_x)
    crop = left if side == "left" else right
    return encode_image(crop, format=format, quality=quality)


def detect_image_media_type(image_data: bytes) -> str:
    """Best-effort media type detection for stored image bytes."""
    if image_data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if image_data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    if image_data.startswith(b"RIFF") and image_data[8:12] == b"WEBP":
        return "image/webp"
    return "image/jpeg"


def get_image_dimensions(image_data: bytes) -> Tuple[int, int]:
    """Get (width, height) of image from bytes without full decoding."""
    img = Image.open(io.BytesIO(image_data))
    return img.size  # (width, height)


def base64_to_bytes(b64_string: str) -> bytes:
    """Decode base64 string to bytes, handling data URI prefix."""
    if ',' in b64_string:
        b64_string = b64_string.split(',', 1)[1]
    return base64.b64decode(b64_string)


def bytes_to_base64(data: bytes, mime_type: str = "image/jpeg") -> str:
    """Encode bytes to base64 data URI string."""
    b64 = base64.b64encode(data).decode('utf-8')
    return f"data:{mime_type};base64,{b64}"


def generate_debug_image(
    image_array: np.ndarray,
    split_x: int,
    strategy_name: str,
    confidence: float,
    metadata: Optional[dict] = None
) -> bytes:
    """
    Generate a debug visualization showing the split line and metadata
    overlaid on the original image.
    """
    vis = image_array.copy()
    h, w = vis.shape[:2]

    # Draw split line (red)
    cv2.line(vis, (split_x, 0), (split_x, h), (0, 0, 255), 3)

    # Draw label
    label = f"{strategy_name}: x={split_x} ({confidence:.0%})"
    cv2.putText(vis, label, (split_x + 10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)

    # Draw peak positions if available
    if metadata and "peak_positions" in metadata:
        for px in metadata["peak_positions"]:
            cv2.line(vis, (px, 0), (px, h), (0, 255, 0), 1)

    return encode_image(vis, quality=85)
