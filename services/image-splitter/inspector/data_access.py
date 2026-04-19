"""
Data-access helpers for the X-Ray Inspector.

Responsibilities:
    - Look up ASE scans by id or container number → fetch the raw `bytea`
      blob from `asescans.scanimage`
    - Look up FS6000 scans by id or container number → resolve the filesystem
      folder containing `{stem}high.img`, `{stem}low.img`, `{stem}material.img`
    - Search across both scanner tables for a container-number query
    - Return lightweight metadata records for the Blazor UI's scan picker

Uses the sync psycopg2 engine from the splitter's `config.DATABASE_URL_SYNC`
so callers can treat these as regular blocking functions (the inspector
routes are kept synchronous for simplicity — image math dominates wall time).
"""
from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

import psycopg2
import psycopg2.extras

from config import DB_PASSWORD


# ── Connection helper ────────────────────────────────────────────────

def _connect():
    return psycopg2.connect(
        host=os.environ.get("NICKSCAN_DB_HOST", "localhost"),
        port=int(os.environ.get("NICKSCAN_DB_PORT", "5432")),
        dbname=os.environ.get("NICKSCAN_DB_NAME", "nickscan_production"),
        user=os.environ.get("NICKSCAN_DB_USER", "postgres"),
        password=DB_PASSWORD,
    )


# ── Data classes ─────────────────────────────────────────────────────

@dataclass
class ScanSearchResult:
    """Lightweight record for the search sidebar."""
    scanner: str              # "ase" or "fs6000"
    id: str                   # UUID string
    container_number: Optional[str]
    scan_time: Optional[str]  # ISO format
    inspection_id: Optional[str] = None
    file_path: Optional[str] = None
    has_blob: bool = False

    def to_dict(self) -> dict:
        return {
            "scanner": self.scanner,
            "id": self.id,
            "container_number": self.container_number,
            "scan_time": self.scan_time,
            "inspection_id": self.inspection_id,
            "has_blob": self.has_blob,
        }


@dataclass
class AseScanRecord:
    id: str
    inspection_id: Optional[int]
    inspection_uuid: Optional[str]
    container_number: Optional[str]
    truck_plate: Optional[str]
    image_display_name: Optional[str]
    scan_time: Optional[str]
    blob: bytes

    def to_metadata_dict(self) -> dict:
        return {
            "scanner": "ase",
            "id": self.id,
            "inspection_id": self.inspection_id,
            "inspection_uuid": self.inspection_uuid,
            "container_number": self.container_number,
            "truck_plate": self.truck_plate,
            "image_display_name": self.image_display_name,
            "scan_time": self.scan_time,
            "blob_length": len(self.blob),
        }


@dataclass
class Fs6000ScanRecord:
    id: str
    container_number: Optional[str]
    pic_number: Optional[str]
    scan_time: Optional[str]
    file_path: Optional[str]
    has_image: bool
    image_count: int
    has_img_blob: bool = False  # True when HighEnergy/LowEnergy/Material rows exist in DB

    def to_metadata_dict(self) -> dict:
        return {
            "scanner": "fs6000",
            "id": self.id,
            "container_number": self.container_number,
            "pic_number": self.pic_number,
            "scan_time": self.scan_time,
            "file_path": self.file_path,
            "has_image": self.has_image,
            "image_count": self.image_count,
        }


# ── Search across both scanner tables ────────────────────────────────

