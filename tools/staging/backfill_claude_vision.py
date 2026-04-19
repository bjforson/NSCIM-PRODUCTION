"""
Backfill claude_vision for jobs where it never ran (pre-1.20.0 deploy).

For each image_split_jobs row WHERE claude_vision_ran_at IS NULL:
  - POST the raw image_data to /api/split/upload
  - Wait for the new job to complete
  - Copy the claude_vision result (split_x, confidence, reasoning, tokens, slice bytes)
    back onto the ORIGINAL job so its existing UUID stays stable and the review
    page shows claude_vision alongside the original results.

Skips TEST001 synthetic rows and anything that still has image_data = NULL.
"""
import os
import sys
import time
import uuid
import requests
import psycopg2
from psycopg2.extras import Json

DB_CONN = os.environ.get(
    "NICKSCAN_DB_CONN",
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD','')}",
)
SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")

def fetch_targets(conn):
    with conn.cursor() as cur:
        cur.execute("""
            SELECT id, container_numbers, image_width, image_height
            FROM image_split_jobs
            WHERE claude_vision_ran_at IS NULL
              AND image_data IS NOT NULL
              AND container_numbers <> 'TEST001,TEST002'
            ORDER BY created_at DESC
        """)
        return cur.fetchall()

def load_image_bytes(conn, job_id):
    with conn.cursor() as cur:
        cur.execute("SELECT image_data FROM image_split_jobs WHERE id = %s", (job_id,))
        row = cur.fetchone()
        return bytes(row[0]) if row and row[0] else None

def run_claude_vision_on_bytes(image_bytes, container_numbers):
    """POST to /api/split/upload, poll until completed, return (new_job_id, claude_result_dict)."""
    files = {"file": ("scan.jpg", image_bytes, "image/jpeg")}
    data = {"container_numbers": container_numbers}
    r = requests.post(f"{SPLITTER_URL}/api/split/upload", files=files, data=data, timeout=60)
    r.raise_for_status()
    new_job_id = r.json()["id"]

    for _ in range(30):  # up to 60s wait (claude_vision is ~5s)
        time.sleep(2)
        jr = requests.get(f"{SPLITTER_URL}/api/split/{new_job_id}", timeout=15).json()
        if jr["status"] in ("completed", "failed"):
            break

    # Fetch results
    results = requests.get(f"{SPLITTER_URL}/api/split/{new_job_id}/results", timeout=15).json()
    cv = next((r for r in results if r["strategy_name"] == "claude_vision"), None)
    return new_job_id, cv

def copy_cv_back(conn, original_job_id, new_job_id, cv_result):
    """
    Copy claude_vision data from the new job row into the original job row.
    Also creates an image_split_results row on the original job (with slice bytes)
    and updates the job_row's claude_vision_* denorm columns.
    """
    if not cv_result:
        return False
    with conn.cursor() as cur:
        # Pull the full source row from image_split_results (for left_image/right_image bytes)
        cur.execute("""
            SELECT id, strategy_name, split_x, confidence, processing_ms, metadata,
                   left_image, right_image
            FROM image_split_results
            WHERE job_id = %s AND strategy_name = 'claude_vision'
            LIMIT 1
        """, (new_job_id,))
        src = cur.fetchone()
        if not src:
            return False
        (_, strategy_name, split_x, confidence, processing_ms, metadata, left_img, right_img) = src

        # Insert an equivalent row against the ORIGINAL job
        new_result_id = str(uuid.uuid4())
        cur.execute("""
            INSERT INTO image_split_results
              (id, job_id, strategy_name, split_x, confidence, processing_ms, metadata, left_image, right_image, created_at)
            VALUES (%s, %s, 'claude_vision', %s, %s, %s, %s, %s, %s, NOW())
            ON CONFLICT DO NOTHING
        """, (new_result_id, original_job_id, split_x, confidence, processing_ms,
              Json(metadata) if metadata is not None else None, left_img, right_img))

        # Also pull steel_wall_midpoint's split_x from the ORIGINAL job so we
        # can apply the 1.20.1 disagreement-guard rule when deciding whether
        # claude_vision should become the new best_strategy:
        #   - |cv - steel| <= 50 px → claude_vision wins
        #   - otherwise            → leave best_strategy alone (steel_wall stays)
        cur.execute("""
            SELECT split_x FROM image_split_results
             WHERE job_id = %s AND strategy_name = 'steel_wall_midpoint' LIMIT 1
        """, (original_job_id,))
        sw_row = cur.fetchone()
        sw_split_x = sw_row[0] if sw_row else None

        promote_to_best = (
            sw_split_x is None or abs(split_x - sw_split_x) <= 50
        )

        # Denorm: update the claude_vision_* columns on the job row. And if the
        # disagreement guard passes, promote claude_vision to best_strategy so
        # the Blazor review page's displayed split matches the new primary
        # strategy. This is the fix for the "UI still shows steel_wall answer"
        # bug — previously the backfill only touched the per-strategy result
        # rows, leaving best_strategy frozen at whatever ran at first intake.
        if promote_to_best:
            cur.execute("""
                UPDATE image_split_jobs
                   SET claude_vision_split_x         = %s,
                       claude_vision_confidence      = %s,
                       claude_vision_reasoning       = %s,
                       claude_vision_input_tokens    = %s,
                       claude_vision_output_tokens   = %s,
                       claude_vision_latency_ms      = %s,
                       claude_vision_model           = %s,
                       claude_vision_ran_at          = NOW(),
                       best_strategy                 = 'claude_vision',
                       best_score                    = LEAST(GREATEST(COALESCE(%s, 0.0) + 0.05, 0.0), 1.0),
                       split_x                       = %s
                 WHERE id = %s
                   AND analyst_verdict IS NULL
            """, (
                split_x,
                confidence,
                (metadata or {}).get("reasoning"),
                ((metadata or {}).get("usage") or {}).get("input_tokens"),
                ((metadata or {}).get("usage") or {}).get("output_tokens"),
                processing_ms,
                (metadata or {}).get("model"),
                confidence,
                split_x,
                original_job_id,
            ))
        else:
            cur.execute("""
                UPDATE image_split_jobs
                   SET claude_vision_split_x         = %s,
                       claude_vision_confidence      = %s,
                       claude_vision_reasoning       = %s,
                       claude_vision_input_tokens    = %s,
                       claude_vision_output_tokens   = %s,
                       claude_vision_latency_ms      = %s,
                       claude_vision_model           = %s,
                       claude_vision_ran_at          = NOW()
                 WHERE id = %s
            """, (
            split_x,
            confidence,
            (metadata or {}).get("reasoning"),
            ((metadata or {}).get("usage") or {}).get("input_tokens"),
            ((metadata or {}).get("usage") or {}).get("output_tokens"),
            processing_ms,
            (metadata or {}).get("model"),
            original_job_id,
        ))
    conn.commit()
    return True

