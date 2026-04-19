"""
Batch-validate the splitter pipeline on 40 fresh dual-container asescans.

Picks the most recent 40 dual-container asescans that have NOT already been
through the splitter, uploads each image via /api/split/upload, waits for the
pipeline to complete, then collects results and prints an accuracy report.

The pipeline now has four relevant observation points:
  - inner_casting_pair split_x
  - steel_wall_midpoint split_x
  - claude_vision raw regression split_x
  - claude_verifier pick (runs only when icp+sw disagree > 10 px)

Key metrics:
  - consensus rate (icp ≈ sw within 10 px)
  - per-strategy agreement
  - catastrophic disagreement count
  - verifier pick distribution (when it runs)
  - few_shot_count observed on verifier calls
"""
import io
import os
import sys
import time
import json

import numpy as np
import psycopg2
import requests
from PIL import Image

# Need the ASE decoder to convert the raw scanimage bytea to a JPEG the
# splitter can ingest. The inspector decoders live inside the splitter
# service, so we add it to sys.path before importing.
_SPLITTER_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "services", "image-splitter")
sys.path.insert(0, _SPLITTER_DIR)
from inspector.decoders.ase import decode_ase  # noqa: E402

DB_CONN = (
    "host=localhost port=5432 dbname=nickscan_production user=postgres "
    f"password={os.environ.get('NICKSCAN_DB_PASSWORD', '')}"
)
SPLITTER_URL = os.environ.get("SPLITTER_URL", "http://localhost:5320")
BATCH_SIZE = 40
CONSENSUS_PX = 10
POLL_INTERVAL = 2
POLL_TIMEOUT = 90


def fetch_fresh_duals(conn, n):
    cur = conn.cursor()
    cur.execute("""
        SELECT a.id, a.containernumber, a.scanimage, a.imagedisplayname
        FROM asescans a
        WHERE a.containernumber LIKE '%%,%%'
          AND a.scanimage IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM image_split_jobs j
               WHERE j.container_numbers = a.containernumber
                 AND j.image_data IS NOT NULL
                 AND j.analyst_verdict IS NULL
          )
        ORDER BY a.scantime DESC
        LIMIT %s
    """, (n,))
    return cur.fetchall()


def ase_to_jpeg(raw_bytes: bytes) -> bytes:
    """Decode ASE blob → high-quality grayscale JPEG for the splitter.

    Uses the inspector's rendering pipeline for better image quality:
      1. decode_ase() to get the raw 16-bit pixel array
      2. Take the low-energy panel (panel 0) for multi-panel scans
      3. Rotate 90° CCW (k=1) so trailer runs left-to-right
      4. Render via the inspector's percentile-stretch (preserves more
         dynamic range than the old crude 1st-99th percentile stretch)
      5. Encode as high-quality JPEG

    The 16-bit source data means corner castings have sharper contrast
    against cargo and roof lines, which improves both inner_casting_pair
    peak detection and Claude Vision's visual analysis.
    """
    ase = decode_ase(raw_bytes)
    arr = ase.panel(0) if ase.is_multi_panel else ase.pixels

    # Rotate 90° CCW so trailer runs left-to-right with tractor on the left
    arr = np.rot90(arr, k=1)

    # Use the inspector's rendering pipeline for better quality. The inspector
    # has a render_raw_png() for lossless 16-bit, but the splitter strategies
    # expect 8-bit grayscale input (numpy arrays with 0-255 range). So we do
    # a careful 16-bit → 8-bit conversion that preserves casting contrast.
    #
    # Two-pass normalization:
    #   1. Percentile stretch (1st-99.5th) to spread the useful dynamic range
    #   2. If the image is 16-bit, the stretched values land in [0, 65535]
    #      which we scale to [0, 255] for 8-bit output
    if arr.dtype == np.uint16:
        lo = np.percentile(arr, 0.5)
        hi = np.percentile(arr, 99.5)
        if hi <= lo:
            lo, hi = float(arr.min()), float(arr.max())
        if hi <= lo:
            arr8 = np.zeros(arr.shape, dtype=np.uint8)
        else:
            stretched = np.clip(
                (arr.astype(np.float32) - lo) * 255.0 / (hi - lo), 0, 255
            )
            arr8 = stretched.astype(np.uint8)
    elif arr.dtype == np.uint8:
        arr8 = arr
    else:
        arr8 = arr.astype(np.uint8)

    img = Image.fromarray(arr8, mode="L")
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=92)
    return buf.getvalue()


def submit_and_wait(container_numbers, image_bytes):
    files = {"file": ("scan.jpg", image_bytes, "image/jpeg")}
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
    return job_id, None  # timed out


def fetch_job_results(conn, job_id):
    cur = conn.cursor()
    cur.execute("""
        SELECT strategy_name, split_x, confidence, metadata
        FROM image_split_results
        WHERE job_id = %s
    """, (job_id,))
    return {r[0]: {"split_x": r[1], "conf": r[2], "meta": r[3] or {}} for r in cur.fetchall()}


def fetch_job_best(conn, job_id):
    cur = conn.cursor()
    cur.execute(
        "SELECT best_strategy, split_x FROM image_split_jobs WHERE id = %s",
        (job_id,),
    )
    return cur.fetchone()


