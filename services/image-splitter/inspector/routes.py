"""
FastAPI router mounted at /inspector in the image-splitter service.

Endpoints:

    GET  /inspector/health                       liveness probe
    GET  /inspector/search                       container-number search across ASE + FS6000
    GET  /inspector/meta/{scanner}/{id}          scan metadata + decode stats
    GET  /inspector/pixels/{scanner}/{id}        pixel PNG (default: raw16)
    GET  /inspector/composite/{scanner}/{id}     colorized dual-energy composite (BGR PNG)
    POST /inspector/roi-stats                    ROI histogram + statistics
    POST /inspector/line-profile                 pixel values along a line segment
    POST /inspector/edge                         Canny/Sobel edge overlay
    POST /inspector/threshold                    binary threshold mask PNG
    POST /inspector/objects                      connected-component object detection
    POST /inspector/dual-energy-diff             FS6000/ASE dual-energy difference image
    GET  /inspector/vendor-jpeg/{scanner}/{id}   original vendor-rendered JPEG (pass-through)
    POST /inspector/export/roi-csv               CSV of ROI stats
    POST /inspector/export/pdf-report            PDF report with annotations and stats
"""
from __future__ import annotations

import io
import logging
from typing import Any, Literal, Optional

import cv2
import numpy as np
from fastapi import APIRouter, HTTPException, Query, Response
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel, Field

from inspector import analysis, composite, rendering, report
from inspector.cache import cache, cache_stats
from inspector.data_access import (
    Fs6000ScanRecord,
    AseScanRecord,
    load_ase_scan,
    load_fs6000_scan,
    load_fs6000_img_blobs,
    resolve_fs6000_folder,
    search_scans,
)
from inspector.decoders import (
    Fs6000Image,
    Fs6000Paths,
    AseImage,
    decode_ase,
    decode_fs6000,
    decode_fs6000_from_bytes,
)

logger = logging.getLogger("inspector")

router = APIRouter(prefix="/inspector", tags=["inspector"])


# ── Helpers to load and decode on demand ─────────────────────────────

def _load_ase(scan_id: str) -> tuple[AseScanRecord, AseImage]:
    """
    Cached load of an ASE scan + decoded image.
    Cache key: ("ase", scan_id). The loader fetches the bytea from Postgres
    (1-2 MB) and decodes it; both steps are bypassed on cache hits.
    """
    key = ("ase", scan_id)

    def _loader() -> tuple[AseScanRecord, AseImage]:
        try:
            record = load_ase_scan(scan_id)
        except KeyError as e:
            raise HTTPException(status_code=404, detail=str(e))
        try:
            img = decode_ase(record.blob)
        except Exception as e:
            raise HTTPException(status_code=422, detail=f"ASE decode failed: {e}")
        return record, img

    return cache.get_or_load(key, _loader)


def _load_fs6000(scan_id: str) -> tuple[Fs6000ScanRecord, Fs6000Image]:
    """
    Cached load of an FS6000 scan + decoded image.

    DB-first: if raw .img channel blobs are stored in fs6000images
    (HighEnergy/LowEnergy/Material), decode from DB — no filesystem needed.
    Falls back to disk (network share / staging) for pre-backfill scans.
    """
    key = ("fs6000", scan_id)

    def _loader() -> tuple[Fs6000ScanRecord, Fs6000Image]:
        try:
            record = load_fs6000_scan(scan_id)
        except KeyError as e:
            raise HTTPException(status_code=404, detail=str(e))

        # DB-first path: use stored .img blobs when available
        if record.has_img_blob:
            try:
                blobs = load_fs6000_img_blobs(scan_id)
                img = decode_fs6000_from_bytes(
                    high_bytes=blobs["high"],
                    low_bytes=blobs["low"],
                    mat_bytes=blobs["material"],
                )
                logger.debug("FS6000 scan %s loaded from DB blobs", scan_id)
                return record, img
            except Exception as e:
                logger.warning(
                    "DB blob load failed for FS6000 scan %s, falling back to disk: %s",
                    scan_id, e,
                )

        # Disk fallback path (for pre-backfill scans or DB load failure)
        try:
            folder = resolve_fs6000_folder(record)
        except FileNotFoundError as e:
            raise HTTPException(status_code=404, detail=str(e))
        try:
            paths = Fs6000Paths.from_folder(folder)
            img = decode_fs6000(paths)
        except Exception as e:
            raise HTTPException(status_code=422, detail=f"FS6000 decode failed: {e}")
        return record, img

    return cache.get_or_load(key, _loader)


