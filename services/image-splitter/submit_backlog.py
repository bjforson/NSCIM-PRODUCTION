"""
Submit ALL multi-container scans to the image splitter (1.19.0).

Previously this script only pulled from crossrecordscans, which meant the
splitter only saw scans where the cross-record detector had already fired.
That was too restrictive — many legitimate dual-container scans never got
split because either (a) they belonged to the same record (not "cross"),
or (b) the cross-record identification was broken (the 1.19.0
settledCount trap bug).

Now this script pulls from OriginalScanRecord WHERE DerivedRecordCount >= 2,
which is the scanner-agnostic source-of-truth for multi-container scans.
CrossRecordScan is consulted only to enrich metadata about cross-record
pairs (useful for downstream analyst review), not as a filter.

Uses the .NET API's /api/ContainerDetails/image/ase/full endpoint to convert
ASE proprietary images to JPEG before submitting to the Python splitter.

Run from within the venv. Safe to re-run — skips already-submitted pairs.

Usage:
    python submit_backlog.py [--limit N] [--dry-run]

Env vars:
    NICKSCAN_DB_PASSWORD   — required
    NSCIM_API_TOKEN        — required for ASE image fetch (Authorization: Bearer ...)
"""
import os
import sys
import time
import argparse
import psycopg2
import requests

SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")
API_URL = os.environ.get("NSCIM_API_URL", "http://localhost:5205")
API_TOKEN = os.environ.get("NSCIM_API_TOKEN", "")


def main():
    parser = argparse.ArgumentParser(description="Submit multi-container scans to the image splitter")
    parser.add_argument("--limit", type=int, default=None, help="Max scans to submit this run")
    parser.add_argument("--dry-run", action="store_true", help="List what would be submitted without submitting")
    parser.add_argument("--order", choices=["oldest", "newest"], default="oldest",
                        help="Process oldest (default, for bootstrap) or newest first")
    args = parser.parse_args()

    pw = os.environ.get("NICKSCAN_DB_PASSWORD", "")
    if not pw:
        print("ERROR: NICKSCAN_DB_PASSWORD env var must be set")
        sys.exit(1)

    conn = psycopg2.connect(host="localhost", dbname="nickscan_production", user="postgres", password=pw)
    cur = conn.cursor()

    # 1.19.0 — pull ALL multi-container scans from OriginalScanRecord, not just crossrecordscans
    order_clause = "ASC" if args.order == "oldest" else "DESC"
    limit_clause = f"LIMIT {args.limit}" if args.limit else ""

    cur.execute(f"""
        SELECT osr.id, osr.originalcontainernumbers, osr.scannertype, osr.inspectionid, osr.scantime
        FROM originalscanrecords osr
        WHERE osr.derivedrecordcount >= 2
          AND osr.originalcontainernumbers IS NOT NULL
          AND osr.originalcontainernumbers <> ''
        ORDER BY osr.scantime {order_clause}
        {limit_clause}
    """)
    originals = cur.fetchall()
    print(f"Found {len(originals)} multi-container scans to consider")

    # Get already-submitted container pairs from splitter
    try:
        resp = requests.get(f"{SPLITTER_URL}/api/split/pending", timeout=10)
        existing = resp.json() if resp.ok else []
    except Exception as e:
        print(f"WARNING: Could not reach splitter at {SPLITTER_URL}: {e}")
        existing = []

    existing_pairs = set()
    for j in existing:
        parts = [p.strip() for p in j["container_numbers"].split(",") if p.strip()]
        if len(parts) >= 2:
            key = tuple(sorted(parts[:2]))
            existing_pairs.add(key)
    print(f"Splitter already has {len(existing)} jobs ({len(existing_pairs)} unique pairs)")

    submitted = 0
    skipped = 0
    failed = 0
    no_image = 0

    for original_id, raw_containers, scanner_type, inspection_id, scan_time in originals:
        # Split and normalize container numbers
        parts = [c.strip() for c in raw_containers.replace(";", ",").split(",")]
        parts = [c for c in parts if len(c) >= 4 and c.upper() != "UNKNOWN"]
        if len(parts) < 2:
            skipped += 1
            continue

        c1, c2 = parts[0], parts[1]
        pair_key = tuple(sorted([c1, c2]))

        if pair_key in existing_pairs:
            skipped += 1
            continue

        # Fetch image
        image_data = fetch_image(cur, c1, c2, scanner_type, inspection_id)
        if not image_data:
            no_image += 1
            continue

        if args.dry_run:
            print(f"  DRY {c1}|{c2} ({scanner_type}, {len(image_data)//1024}KB)")
            submitted += 1
            existing_pairs.add(pair_key)
            continue

        try:
            resp = requests.post(
                f"{SPLITTER_URL}/api/split/upload",
                files={"file": ("scan.jpg", image_data, "image/jpeg")},
                data={"container_numbers": f"{c1},{c2}", "scanner_type": scanner_type},
                timeout=30
            )
            if resp.ok:
                job = resp.json()
                job_short = job.get("id", "")[:8] if isinstance(job.get("id"), str) else str(job.get("id", ""))[:8]
                print(f"  OK  {c1}|{c2} ({scanner_type}) job={job_short} ({len(image_data)//1024}KB)")
                submitted += 1
                existing_pairs.add(pair_key)
            else:
                print(f"  ERR {c1}|{c2} HTTP {resp.status_code}: {resp.text[:120]}")
                failed += 1
        except Exception as e:
            print(f"  EXC {c1}|{c2} {e}")
            failed += 1

        time.sleep(0.2)

    print(f"\nDone: {submitted} submitted, {skipped} skipped, {no_image} no image, {failed} failed")
    conn.close()


def fetch_image(cur, c1, c2, scanner_type, inspection_id):
    """Return raw JPEG bytes for the scan, or None if unavailable."""
    if scanner_type.upper() == "ASE":
        # Look up the ASE scan row by inspection id
        cur.execute(
            "SELECT id FROM asescans WHERE inspectionid::text = %s AND scanimage IS NOT NULL LIMIT 1",
            (str(inspection_id),),
        )
        if not cur.fetchone():
            return None

        # Use the .NET API endpoint for ASE-to-JPEG conversion
        for cn in [c1, c2]:
            try:
                resp = requests.get(
                    f"{API_URL}/api/ContainerDetails/image/ase/full",
                    params={"container": cn},
                    headers={"Authorization": f"Bearer {API_TOKEN}"} if API_TOKEN else {},
                    timeout=30,
                    verify=False,
                )
                if resp.ok and resp.headers.get("content-type", "").startswith("image/"):
                    return resp.content
            except Exception:
                pass
        return None

    elif scanner_type.upper() == "FS6000":
        cur.execute(
            """
            SELECT i.imagedata
            FROM fs6000images i
            JOIN fs6000scans s ON s.id = i.scanid
            WHERE s.inspectionid::text = %s AND i.imagetype = 'Main' AND i.imagedata IS NOT NULL
            ORDER BY i.createdat LIMIT 1
            """,
            (str(inspection_id),),
        )
        row = cur.fetchone()
        if row and row[0]:
            return bytes(row[0])
        return None

    return None


if __name__ == "__main__":
    main()
