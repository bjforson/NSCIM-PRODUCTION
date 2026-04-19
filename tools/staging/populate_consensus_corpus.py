"""
Populate splitter_consensus_corpus from existing consensus matches.

For every image_split_jobs row where:
  - inner_casting_pair and steel_wall_midpoint both have results
  - their split_x values agree within CONSENSUS_PX
  - the job has not already been added to the corpus
  - the image bytes are still stored

create a corpus row with verified_split_x = inner_casting_pair's answer
(the CV detector's peak-refined output is sub-pixel-accurate when consensus
is confirmed), verification_source = 'consensus'.

Also pulls c1_right_casting_x_end and c2_left_casting_x_start from the
inner_casting_pair metadata so the few-shot prompt can show Claude the
exact ground truth in the same structured format it's asked to produce.

Idempotent: a UNIQUE index on source_job_id prevents duplicates; the
INSERT uses ON CONFLICT DO NOTHING.
"""
import os
import sys
import psycopg2
from psycopg2.extras import RealDictCursor

CONSENSUS_PX = 10
DB_CONN = (
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD', '')}"
)

def main():
    conn = psycopg2.connect(DB_CONN)
    cur = conn.cursor(cursor_factory=RealDictCursor)

    # Find all jobs with both icp and sw results
    cur.execute("""
        SELECT
            j.id AS job_id,
            j.image_data,
            j.image_width,
            j.image_height,
            icp.split_x AS icp_x,
            icp.metadata AS icp_metadata,
            sw.split_x AS sw_x
        FROM image_split_jobs j
        JOIN image_split_results icp
          ON icp.job_id = j.id AND icp.strategy_name = 'inner_casting_pair'
        JOIN image_split_results sw
          ON sw.job_id = j.id AND sw.strategy_name = 'steel_wall_midpoint'
        WHERE j.image_data IS NOT NULL
          AND j.container_numbers <> 'TEST001,TEST002'
          AND ABS(icp.split_x - sw.split_x) <= %s
        ORDER BY j.created_at DESC
    """, (CONSENSUS_PX,))
    rows = cur.fetchall()
    print(f"[corpus] {len(rows)} consensus-match candidates", flush=True)

    added = 0
    skipped = 0
    for r in rows:
        icp_meta = r["icp_metadata"] or {}
        delta = abs(r["icp_x"] - r["sw_x"])
        c1_end = icp_meta.get("c1_right_casting_x_end")
        c2_start = icp_meta.get("c2_left_casting_x_start")

        cur.execute("""
            INSERT INTO splitter_consensus_corpus
              (source_job_id, image_data, image_width, image_height,
               icp_split_x, steel_wall_split_x,
               c1_right_casting_x_end, c2_left_casting_x_start,
               verified_split_x, verification_source,
               consensus_delta_px, added_by)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, 'consensus', %s, 'populate_script')
            ON CONFLICT (source_job_id) DO NOTHING
            RETURNING id
        """, (
            r["job_id"],
            bytes(r["image_data"]),
            r["image_width"],
            r["image_height"],
            r["icp_x"],
            r["sw_x"],
            c1_end,
            c2_start,
            r["icp_x"],  # consensus → icp's answer is the verified split
            delta,
        ))
        if cur.fetchone() is not None:
            added += 1
        else:
            skipped += 1
        conn.commit()

    cur.execute("SELECT COUNT(*) AS n FROM splitter_consensus_corpus")
    total = cur.fetchone()["n"]
    print(
        f"[corpus] added={added} skipped={skipped} (already in corpus)  "
        f"total_rows={total}",
        flush=True,
    )
    conn.close()


if __name__ == "__main__":
    main()
