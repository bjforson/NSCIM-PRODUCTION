"""
Re-run the Claude Vision verifier on all pending jobs.

Now that the orchestrator always calls the verifier (no consensus override),
this script triggers a re-evaluation of each job through the /verify-candidates
endpoint and updates the best_strategy + split_x on the job row.

Runs in-process: hits the splitter HTTP API for the verifier call, then
writes the result directly to the DB.
"""
import os
import sys
import json
import time
import requests
import psycopg2

DB_CONN = (
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD', '')}"
)
SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")


def main():
    conn = psycopg2.connect(DB_CONN)
    cur = conn.cursor()
    cur.execute("""
        SELECT id, container_numbers, split_x, best_strategy
        FROM image_split_jobs
        WHERE analyst_verdict IS NULL
          AND container_numbers <> 'TEST001,TEST002'
          AND image_data IS NOT NULL
        ORDER BY created_at DESC
    """)
    jobs = cur.fetchall()
    print(f"[verifier_backfill] {len(jobs)} jobs to re-evaluate", flush=True)

    ok = 0
    fail = 0
    changed = 0

    for i, (job_id, containers, old_split, old_best) in enumerate(jobs, 1):
        try:
            r = requests.post(
                f"{SPLITTER_URL}/api/split/{job_id}/verify-candidates",
                timeout=90,
            )
            if r.status_code != 200:
                print(
                    f"[verifier_backfill] {i}/{len(jobs)} {str(job_id)[:8]} "
                    f"HTTP {r.status_code}: {r.text[:100]}",
                    flush=True,
                )
                fail += 1
                continue

            data = r.json()
            new_strategy = data.get("picked_strategy")
            new_split = data.get("picked_split_x")
            reasoning = data.get("reasoning", "")[:200]
            few_shot = data.get("few_shot_count", 0)

            if new_strategy and new_split is not None:
                cur.execute("""
                    UPDATE image_split_jobs
                       SET best_strategy = %s,
                           split_x       = %s,
                           best_score    = 1.0
                     WHERE id = %s
                       AND analyst_verdict IS NULL
                """, (new_strategy, new_split, str(job_id)))
                conn.commit()

                did_change = (new_strategy != old_best or new_split != old_split)
                if did_change:
                    changed += 1

                tag = "CHANGED" if did_change else "same"
                print(
                    f"[verifier_backfill] {i}/{len(jobs)} {str(job_id)[:8]} "
                    f"{old_best}@{old_split} -> {new_strategy}@{new_split} "
                    f"fs={few_shot} {tag}",
                    flush=True,
                )
                ok += 1
            else:
                print(
                    f"[verifier_backfill] {i}/{len(jobs)} {str(job_id)[:8]} "
                    f"no pick returned",
                    flush=True,
                )
                fail += 1
        except Exception as e:
            fail += 1
            try:
                conn.rollback()
            except Exception:
                pass
            print(
                f"[verifier_backfill] {i}/{len(jobs)} {str(job_id)[:8]} "
                f"ERROR: {e}",
                flush=True,
            )

    print(flush=True)
    print(
        f"[verifier_backfill] done. ok={ok} fail={fail} changed={changed}",
        flush=True,
    )
    conn.close()


if __name__ == "__main__":
    main()