def _pick_channel(img: AseImage | Fs6000Image, channel: str) -> np.ndarray:
    """
    Return the requested channel as a numpy array.

    For ASE single-view scans ("ase" + line_data_type=2): "main" → full image.
    For ASE tri-panel ("ase" + line_data_type=3): "low" / "high" / "material"
    → panels 0/1/2, "main" → full tiled image.
    For FS6000: "high" / "low" / "material" → the three channels directly.
    """
    if isinstance(img, Fs6000Image):
        if channel in ("high", "main", ""):
            return img.high
        if channel == "low":
            return img.low
        if channel == "material":
            return img.material
        raise HTTPException(400, f"FS6000 has no channel '{channel}'")
    # ASE
    assert isinstance(img, AseImage)
    if channel in ("main", "", None):
        return img.pixels
    if img.is_multi_panel:
        if channel == "low":
            return img.panel(0)
        if channel == "high":
            return img.panel(1)
        if channel == "material":
            return img.panel(2)
    raise HTTPException(400, f"ASE channel '{channel}' not available for this scan")


def _load_any(scanner: str, scan_id: str):
    scanner = scanner.lower()
    if scanner == "ase":
        record, img = _load_ase(scan_id)
        return record, img
    if scanner == "fs6000":
        record, img = _load_fs6000(scan_id)
        return record, img
    raise HTTPException(400, f"unknown scanner type: {scanner!r}")


# ── Health ───────────────────────────────────────────────────────────

@router.get("/health")
def health():
    import os
    import psutil
    import time

    _start = getattr(health, "_start_time", None)
    if _start is None:
        health._start_time = time.time()
        _start = health._start_time

    uptime = int(time.time() - _start)

    # Cache metrics
    stats = cache_stats()

    # DB connectivity check
    db_ok = True
    try:
        from models.database import sync_engine
        with sync_engine.connect() as conn:
            conn.execute(__import__("sqlalchemy").text("SELECT 1"))
    except Exception:
        db_ok = False

    # FS6000 share check
    fs6000_share = os.environ.get("NICKSCAN_FS6000_SHARE", "Z:\\23301FS01")
    fs6000_ok = os.path.isdir(fs6000_share) if fs6000_share else False

    # Memory
    process = psutil.Process()
    mem_mb = int(process.memory_info().rss / 1024 / 1024)

    # Status determination
    status = "healthy"
    if not db_ok:
        status = "unhealthy"
    elif mem_mb > 800 or (stats.get("hit_ratio", 1.0) < 0.3 and stats.get("total_requests", 0) > 100):
        status = "degraded"

    return {
        "status": status,
        "service": "NSCIM Raw Image Engine",
        "version": "2.7.0",
        "uptime_seconds": uptime,
        "db_connected": db_ok,
        "cache": {
            "entries": stats.get("size", 0),
            "bytes_used": stats.get("bytes_used", 0),
            "hit_rate": round(stats.get("hit_ratio", 0.0), 3),
        },
        "fs6000_share_accessible": fs6000_ok,
        "memory_mb": mem_mb,
    }


@router.get("/cache/stats")
def get_cache_stats():
    """Decoded-image cache telemetry. See inspector/cache.py for details."""
    return cache_stats()


@router.post("/cache/clear")
def clear_cache():
    """Nuke the decoded-image cache. Useful for freeing memory after a
    bulk-viewing session or when testing cold-cache latency."""
    cache.clear()
    return {"status": "cleared"}


# ── Search ───────────────────────────────────────────────────────────

@router.get("/search")
def search(
    q: str = Query(..., min_length=1, description="Container number fragment"),
    scanner: Literal["ase", "fs6000", "both"] = "both",
    limit: int = Query(50, ge=1, le=500),
):
    try:
        results = search_scans(q, scanner=scanner, limit=limit)
    except Exception as e:
        logger.exception("search failed")
        raise HTTPException(500, f"search failed: {e}")
    return {"query": q, "scanner": scanner, "count": len(results),
            "results": [r.to_dict() for r in results]}


# ── Metadata ─────────────────────────────────────────────────────────

@router.get("/meta/{scanner}/{scan_id}")
def get_metadata(scanner: str, scan_id: str):
    record, img = _load_any(scanner, scan_id)
    return {
        "record": record.to_metadata_dict(),
        "image": img.metadata_dict(),
    }


# ── Pixel rendering ──────────────────────────────────────────────────

