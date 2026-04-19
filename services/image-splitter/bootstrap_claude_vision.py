"""
Bootstrap Claude Vision on the first N multi-container scans (1.19.0).

This is the one-shot script that runs Claude Vision across the existing backlog
of multi-container scans to establish a ground-truth baseline. After it runs,
every scan in the set will have a claude_vision_split_x populated on its job
row, and the analyst /annotate page will show Claude's split alongside the
hand-engineered strategies.

Usage:
    # Bootstrap the first 100 scans chronologically (default)
    python bootstrap_claude_vision.py

    # Different count
    python bootstrap_claude_vision.py --limit 50

    # Newest first instead of oldest first
    python bootstrap_claude_vision.py --order newest

    # Dry run (prints what it would do)
    python bootstrap_claude_vision.py --dry-run

Env vars:
    ANTHROPIC_API_KEY      — REQUIRED. Without it, the claude_vision strategy
                             silently returns None and nothing will be recorded.
    NICKSCAN_DB_PASSWORD   — required
    NSCIM_API_TOKEN        — required for ASE image fetching (Bearer token)
    SPLITTER_URL           — default http://localhost:5320
    NSCIM_API_URL          — default http://localhost:5205

This script does two things per scan:
  1. If the scan has no existing image_split_job row, submits it to the splitter
     via POST /api/split/upload (which runs ALL strategies including claude_vision).
  2. If it already has a job row but no claude_vision_split_x, re-runs just the
     claude_vision strategy in-process and updates the job row directly.

The result is that after running this script:
  - The first N multi-container scans all have a job row in the splitter DB
  - Every job row has claude_vision_split_x and related fields populated
  - You can open /annotate and see Claude's split alongside the others, mark
    ground truth, and iterate.
"""
import argparse
import asyncio
import os
import sys
import time
from datetime import datetime, timezone

import psycopg2
import requests


SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")
API_URL = os.environ.get("NSCIM_API_URL", "http://localhost:5205")
API_TOKEN = os.environ.get("NSCIM_API_TOKEN", "")


def main():
    parser = argparse.ArgumentParser(description="Bootstrap Claude Vision on multi-container scans")
    parser.add_argument("--limit", type=int, default=100, help="Max scans to process (default: 100)")
    parser.add_argument("--order", choices=["oldest", "newest"], default="oldest")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    if not os.environ.get("ANTHROPIC_API_KEY"):
        print("ERROR: ANTHROPIC_API_KEY env var must be set before running this script.")
        print("       The claude_vision strategy silently returns None without it.")
        sys.exit(1)

    pw = os.environ.get("NICKSCAN_DB_PASSWORD", "")
    if not pw:
        print("ERROR: NICKSCAN_DB_PASSWORD env var must be set")
        sys.exit(1)

    conn = psycopg2.connect(host="localhost", dbname="nickscan_production", user="postgres", password=pw)
    cur = conn.cursor()

    # ── Step 1: find candidate scans ──────────────────────────────────────
    order_clause = "ASC" if args.order == "oldest" else "DESC"
    cur.execute(f"""
        SELECT osr.id, osr.originalcontainernumbers, osr.scannertype, osr.inspectionid, osr.scantime
        FROM originalscanrecords osr
        WHERE osr.derivedrecordcount >= 2
          AND osr.originalcontainernumbers IS NOT NULL
          AND osr.originalcontainernumbers <> ''
        ORDER BY osr.scantime {order_clause}
        LIMIT %s
    """, (args.limit,))
    candidates = cur.fetchall()
    print(f"Found {len(candidates)} multi-container scans (order={args.order}, limit={args.limit})")

    processed = 0
    submitted_new = 0
    reran_existing = 0
    skipped_existing = 0
    failed = 0

    for original_id, raw_containers, scanner_type, inspection_id, scan_time in candidates:
        parts = [c.strip() for c in raw_containers.replace(";", ",").split(",")]
        parts = [c for c in parts if len(c) >= 4 and c.upper() != "UNKNOWN"]
        if len(parts) < 2:
            continue
        c1, c2 = parts[0], parts[1]

        # Is there already a splitter job for this container pair?
        cur.execute(
            """
            SELECT id, claude_vision_split_x
            FROM image_split_jobs
            WHERE container_numbers ILIKE %s OR container_numbers ILIKE %s
            ORDER BY created_at DESC LIMIT 1
            """,
            (f"%{c1}%{c2}%", f"%{c2}%{c1}%"),
        )
        existing = cur.fetchone()

        if existing and existing[1] is not None:
            # Already has Claude Vision data
            skipped_existing += 1
            continue

        if existing:
            # Job exists but Claude Vision never ran on it. Re-process it by
            # re-submitting the strategy in-process.
            job_id = existing[0]
            print(f"  RE-RUN {c1}|{c2} (existing job {str(job_id)[:8]})")
            if args.dry_run:
                reran_existing += 1
                continue
            ok = rerun_claude_on_existing_job(cur, conn, job_id)
            if ok:
                reran_existing += 1
            else:
                failed += 1
            processed += 1
            continue

        # No existing job — fetch the image and submit a fresh job to the splitter.
        image_data = fetch_image(cur, c1, c2, scanner_type, inspection_id)
        if not image_data:
            print(f"  SKIP   {c1}|{c2} no image available")
            failed += 1
            continue

        print(f"  SUBMIT {c1}|{c2} ({scanner_type}, {len(image_data)//1024}KB)")
        if args.dry_run:
            submitted_new += 1
            continue

        try:
            resp = requests.post(
                f"{SPLITTER_URL}/api/split/upload",
                files={"file": ("scan.jpg", image_data, "image/jpeg")},
                data={"container_numbers": f"{c1},{c2}", "scanner_type": scanner_type},
                timeout=60,
            )
            if resp.ok:
                submitted_new += 1
            else:
                print(f"    ERR HTTP {resp.status_code}: {resp.text[:120]}")
                failed += 1
        except Exception as e:
            print(f"    EXC {e}")
            failed += 1

        processed += 1
        time.sleep(0.5)  # be kind to the Anthropic API

    print(f"\nDone: {submitted_new} new jobs submitted, {reran_existing} existing jobs re-run, {skipped_existing} already had Claude data, {failed} failed")
    conn.close()