def search_scans(
    query: str,
    scanner: str = "both",
    limit: int = 50,
) -> list[ScanSearchResult]:
    """
    Search for scans by container number. Matches either scanner table.
    `scanner` may be "ase", "fs6000", or "both".
    """
    q = (query or "").strip()
    if not q:
        return []
    results: list[ScanSearchResult] = []

    conn = _connect()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)

        if scanner in ("ase", "both"):
            cur.execute(
                """
                SELECT id::text AS id,
                       inspectionid::text AS inspection_id,
                       inspectionuuid,
                       containernumber,
                       scantime,
                       (scanimage IS NOT NULL) AS has_blob
                FROM asescans
                WHERE containernumber ILIKE %s
                ORDER BY scantime DESC NULLS LAST
                LIMIT %s
                """,
                (f"%{q}%", limit),
            )
            for row in cur.fetchall():
                results.append(ScanSearchResult(
                    scanner="ase",
                    id=row["id"],
                    container_number=row["containernumber"],
                    scan_time=row["scantime"].isoformat() if row["scantime"] else None,
                    inspection_id=row["inspection_id"],
                    has_blob=bool(row["has_blob"]),
                ))

        if scanner in ("fs6000", "both"):
            cur.execute(
                """
                SELECT id::text AS id,
                       containernumber,
                       picnumber,
                       scantime,
                       filepath,
                       hasimage
                FROM fs6000scans
                WHERE containernumber ILIKE %s
                ORDER BY scantime DESC NULLS LAST
                LIMIT %s
                """,
                (f"%{q}%", limit),
            )
            for row in cur.fetchall():
                results.append(ScanSearchResult(
                    scanner="fs6000",
                    id=row["id"],
                    container_number=row["containernumber"],
                    scan_time=row["scantime"].isoformat() if row["scantime"] else None,
                    file_path=row["filepath"],
                    has_blob=bool(row["hasimage"]),
                ))
        cur.close()
    finally:
        conn.close()

    # Sort combined list by scan_time desc (nulls last), then scanner, container
    def sort_key(r: ScanSearchResult):
        return (r.scan_time or "", r.scanner, r.container_number or "")
    results.sort(key=sort_key, reverse=True)
    return results[:limit]


# ── ASE blob fetching ────────────────────────────────────────────────

def load_ase_scan(scan_id: str) -> AseScanRecord:
    """
    Load an ASE scan record + its raw bytea blob by UUID.
    Raises KeyError if not found.
    """
    conn = _connect()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        cur.execute(
            """
            SELECT id::text AS id,
                   inspectionid,
                   inspectionuuid,
                   containernumber,
                   truckplate,
                   imagedisplayname,
                   scantime,
                   scanimage
            FROM asescans
            WHERE id = %s::uuid
            """,
            (scan_id,),
        )
        row = cur.fetchone()
        cur.close()
    finally:
        conn.close()

    if not row:
        raise KeyError(f"ASE scan {scan_id} not found")
    if row["scanimage"] is None:
        raise KeyError(f"ASE scan {scan_id} has no scanimage blob")

    return AseScanRecord(
        id=row["id"],
        inspection_id=row["inspectionid"],
        inspection_uuid=row["inspectionuuid"],
        container_number=row["containernumber"],
        truck_plate=row["truckplate"],
        image_display_name=row["imagedisplayname"],
        scan_time=row["scantime"].isoformat() if row["scantime"] else None,
        blob=bytes(row["scanimage"]),
    )


# ── FS6000 path resolution ───────────────────────────────────────────

def load_fs6000_scan(scan_id: str) -> Fs6000ScanRecord:
    """
    Load an FS6000 scan metadata record by UUID. Also checks whether
    raw .img channel blobs (HighEnergy/LowEnergy/Material) are stored
    in the database for DB-first image loading.
    """
    conn = _connect()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        cur.execute(
            """
            SELECT id::text AS id,
                   containernumber,
                   picnumber,
                   scantime,
                   filepath,
                   hasimage,
                   imagecount,
                   EXISTS(
                       SELECT 1 FROM fs6000images i
                       WHERE i.scanid = fs6000scans.id
                         AND i.imagetype IN ('HighEnergy', 'LowEnergy', 'Material')
                         AND i.imagedata IS NOT NULL
                   ) AS has_img_blob
            FROM fs6000scans
            WHERE id = %s::uuid
            """,
            (scan_id,),
        )
        row = cur.fetchone()
        cur.close()
    finally:
        conn.close()

    if not row:
        raise KeyError(f"FS6000 scan {scan_id} not found")

    return Fs6000ScanRecord(
        id=row["id"],
        container_number=row["containernumber"],
        pic_number=row["picnumber"],
        scan_time=row["scantime"].isoformat() if row["scantime"] else None,
        file_path=row["filepath"],
        has_image=bool(row["hasimage"]),
        image_count=int(row["imagecount"] or 0),
        has_img_blob=bool(row.get("has_img_blob", False)),
    )


# Map from DB ImageType to channel key used by the decoder
_IMG_TYPE_MAP = {
    "HighEnergy": "high",
    "LowEnergy": "low",
    "Material": "material",
}