@router.get("/pixels/{scanner}/{scan_id}")
def get_pixels(
    scanner: str,
    scan_id: str,
    channel: str = Query("main"),
    transform: Literal["raw", "clahe", "percentile", "log", "gamma", "window"] = "raw",
    format: Literal["png", "bin"] = "png",
    lo_pct: float = 1.0,
    hi_pct: float = 99.5,
    gamma: float = 1.0,
    window_lo: Optional[float] = None,
    window_hi: Optional[float] = None,
    invert: bool = False,
    rotate_deg: int = 0,
    colormap: str = "none",
):
    """
    Render a channel as PNG (default) or a compact binary envelope.

    `format=png` (default):
        - `transform=raw` → lossless native-bit-depth PNG (16-bit for
          energies, 8-bit for FS6000 material)
        - Any other transform → 8-bit cooked PNG

    `format=bin` (client-side 16-bit rendering):
        - Always returns the raw native-bit-depth pixels in a tiny
          'XRAY' binary envelope (see rendering.render_raw_binary).
          The `transform` argument is IGNORED in bin mode — the browser
          is expected to apply window/level interactively on its copy.
          Used by the X-Ray Inspector canvas viewer so it can keep a
          true uint16 buffer in memory and re-render at drag speed.
    """
    _record, img = _load_any(scanner, scan_id)
    arr = _pick_channel(img, channel)

    if format == "bin":
        bin_bytes = rendering.render_raw_binary(arr, rotate_deg=rotate_deg)
        return Response(content=bin_bytes, media_type="application/octet-stream")

    if transform == "raw" and colormap == "none":
        png = rendering.render_raw_png(arr, rotate_deg=rotate_deg)
        return Response(content=png, media_type="image/png")

    # Cooked path
    if transform == "raw":
        # raw + colormap: downcast to 8-bit linearly then color-map
        if arr.dtype == np.uint16:
            base = (arr >> 8).astype(np.uint8)
        else:
            base = arr.astype(np.uint8)
    else:
        png = rendering.render_cooked_png(
            arr,
            transform=transform,
            lo_pct=lo_pct,
            hi_pct=hi_pct,
            gamma=gamma,
            window_lo=window_lo,
            window_hi=window_hi,
            invert=invert,
            rotate_deg=rotate_deg,
        )
        if colormap == "none":
            return Response(content=png, media_type="image/png")
        # Decode the cooked PNG back to an 8-bit array for colormap application
        base_img = cv2.imdecode(np.frombuffer(png, dtype=np.uint8), cv2.IMREAD_GRAYSCALE)
        base = base_img

    bgr = rendering.apply_colormap(base, colormap)
    png = rendering.encode_bgr_png(bgr)
    return Response(content=png, media_type="image/png")


# ── Dual-energy composite (FS6000 + ASE tri-panel) ──────────────────

@router.get("/composite/{scanner}/{scan_id}")
def get_composite(
    scanner: str,
    scan_id: str,
    luminance: Literal["high", "low", "avg"] = "high",
    material_strength: float = 0.65,
):
    _record, img = _load_any(scanner, scan_id)
    if isinstance(img, Fs6000Image):
        bgr = composite.composite_fs6000_color(
            low=img.low,
            high=img.high,
            material=img.material,
            luminance_source=luminance,
            material_strength=material_strength,
        )
    elif isinstance(img, AseImage) and img.is_multi_panel:
        bgr = composite.composite_ase_tri_panel(img, material_strength=material_strength)
    else:
        raise HTTPException(400, "composite requires FS6000 or ASE tri-panel scan")
    return Response(content=rendering.encode_bgr_png(bgr), media_type="image/png")


# ── Vendor-rendered JPEG pass-through ────────────────────────────────

@router.get("/vendor-jpeg/{scanner}/{scan_id}")
def get_vendor_jpeg(scanner: str, scan_id: str):
    if scanner.lower() == "fs6000":
        record = load_fs6000_scan(scan_id)
        folder = resolve_fs6000_folder(record)
        paths = Fs6000Paths.from_folder(folder)
        if not paths.jpeg or not paths.jpeg.exists():
            raise HTTPException(404, "vendor JPEG not found")
        return Response(content=paths.jpeg.read_bytes(), media_type="image/jpeg")
    raise HTTPException(400, "vendor JPEG pass-through only available for FS6000")


# ── ROI stats ────────────────────────────────────────────────────────

class RoiRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    roi: dict = Field(default_factory=lambda: {"kind": "whole"})
    bins: int = 64