def fetch_image(cur, c1, c2, scanner_type, inspection_id):
    """Return raw JPEG bytes for the scan or None."""
    if scanner_type.upper() == "ASE":
        cur.execute(
            "SELECT id FROM asescans WHERE inspectionid::text = %s AND scanimage IS NOT NULL LIMIT 1",
            (str(inspection_id),),
        )
        if not cur.fetchone():
            return None
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


def rerun_claude_on_existing_job(cur, conn, job_id):
    """Fetch image bytes from image_split_jobs and re-run just the Claude Vision strategy."""
    try:
        cur.execute("SELECT image_data FROM image_split_jobs WHERE id = %s", (str(job_id),))
        row = cur.fetchone()
        if not row or not row[0]:
            print(f"    ERR  job {str(job_id)[:8]} has no image_data")
            return False
        image_data = bytes(row[0])
    except Exception as e:
        print(f"    ERR  loading image bytes for job {str(job_id)[:8]}: {e}")
        return False

    try:
        # Import here so the script doesn't hard-depend on the strategy package
        # unless it actually needs to run Claude.
        from strategies.claude_vision import ClaudeVisionStrategy
        from pipeline.image_utils import decode_image
    except Exception as e:
        print(f"    ERR  could not import strategy: {e}")
        return False

    strategy = ClaudeVisionStrategy()
    image_array = decode_image(image_data)

    try:
        result = asyncio.run(strategy.analyze(image_data, image_array))
    except Exception as e:
        print(f"    ERR  strategy.analyze raised: {e}")
        return False

    if result is None:
        print(f"    WARN job {str(job_id)[:8]} Claude Vision returned None (check ANTHROPIC_API_KEY)")
        return False

    # Persist to both the results table and the denormalised job row
    try:
        import json as _json
        usage = (result.metadata or {}).get("usage", {}) or {}
        cur.execute(
            """
            INSERT INTO image_split_results
                (id, job_id, strategy_name, split_x, confidence, processing_ms, metadata, created_at)
            VALUES (gen_random_uuid(), %s, %s, %s, %s, %s, %s::jsonb, now())
            """,
            (
                str(job_id),
                result.strategy_name,
                result.split_x,
                result.confidence,
                result.processing_ms,
                _json.dumps(result.metadata or {}),
            ),
        )
        cur.execute(
            """
            UPDATE image_split_jobs
               SET claude_vision_split_x = %s,
                   claude_vision_confidence = %s,
                   claude_vision_reasoning = %s,
                   claude_vision_input_tokens = %s,
                   claude_vision_output_tokens = %s,
                   claude_vision_latency_ms = %s,
                   claude_vision_model = %s,
                   claude_vision_ran_at = now()
             WHERE id = %s
            """,
            (
                result.split_x,
                result.confidence,
                (result.metadata or {}).get("reasoning"),
                usage.get("input_tokens"),
                usage.get("output_tokens"),
                result.processing_ms,
                (result.metadata or {}).get("model"),
                str(job_id),
            ),
        )
        conn.commit()
        print(f"    OK   job {str(job_id)[:8]} Claude split_x={result.split_x} conf={result.confidence:.3f}")
        return True
    except Exception as e:
        conn.rollback()
        print(f"    ERR  DB update failed for job {str(job_id)[:8]}: {e}")
        return False


if __name__ == "__main__":
    main()
