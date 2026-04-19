"""
Re-process split jobs using 16-bit images from the ASE pipeline.

For each pending split job:
  1. Find the matching raw ASE scan blob in asescans
  2. Decode via inspector's decode_ase (16-bit uint16 pixel array)
  3. Rotate 90° CCW to landscape
  4. Render as 16-bit PNG via inspector's render_raw_png
  5. Upload the 16-bit PNG to the splitter via /api/split/upload
  6. Wait for the full pipeline (all strategies + Claude verifier)
  7. Update the original job's best_strategy + split_x with the new result
  8. Clean up the scratch job

The 16-bit PNG preserves the full dynamic range of the X-ray scan,
giving sharper corner-casting contrast for both the inner_casting_pair
CV detector and Claude Vision's analysis.
"""
import io
import os
import sys
import time
import uuid

import numpy as np
import psycopg2
import requests
from PIL import Image

_SPLITTER_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "services", "image-splitter")
sys.path.insert(0, _SPLITTER_DIR)
from inspector.decoders.ase import decode_ase
from inspector.rendering import render_raw_png

DB_CONN = (
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD', '')}"
)
SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")
POLL_INTERVAL = 2
POLL_TIMEOUT = 120  # Claude Vision can take 10-15s per call


def fetch_jobs_with_ase(conn):
    cur = conn.cursor()
    cur.execute("""
        SELECT j.id AS job_id, j.container_numbers, j.split_x, j.best_strategy,
               a.id AS ase_id, a.scanimage
        FROM image_split_jobs j
        JOIN asescans a ON (
            REPLACE(j.container_numbers, ' ', '') = REPLACE(a.containernumber, ' ', '')
        )
        WHERE j.analyst_verdict IS NULL
          AND j.container_numbers <> 'TEST001,TEST002'
          AND a.scanimage IS NOT NULL
        ORDER BY j.created_at DESC
    """)
    return cur.fetchall()


def ase_to_16bit_png(raw_bytes: bytes) -> bytes:
    """Decode ASE blob → 16-bit PNG at native resolution."""
    ase = decode_ase(raw_bytes)
    arr = ase.panel(0) if ase.is_multi_panel else ase.pixels
    # Rotate 90° CCW so trailer runs left-to-right
    arr = np.rot90(arr, k=1)
    # Render as 16-bit PNG (lossless, full dynamic range)
    return render_raw_png(arr)


def submit_and_wait(container_numbers, image_bytes):
    files = {"file": ("scan.png", image_bytes, "image/png")}
    data = {"container_numbers": container_numbers}
    r = requests.post(f"{SPLITTER_URL}/api/split/upload", files=files, data=data, timeout=60)
    r.raise_for_status()
    job_id = r.json()["id"]
    deadline = time.time() + POLL_TIMEOUT
    while time.time() < deadline:
        time.sleep(POLL_INTERVAL)
        jr = requests.get(f"{SPLITTER_URL}/api/split/{job_id}", timeout=20).json()
        if jr["status"] in ("completed", "failed"):
            return job_id, jr
    return job_id, None


def get_new_best(conn, scratch_job_id):
    cur = conn.cursor()
    cur.execute("""
        SELECT best_strategy, split_x, image_width, image_height
        FROM image_split_jobs WHERE id = %s
    """, (scratch_job_id,))
    return cur.fetchone()


def update_original(conn, original_job_id, new_best, new_split, new_width, new_height):
    cur = conn.cursor()
    cur.execute("""
        UPDATE image_split_jobs
           SET best_strategy = %s, split_x = %s, best_score = 1.0,
               image_width = %s, image_height = %s
         WHERE id = %s AND analyst_verdict IS NULL
    """, (new_best, new_split, new_width, new_height, str(original_job_id)))
    conn.commit()


def delete_scratch(conn, scratch_job_id):
    cur = conn.cursor()
    cur.execute("DELETE FROM image_split_assignments WHERE job_id = %s", (scratch_job_id,))
    cur.execute("DELETE FROM image_split_assignments WHERE result_id IN (SELECT id FROM image_split_results WHERE job_id = %s)", (scratch_job_id,))
    cur.execute("DELETE FROM image_split_results WHERE job_id = %s", (scratch_job_id,))
    cur.execute("DELETE FROM image_split_jobs WHERE id = %s", (scratch_job_id,))
    conn.commit()


def main():
    conn = psycopg2.connect(DB_CONN)
    jobs = fetch_jobs_with_ase(conn)
    print(f"[16bit] {len(jobs)} jobs with matching ASE scans", flush=True)

    ok = 0
    fail = 0
    changed = 0

    for i, (job_id, containers, old_split, old_best, ase_id, scanimage) in enumerate(jobs, 1):
        try:
            raw = bytes(scanimage)
            png_16bit = ase_to_16bit_png(raw)
            img = Image.open(io.BytesIO(png_16bit))
            w, h = img.size

            scratch_id, jr = submit_and_wait(containers, png_16bit)
            if jr is None or jr["status"] != "completed":
                print(f"[16bit] {i}/{len(jobs)} {str(job_id)[:8]} TIMEOUT/FAILED", flush=True)
                fail += 1
                try:
                    delete_scratch(conn, scratch_id)
                except:
                    pass
                continue

            result = get_new_best(conn, scratch_id)
            if result:
                new_best, new_split, new_w, new_h = result
                did_change = (new_best != old_best or new_split != old_split)
                if did_change:
                    changed += 1
                update_original(conn, job_id, new_best, new_split, new_w or w, new_h or h)
                delete_scratch(conn, scratch_id)
                tag = "CHANGED" if did_change else "same"
                print(
                    f"[16bit] {i}/{len(jobs)} {str(job_id)[:8]} "
                    f"{old_best}@{old_split} -> {new_best}@{new_split} "
                    f"({w}x{h}) {tag}",
                    flush=True,
                )
                ok += 1
            else:
                fail += 1
                delete_scratch(conn, scratch_id)
                print(f"[16bit] {i}/{len(jobs)} {str(job_id)[:8]} no result from scratch", flush=True)

        except Exception as e:
            fail += 1
            try:
                conn.rollback()
            except:
                pass
            print(f"[16bit] {i}/{len(jobs)} {str(job_id)[:8]} ERROR: {e}", flush=True)

    print(flush=True)
    print(f"[16bit] done. ok={ok} fail={fail} changed={changed}", flush=True)
    conn.close()


if __name__ == "__main__":
    main()
