"""
Backfill inner_casting_pair across all pending splitter jobs.

Loads each job's image bytes directly from nickscan_production, runs the new
CV detector in-process, writes an image_split_results row, and applies the
1.20.x consensus override: if inner_casting_pair and steel_wall_midpoint
agree within 10 px, promote inner_casting_pair to best_strategy.

No HTTP round-trip, no scratch jobs. Runs in seconds for 50+ jobs.
"""
import asyncio
import io
import os
import sys
import uuid

import numpy as np
import psycopg2
from psycopg2.extras import Json
from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "services", "image-splitter"))
from strategies.inner_casting_detector import InnerCastingPairStrategy  # noqa: E402

DB_CONN = (
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD', '')}"
)
CONSENSUS_PX = 10


async def main():
    conn = psycopg2.connect(DB_CONN)
    cur = conn.cursor()
    cur.execute("""
        SELECT id, image_data, image_width, image_height
        FROM image_split_jobs
        WHERE image_data IS NOT NULL
          AND container_numbers <> 'TEST001,TEST002'
          AND analyst_verdict IS NULL
        ORDER BY created_at DESC
    """)
    jobs = cur.fetchall()
    print(f"[icp_backfill] {len(jobs)} pending jobs to process", flush=True)

    strategy = InnerCastingPairStrategy()
    ok = 0
    no_result = 0
    consensus_hits = 0
    promoted_alone = 0

    for i, (job_id, image_data, w, h) in enumerate(jobs, 1):
        try:
            image_bytes = bytes(image_data)
            img = Image.open(io.BytesIO(image_bytes))
            if img.mode != "RGB":
                img = img.convert("RGB")
            arr = np.array(img)
            result = await strategy.analyze(image_bytes, arr)
            if result is None:
                print(f"[icp_backfill] {i}/{len(jobs)} {str(job_id)[:8]}  NO RESULT", flush=True)
                no_result += 1
                continue

            # Skip if an icp result already exists for this job (idempotent)
            cur.execute("""
                SELECT id FROM image_split_results
                 WHERE job_id = %s AND strategy_name = 'inner_casting_pair'
                 LIMIT 1
            """, (str(job_id),))
            existing = cur.fetchone()
            if existing:
                # Update in place
                cur.execute("""
                    UPDATE image_split_results
                       SET split_x    = %s,
                           confidence = %s,
                           processing_ms = %s,
                           metadata   = %s
                     WHERE id = %s
                """, (
                    result.split_x,
                    result.confidence,
                    result.processing_ms,
                    Json(result.metadata),
                    existing[0],
                ))
            else:
                cur.execute("""
                    INSERT INTO image_split_results
                      (id, job_id, strategy_name, split_x, confidence, processing_ms, metadata, created_at)
                    VALUES (%s, %s, 'inner_casting_pair', %s, %s, %s, %s, NOW())
                """, (
                    str(uuid.uuid4()),
                    str(job_id),
                    result.split_x,
                    result.confidence,
                    result.processing_ms,
                    Json(result.metadata),
                ))

            # Consensus check with steel_wall_midpoint
            cur.execute("""
                SELECT split_x FROM image_split_results
                 WHERE job_id = %s AND strategy_name = 'steel_wall_midpoint' LIMIT 1
            """, (str(job_id),))
            sw_row = cur.fetchone()
            consensus = False
            if sw_row is not None:
                sw_x = sw_row[0]
                if abs(result.split_x - sw_x) <= CONSENSUS_PX:
                    consensus = True

            # Apply the promotion rules from the orchestrator
            if consensus:
                cur.execute("""
                    UPDATE image_split_jobs
                       SET best_strategy = 'inner_casting_pair',
                           split_x       = %s,
                           best_score    = 1.0
                     WHERE id = %s
                       AND analyst_verdict IS NULL
                """, (result.split_x, str(job_id)))
                consensus_hits += 1
                tag = "CONSENSUS"
            elif result.confidence >= 0.6:
                cur.execute("""
                    UPDATE image_split_jobs
                       SET best_strategy = 'inner_casting_pair',
                           split_x       = %s,
                           best_score    = 0.99
                     WHERE id = %s
                       AND analyst_verdict IS NULL
                """, (result.split_x, str(job_id)))
                promoted_alone += 1
                tag = "SOLO    "
            else:
                tag = "weak    "

            conn.commit()
            ok += 1
            sw_info = f"sw={sw_row[0] if sw_row else '-'}"
            print(
                f"[icp_backfill] {i}/{len(jobs)} {str(job_id)[:8]}  "
                f"split={result.split_x:4}  conf={result.confidence:.3f}  "
                f"{sw_info:10}  {tag}",
                flush=True,
            )
        except Exception as e:
            conn.rollback()
            print(f"[icp_backfill] {i}/{len(jobs)} {str(job_id)[:8]}  ERROR: {e}", flush=True)

    print(flush=True)
    print(f"[icp_backfill] done.  ok={ok}  no_result={no_result}  "
          f"consensus={consensus_hits}  promoted_alone={promoted_alone}", flush=True)
    conn.close()


if __name__ == "__main__":
    asyncio.run(main())
