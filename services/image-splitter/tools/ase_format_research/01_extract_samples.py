"""
01_extract_samples.py

Pull raw ASE scan blobs from asescans.scanimage directly to disk so they can
be analyzed without any DLL in the loop.

Writes, for each sample:
  samples/<asescan_id>.ase         raw bytes (bytea straight from postgres)
  samples/<asescan_id>.json        metadata sidecar (ids, sha256, size, scan time)

Usage:
  python 01_extract_samples.py --count 10
  python 01_extract_samples.py --count 5 --min-size 50000 --max-size 2000000

Reads NICKSCAN_DB_PASSWORD from the environment. No writes to the DB.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

import psycopg2

HERE = Path(__file__).resolve().parent
SAMPLES_DIR = HERE / "samples"


def connect():
    pw = os.environ.get("NICKSCAN_DB_PASSWORD", "")
    if not pw:
        print("ERROR: NICKSCAN_DB_PASSWORD not set in environment", file=sys.stderr)
        sys.exit(2)
    return psycopg2.connect(
        host="localhost",
        port=5432,
        dbname="nickscan_production",
        user="postgres",
        password=pw,
    )


def fetch_samples(conn, count: int, min_size: int, max_size: int):
    """Fetch a spread of blobs: largest, smallest, and random middle ones."""
    cur = conn.cursor()

    # Use bucketing to get a varied spread instead of just "LIMIT N".
    cur.execute(
        """
        SELECT
            id,
            inspectionid,
            inspectionuuid,
            containernumber,
            truckplate,
            imagedisplayname,
            scantime,
            length(scanimage) AS blob_len,
            scanimage
        FROM asescans
        WHERE scanimage IS NOT NULL
          AND length(scanimage) BETWEEN %s AND %s
        ORDER BY
            -- spread across the id range: hash-shuffle for pseudo-random but deterministic
            md5(id::text)
        LIMIT %s
        """,
        (min_size, max_size, count),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def write_sample(row) -> dict:
    (
        asescan_id,
        inspection_id,
        inspection_uuid,
        container_number,
        truck_plate,
        display_name,
        scan_time,
        blob_len,
        blob,
    ) = row

    blob_bytes = bytes(blob) if blob is not None else b""
    sha256 = hashlib.sha256(blob_bytes).hexdigest()

    stem = str(asescan_id)
    ase_path = SAMPLES_DIR / f"{stem}.ase"
    meta_path = SAMPLES_DIR / f"{stem}.json"

    ase_path.write_bytes(blob_bytes)

    meta = {
        "asescan_id": str(asescan_id),
        "inspection_id": inspection_id,
        "inspection_uuid": inspection_uuid,
        "container_number": container_number,
        "truck_plate": truck_plate,
        "image_display_name": display_name,
        "scan_time": scan_time.isoformat() if scan_time else None,
        "blob_length_db": blob_len,
        "blob_length_disk": len(blob_bytes),
        "sha256": sha256,
        "extracted_at": datetime.now(timezone.utc).isoformat(),
        "source": "asescans.scanimage",
    }
    meta_path.write_text(json.dumps(meta, indent=2))

    assert blob_len == len(blob_bytes), f"length mismatch for {asescan_id}"
    return meta


def main():
    ap = argparse.ArgumentParser(description="Extract raw ASE blobs from asescans.scanimage")
    ap.add_argument("--count", type=int, default=10)
    ap.add_argument("--min-size", type=int, default=10_240, help="min blob size bytes")
    ap.add_argument("--max-size", type=int, default=50_000_000, help="max blob size bytes")
    args = ap.parse_args()

    SAMPLES_DIR.mkdir(parents=True, exist_ok=True)

    conn = connect()
    try:
        rows = fetch_samples(conn, args.count, args.min_size, args.max_size)
    finally:
        conn.close()

    if not rows:
        print("No asescans rows matched the size filter.", file=sys.stderr)
        sys.exit(1)

    print(f"Fetched {len(rows)} rows. Writing to {SAMPLES_DIR}")
    print()
    print(f"{'asescan_id':<38} {'inspection':>10} {'size':>12}  display_name       container")
    print("-" * 100)
    for row in rows:
        meta = write_sample(row)
        print(
            f"{meta['asescan_id']:<38} "
            f"{meta['inspection_id']:>10} "
            f"{meta['blob_length_disk']:>12}  "
            f"{(meta['image_display_name'] or '')[:18]:<18} "
            f"{meta['container_number'] or ''}"
        )
    print()
    print(f"Done. Extracted {len(rows)} samples to {SAMPLES_DIR}.")


if __name__ == "__main__":
    main()
