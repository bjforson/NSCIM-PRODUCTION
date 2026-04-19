"""
PDF report builder for the X-Ray Inspector.

Uses fpdf2 (a single small dependency) to compose a one-or-two-page report
with scan metadata, an embedded viewable render, and any ROI statistics
the analyst collected. Falls back gracefully if fpdf2 isn't installed yet.
"""
from __future__ import annotations

import io
from datetime import datetime
from typing import Any, Optional

import cv2
import numpy as np
from PIL import Image

from inspector import analysis, composite, rendering
from inspector.decoders import AseImage, Fs6000Image

try:
    from fpdf import FPDF
    _HAS_FPDF = True
except ImportError:
    _HAS_FPDF = False


_MARGIN = 15  # mm
_PAGE_W = 210  # A4 width in mm
_PAGE_H = 297  # A4 height in mm


def _downscale_for_pdf(arr: np.ndarray, max_width: int = 1600) -> np.ndarray:
    """Downscale a large image for embedding without blowing up the PDF."""
    h, w = arr.shape[:2]
    if w <= max_width:
        return arr
    scale = max_width / w
    new_size = (max_width, int(round(h * scale)))
    return cv2.resize(arr, new_size, interpolation=cv2.INTER_AREA)


def _render_preview_png(img: AseImage | Fs6000Image, channel: str) -> bytes:
    """Build an 8-bit preview PNG for embedding. Uses CLAHE for punch."""
    if isinstance(img, Fs6000Image):
        if channel == "low":
            arr = img.low
        elif channel == "material":
            arr = img.material
        else:
            arr = img.high
    else:
        if img.is_multi_panel and channel == "low":
            arr = img.panel(0)
        elif img.is_multi_panel and channel == "material":
            arr = img.panel(2)
        else:
            arr = img.pixels
    return rendering.render_cooked_png(arr, transform="clahe")


def _render_composite_png(img: AseImage | Fs6000Image) -> Optional[bytes]:
    try:
        if isinstance(img, Fs6000Image):
            bgr = composite.composite_fs6000_color(img.low, img.high, img.material)
        elif isinstance(img, AseImage) and img.is_multi_panel:
            bgr = composite.composite_ase_tri_panel(img)
        else:
            return None
        return rendering.encode_bgr_png(bgr)
    except Exception:
        return None


def build_pdf_report(
    record: Any,
    img: AseImage | Fs6000Image,
    channel: str = "main",
    rois: Optional[list[dict]] = None,
    title: Optional[str] = None,
    notes: Optional[str] = None,
    include_composite: bool = True,
) -> bytes:
    if not _HAS_FPDF:
        raise RuntimeError(
            "fpdf2 is not installed. Add 'fpdf2' to requirements.txt and pip install it."
        )

    rois = rois or []

    pdf = FPDF(unit="mm", format="A4")
    pdf.set_auto_page_break(auto=True, margin=_MARGIN)
    pdf.add_page()

    # Header
    pdf.set_font("Helvetica", "B", 16)
    pdf.cell(0, 10, title or "X-Ray Scan Report", ln=1)
    pdf.set_font("Helvetica", size=9)
    pdf.cell(0, 5, f"Generated {datetime.now().isoformat(timespec='seconds')}", ln=1)
    pdf.ln(2)

    # Metadata block
    meta = record.to_metadata_dict() if hasattr(record, "to_metadata_dict") else {}
    img_meta = img.metadata_dict()
    pdf.set_font("Helvetica", "B", 11)
    pdf.cell(0, 6, "Scan metadata", ln=1)
    pdf.set_font("Helvetica", size=9)
    for key in ("scanner", "id", "container_number", "scan_time", "inspection_id",
                "pic_number", "file_path", "image_display_name", "truck_plate"):
        if key in meta and meta[key] is not None:
            pdf.cell(0, 4, f"  {key}: {meta[key]}", ln=1)
    pdf.cell(0, 4,
             f"  dimensions: {img_meta.get('width')} x {img_meta.get('height')}",
             ln=1)
    if "bit_depth" in img_meta:
        pdf.cell(0, 4, f"  bit depth: {img_meta['bit_depth']}", ln=1)
    if "layout" in img_meta:
        pdf.cell(0, 4, f"  layout: {img_meta['layout']}", ln=1)
    pdf.ln(2)

    # Preview image
    pdf.set_font("Helvetica", "B", 11)
    pdf.cell(0, 6, f"Preview ({channel})", ln=1)
    preview_png = _render_preview_png(img, channel)
    preview_pil = Image.open(io.BytesIO(preview_png))
    # Save to a temp buffer and insert
    preview_buf = io.BytesIO()
    preview_pil.save(preview_buf, format="PNG")
    preview_buf.seek(0)
    pdf.image(preview_buf, x=_MARGIN, w=_PAGE_W - 2 * _MARGIN)
    pdf.ln(2)

    # Optional composite image (FS6000 + ASE tri-panel only)
    if include_composite:
        comp_png = _render_composite_png(img)
        if comp_png:
            pdf.ln(1)
            pdf.set_font("Helvetica", "B", 11)
            pdf.cell(0, 6, "Dual-energy composite", ln=1)
            comp_buf = io.BytesIO(comp_png)
            pdf.image(comp_buf, x=_MARGIN, w=_PAGE_W - 2 * _MARGIN)
            pdf.ln(2)

    # ROI statistics table
    if rois:
        pdf.add_page()
        pdf.set_font("Helvetica", "B", 12)
        pdf.cell(0, 8, "ROI statistics", ln=1)
        pdf.set_font("Helvetica", "B", 8)
        headers = ["#", "Kind", "Count", "Min", "Max", "Mean", "Std", "Median"]
        widths = [10, 18, 22, 18, 18, 22, 22, 22]
        for hdr, w in zip(headers, widths):
            pdf.cell(w, 6, hdr, border=1)
        pdf.ln()
        pdf.set_font("Helvetica", size=8)

        if isinstance(img, Fs6000Image):
            arr = img.high
        else:
            arr = img.pixels
        for idx, roi in enumerate(rois, start=1):
            mask = analysis.build_mask(arr.shape, roi)
            stats = analysis.roi_stats(arr, mask, bins=1)
            row = [
                str(idx),
                str(roi.get("kind", "?")),
                str(stats["count"]),
                str(stats["min"]),
                str(stats["max"]),
                str(stats["mean"]),
                str(stats["std"]),
                str(stats["median"]),
            ]
            for cell, w in zip(row, widths):
                pdf.cell(w, 5, cell, border=1)
            pdf.ln()

    # Notes
    if notes:
        pdf.ln(3)
        pdf.set_font("Helvetica", "B", 11)
        pdf.cell(0, 6, "Notes", ln=1)
        pdf.set_font("Helvetica", size=9)
        pdf.multi_cell(0, 4, notes)

    out = io.BytesIO()
    pdf.output(out)
    return out.getvalue()