@router.post("/roi-stats")
def roi_stats_endpoint(req: RoiRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    mask = analysis.build_mask(arr.shape, req.roi)
    return analysis.roi_stats(arr, mask, bins=req.bins)


# ── Line profile ─────────────────────────────────────────────────────

class LineProfileRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    x0: float
    y0: float
    x1: float
    y1: float
    samples: int = 512


@router.post("/line-profile")
def line_profile_endpoint(req: LineProfileRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    return analysis.line_profile(arr, req.x0, req.y0, req.x1, req.y1, samples=req.samples)


# ── Edge detection ───────────────────────────────────────────────────

class EdgeRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    method: Literal["canny", "sobel"] = "canny"
    low: float = 50
    high: float = 150
    ksize: int = 5
    rotate_deg: int = 0


@router.post("/edge")
def edge_endpoint(req: EdgeRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    if req.method == "canny":
        edges_bool = analysis.edge_canny(arr, req.low, req.high)
        edges_8 = (edges_bool.astype(np.uint8) * 255)
    else:
        edges_8 = analysis.edge_sobel(arr, ksize=req.ksize)
    png = rendering.render_raw_png(edges_8, rotate_deg=req.rotate_deg)
    return Response(content=png, media_type="image/png")


# ── Threshold mask ───────────────────────────────────────────────────

class ThresholdRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    low: Optional[float] = None
    high: Optional[float] = None
    rotate_deg: int = 0


@router.post("/threshold")
def threshold_endpoint(req: ThresholdRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    mask = analysis.threshold_mask(arr, low=req.low, high=req.high)
    mask_8 = (mask.astype(np.uint8) * 255)
    png = rendering.render_raw_png(mask_8, rotate_deg=req.rotate_deg)
    return Response(content=png, media_type="image/png")


# ── Object detection ─────────────────────────────────────────────────

class ObjectsRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    low: Optional[float] = None
    high: Optional[float] = None
    min_area: int = 25
    max_objects: int = 500


@router.post("/objects")
def objects_endpoint(req: ObjectsRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    mask = analysis.threshold_mask(arr, low=req.low, high=req.high)
    objects = analysis.find_objects(mask, min_area=req.min_area, max_objects=req.max_objects)
    return {"count": len(objects), "objects": objects}


# ── Dual-energy difference ───────────────────────────────────────────

@router.post("/dual-energy-diff")
def dual_energy_diff_endpoint(scanner: str, id: str, rotate_deg: int = 0):
    _record, img = _load_any(scanner, id)
    if isinstance(img, Fs6000Image):
        diff = composite.dual_energy_difference(img.low, img.high)
    elif isinstance(img, AseImage) and img.is_multi_panel:
        diff = composite.dual_energy_difference(img.panel(0), img.panel(1))
    else:
        raise HTTPException(400, "dual-energy diff requires FS6000 or ASE tri-panel")
    png = rendering.render_raw_png(diff, rotate_deg=rotate_deg)
    return Response(content=png, media_type="image/png")


# ── Export: CSV ──────────────────────────────────────────────────────

class ExportCsvRequest(BaseModel):
    scanner: str
    id: str
    channel: str = "main"
    rois: list[dict]
    labels: Optional[list[str]] = None


@router.post("/export/roi-csv")
def export_roi_csv(req: ExportCsvRequest):
    _record, img = _load_any(req.scanner, req.id)
    arr = _pick_channel(img, req.channel)
    out = io.StringIO()
    out.write("label,kind,count,min,max,mean,std,median,p01,p99\n")
    labels = req.labels or [f"ROI {i + 1}" for i in range(len(req.rois))]
    for label, roi in zip(labels, req.rois):
        mask = analysis.build_mask(arr.shape, roi)
        stats = analysis.roi_stats(arr, mask, bins=1)
        out.write(
            f"{label},{roi.get('kind', '?')},{stats['count']},"
            f"{stats['min']},{stats['max']},{stats['mean']},{stats['std']},"
            f"{stats['median']},{stats['p01']},{stats['p99']}\n"
        )
    return Response(
        content=out.getvalue(),
        media_type="text/csv",
        headers={"Content-Disposition": f"attachment; filename=roi_stats_{req.id}.csv"},
    )


# ── Export: PDF report ───────────────────────────────────────────────

class PdfReportRequest(BaseModel):
    scanner: str
    id: str
    title: Optional[str] = None
    notes: Optional[str] = None
    rois: list[dict] = []
    channel: str = "main"
    include_composite: bool = True


@router.post("/export/pdf-report")
def export_pdf_report(req: PdfReportRequest):
    record, img = _load_any(req.scanner, req.id)
    try:
        pdf_bytes = report.build_pdf_report(
            record=record,
            img=img,
            channel=req.channel,
            rois=req.rois,
            title=req.title,
            notes=req.notes,
            include_composite=req.include_composite,
        )
    except Exception as e:
        logger.exception("pdf report generation failed")
        raise HTTPException(500, f"report generation failed: {e}")
    return Response(
        content=pdf_bytes,
        media_type="application/pdf",
        headers={"Content-Disposition": f"attachment; filename=xray_report_{req.id}.pdf"},
    )
