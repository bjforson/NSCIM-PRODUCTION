"""Export splitter job images to disk for the Claude Code vision test.

Pulls a specific list of job ids (or the first N real ones, excluding test data)
from image_split_jobs and writes each image to C:\\tmp\\splitter_vision_test\\{short}.jpg
with a companion .json metadata file.
"""
import os
import sys
import json
import psycopg2


OUT_DIR = r"C:\tmp\splitter_vision_test"
JOB_IDS = [
    "23a8cae8-d425-4483-9a71-667146368e22",
    "c6c8ef16-870f-453c-8520-61de14f52c4d",
    "a79e6f01-f028-49f0-831d-c9d1f773bba7",
    "4d1450d1-1d40-4289-9854-981a85fceca7",
    "eb36dbef-a61f-4f0a-9765-51e42b494757",
    "0b0a7da9-ad8b-4ec8-bab9-72b099245550",
    "01302853-2a06-468d-bb2e-1b3789833b54",
    "0b6d85f8-d67b-430e-8943-2122e5779685",
    "f9b82075-90e5-4549-a481-62814b06d08d",
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    pw = os.environ.get("NICKSCAN_DB_PASSWORD", "")
    conn = psycopg2.connect(host="localhost", dbname="nickscan_production", user="postgres", password=pw)
    cur = conn.cursor()

    exported = []
    for jid in JOB_IDS:
        cur.execute(
            """
            SELECT container_numbers, image_data, image_width, image_height, split_x, best_strategy
            FROM image_split_jobs WHERE id = %s
            """,
            (jid,),
        )
        row = cur.fetchone()
        if not row:
            print(f"MISSING: {jid}")
            continue
        containers, img_bytes, w, h, current_split, strategy = row
        if not img_bytes:
            print(f"NO IMAGE: {jid}")
            continue

        short = jid[:8]
        img_path = os.path.join(OUT_DIR, f"{short}.jpg")
        meta_path = os.path.join(OUT_DIR, f"{short}.json")
        with open(img_path, "wb") as f:
            f.write(bytes(img_bytes))
        meta = {
            "job_id": jid,
            "container_numbers": containers,
            "image_width": w,
            "image_height": h,
            "current_split_x": current_split,
            "current_strategy": strategy,
        }
        with open(meta_path, "w") as f:
            json.dump(meta, f, indent=2)
        exported.append(meta)
        print(f"  OK  {short}: {containers} {w}x{h} current={current_split}")

    conn.close()
    print(f"\nExported {len(exported)} images to {OUT_DIR}")

    # Print the list in a format I can copy into tool calls
    print("\n--- Copy-paste list of absolute paths ---")
    for m in exported:
        short = m["job_id"][:8]
        print(f"{OUT_DIR}\\{short}.jpg  {m['container_numbers']}  {m['image_width']}x{m['image_height']}  current_split={m['current_split_x']}")


if __name__ == "__main__":
    main()