def load_fs6000_img_blobs(scan_id: str) -> dict[str, bytes]:
    """
    Fetch the raw .img file bytes for an FS6000 scan from the database.
    Returns a dict with keys 'high', 'low', 'material' mapping to raw bytes.
    Raises KeyError if any channel is missing.
    """
    conn = _connect()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        cur.execute(
            """
            SELECT imagetype, imagedata
            FROM fs6000images
            WHERE scanid = %s::uuid
              AND imagetype IN ('HighEnergy', 'LowEnergy', 'Material')
              AND imagedata IS NOT NULL
            """,
            (scan_id,),
        )
        rows = cur.fetchall()
        cur.close()
    finally:
        conn.close()

    result: dict[str, bytes] = {}
    for row in rows:
        channel_key = _IMG_TYPE_MAP.get(row["imagetype"])
        if channel_key:
            result[channel_key] = bytes(row["imagedata"])

    missing = {"high", "low", "material"} - result.keys()
    if missing:
        raise KeyError(
            f"FS6000 scan {scan_id}: img blobs missing channels {missing} in database"
        )
    return result


def _candidate_roots() -> list[Path]:
    """
    Ordered list of folders we'll search for FS6000 raw `.img` files.
    The live network share is authoritative — the local staging directory
    is transient (files are moved out after ingestion) and the archive
    only holds post-processing artifacts.
    """
    roots = []
    # 1. Dev mount (most common)
    share = os.environ.get("NICKSCAN_FS6000_SHARE", r"Z:\23301FS01")
    roots.append(Path(share))
    # 2. Production UNC path
    prod_share = os.environ.get("NICKSCAN_FS6000_SHARE_PROD", r"\\172.16.1.1\image\23301FS01")
    roots.append(Path(prod_share))
    # 3. Local staging
    staging = os.environ.get("NICKSCAN_FS6000_STAGING",
                              r"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Staging")
    roots.append(Path(staging))
    return roots


def _picnumber_to_relative(pic_number: str) -> Optional[Path]:
    """
    Decode a PIC number like '23301FS01202604090001' into a relative path
    YYYY/MMDD/NNNN. Format: STATION(7) + YYYY(4) + MMDD(4) + NNNN(4) = 19 chars.
    The station prefix is variable length; we anchor on the last 12 chars.
    """
    if not pic_number or len(pic_number) < 12:
        return None
    tail = pic_number[-12:]      # YYYYMMDDNNNN
    year = tail[0:4]
    mmdd = tail[4:8]
    nnnn = tail[8:12]
    if not (year.isdigit() and mmdd.isdigit() and nnnn.isdigit()):
        return None
    return Path(year) / mmdd / nnnn


def resolve_fs6000_folder(record: Fs6000ScanRecord) -> Path:
    """
    Return the filesystem folder containing the three `.img` files for a
    given FS6000 scan. Strategy:

        1. If `record.file_path` points to an existing file or directory,
           use it (or its parent).
        2. Otherwise, decode the PIC number into YYYY/MMDD/NNNN and look
           for a matching folder under the network share or staging roots.
        3. As a last resort, walk the staging root for any folder
           containing the PIC number in its name.

    Always prefers a folder that actually contains `*high.img` — we skip
    candidates that only hold the rendered JPEG + XML.
    """
    def _has_raw(p: Path) -> bool:
        try:
            return any(p.glob("*high.img"))
        except OSError:
            return False

    tried: list[str] = []

    # 1. Recorded FilePath
    if record.file_path:
        p = Path(record.file_path)
        if p.is_file():
            p = p.parent
        tried.append(str(p))
        if p.is_dir() and _has_raw(p):
            return p

    # 2. PIC-number-derived path under known roots
    rel = _picnumber_to_relative(record.pic_number or "")
    if rel is not None:
        for root in _candidate_roots():
            candidate = root / rel
            tried.append(str(candidate))
            if candidate.is_dir() and _has_raw(candidate):
                return candidate

    # 3. Fallback: recursive search under staging for the PIC number
    if record.pic_number:
        for root in _candidate_roots():
            if not root.exists():
                continue
            try:
                for hit in root.rglob(f"*{record.pic_number}*high.img"):
                    return hit.parent
            except OSError:
                continue

    raise FileNotFoundError(
        f"FS6000 scan {record.id}: raw .img folder not found. "
        f"Tried: {tried}. pic_number={record.pic_number!r}, "
        f"file_path={record.file_path!r}"
    )