def delete_scratch_job(conn, new_job_id):
    """Remove the temp upload job we created for the backfill.

    image_split_assignments has an FK on image_split_results.id, so we must
    clean up the assignment rows first — otherwise the DELETE cascades trip
    on the assignments FK and poison the whole transaction.
    """
    with conn.cursor() as cur:
        cur.execute("""
            DELETE FROM image_split_assignments
             WHERE result_id IN (SELECT id FROM image_split_results WHERE job_id = %s)
        """, (new_job_id,))
        cur.execute("DELETE FROM image_split_assignments WHERE job_id = %s", (new_job_id,))
        cur.execute("DELETE FROM image_split_results WHERE job_id = %s", (new_job_id,))
        cur.execute("DELETE FROM image_split_jobs WHERE id = %s", (new_job_id,))
    conn.commit()

def main():
    conn = psycopg2.connect(DB_CONN)
    targets = fetch_targets(conn)
    print(f"[backfill] {len(targets)} jobs need claude_vision", flush=True)
    ok = 0
    fail = 0
    for i, (job_id, cn, w, h) in enumerate(targets, 1):
        try:
            img = load_image_bytes(conn, job_id)
            if not img:
                print(f"[backfill] {i}/{len(targets)} {str(job_id)[:8]} skip (no image_data)", flush=True)
                continue
            new_id, cv = run_claude_vision_on_bytes(img, cn)
            if cv:
                copied = copy_cv_back(conn, job_id, new_id, cv)
                delete_scratch_job(conn, new_id)
                if copied:
                    ok += 1
                    print(f"[backfill] {i}/{len(targets)} {str(job_id)[:8]} OK split_x={cv['split_x']}", flush=True)
                else:
                    fail += 1
                    print(f"[backfill] {i}/{len(targets)} {str(job_id)[:8]} copy failed", flush=True)
            else:
                fail += 1
                delete_scratch_job(conn, new_id)
                print(f"[backfill] {i}/{len(targets)} {str(job_id)[:8]} no claude_vision result", flush=True)
        except Exception as e:
            fail += 1
            # CRITICAL: psycopg2 leaves the connection in an aborted-transaction
            # state after any failed query — every subsequent statement will
            # raise "current transaction is aborted" until we rollback.
            try:
                conn.rollback()
            except Exception:
                pass
            print(f"[backfill] {i}/{len(targets)} {str(job_id)[:8]} ERROR: {e}", flush=True)
    print(f"[backfill] done. ok={ok} fail={fail}", flush=True)
    conn.close()

if __name__ == "__main__":
    main()