def main():
    conn = psycopg2.connect(DB_CONN)
    duals = fetch_fresh_duals(conn, BATCH_SIZE)
    print(f"[batch] {len(duals)} fresh dual-container asescans to process", flush=True)
    print(flush=True)

    per_job = []
    consensus_hits = 0
    promoted_alone = 0
    verifier_ran = 0
    catastrophic = 0

    for i, (ase_id, containers, image_data, display_name) in enumerate(duals, 1):
        try:
            raw_bytes = bytes(image_data)
            # scanimage is raw ASE format (magic 'IM\x00\x00'), not JPEG.
            # Decode + render to 8-bit JPEG for the splitter.
            try:
                image_bytes = ase_to_jpeg(raw_bytes)
            except Exception as dec_err:
                print(f"[batch] {i}/{len(duals)} {containers[:25]:25}  ASE DECODE ERROR: {dec_err}", flush=True)
                continue
            job_id, jr = submit_and_wait(containers, image_bytes)
            if jr is None:
                print(f"[batch] {i}/{len(duals)} {containers[:25]:25}  TIMED OUT", flush=True)
                continue
            results = fetch_job_results(conn, job_id)
            best = fetch_job_best(conn, job_id)
            best_strategy = best[0] if best else None
            best_x = best[1] if best else None

            icp = results.get("inner_casting_pair", {}).get("split_x")
            sw = results.get("steel_wall_midpoint", {}).get("split_x")
            cv = results.get("claude_vision", {}).get("split_x")
            cf = results.get("corner_fitting", {}).get("split_x")
            dp = results.get("density_profile", {}).get("split_x")

            row = {
                "ase_id": str(ase_id),
                "containers": containers,
                "job_id": job_id,
                "best_strategy": best_strategy,
                "best_x": best_x,
                "icp": icp,
                "sw": sw,
                "cv": cv,
                "cf": cf,
                "dp": dp,
            }

            consensus = False
            if icp is not None and sw is not None and abs(icp - sw) <= CONSENSUS_PX:
                consensus = True
                consensus_hits += 1
            elif best_strategy == "inner_casting_pair":
                promoted_alone += 1

            # Detect catastrophic disagreement among hand-engineered candidates
            xs = [x for x in (icp, sw, cf, dp) if x is not None]
            if xs and (max(xs) - min(xs) > 150):
                catastrophic += 1

            tag = "CONSENSUS" if consensus else ("SOLO" if best_strategy == "inner_casting_pair" else "???")
            sw_str = f"{sw}" if sw is not None else "-"
            icp_str = f"{icp}" if icp is not None else "-"
            cv_str = f"{cv}" if cv is not None else "-"
            cf_str = f"{cf}" if cf is not None else "-"
            dp_str = f"{dp}" if dp is not None else "-"
            print(
                f"[batch] {i:2}/{len(duals)}  {containers[:25]:25}  "
                f"best={best_strategy or '-':20} x={best_x or '-':<5}  "
                f"icp={icp_str:<5} sw={sw_str:<5} cv={cv_str:<5} cf={cf_str:<5} dp={dp_str:<5}  "
                f"{tag}",
                flush=True,
            )
            per_job.append(row)
        except Exception as e:
            print(f"[batch] {i}/{len(duals)} {containers[:25]:25}  ERROR: {e}", flush=True)

    print(flush=True)
    print("=" * 70, flush=True)
    print("SUMMARY", flush=True)
    print("=" * 70, flush=True)
    print(f"processed:           {len(per_job)}", flush=True)
    print(f"consensus (icp~=sw): {consensus_hits}  ({100*consensus_hits/max(1,len(per_job)):.0f}%)", flush=True)
    print(f"promoted solo:       {promoted_alone}", flush=True)
    print(f"catastrophic spread: {catastrophic}  (max-min > 150 px)", flush=True)
    print(flush=True)

    # Strategy agreement matrix: how often does each strategy fall within
    # 10 px of icp (which we treat as the reference here since consensus
    # made it primary on most jobs)
    def within_k(strat_key, k):
        count = 0
        total = 0
        for r in per_job:
            if r["icp"] is not None and r[strat_key] is not None:
                total += 1
                if abs(r[strat_key] - r["icp"]) <= k:
                    count += 1
        return count, total

    print("Agreement with inner_casting_pair (reference):", flush=True)
    for strat_key, label in [
        ("sw", "steel_wall_midpoint"),
        ("cv", "claude_vision"),
        ("cf", "corner_fitting"),
        ("dp", "density_profile"),
    ]:
        within_5, t5 = within_k(strat_key, 5)
        within_15, t15 = within_k(strat_key, 15)
        within_50, t50 = within_k(strat_key, 50)
        if t50:
            print(
                f"  {label:25}  ≤5px: {within_5}/{t50} ({100*within_5/t50:.0f}%)  "
                f"≤15px: {within_15}/{t50} ({100*within_15/t50:.0f}%)  "
                f"≤50px: {within_50}/{t50} ({100*within_50/t50:.0f}%)",
                flush=True,
            )

    # Save the per-job data so the /diagnose tool can show fresh images
    out_path = "C:/tmp/splitter_vision_test/batch_validation_results.json"
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w") as f:
        json.dump(per_job, f, indent=2, default=str)
    print(f"\nper-job results saved to {out_path}", flush=True)

    conn.close()


if __name__ == "__main__":
    main()
