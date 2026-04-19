"""
03_diff_raw_vs_decoded.py

Compare the pure-Python prototype decoder output against the vendor DLL's
output for the same scan. We don't call the DLL directly — instead we read
`imagecaches.imagedata`, which is the already-decoded JPEG that the NSCIM
pipeline produced via the DLL and cached by container number.

For each sample that has a cached DLL image:
  - report dimensions & orientation of both
  - save a side-by-side comparison PNG
  - compute a rough similarity (normalized cross-correlation on downsampled
    grayscale after rotating the prototype to match DLL orientation)

Expected observations:
  - DLL output is rotated 90° relative to our raw layout
  - DLL output has width == raw height
  - Content should be visually identical modulo intensity mapping
"""
from __future__ import annotations

import io
import json
import os
import sys
from pathlib import Path

import numpy as np
import psycopg2
from PIL import Image

HERE = Path(__file__).resolve().parent
SAMPLES_DIR = HERE / "samples"
DIFF_DIR = HERE / "diffs"

sys.path.insert(0, str(HERE))
from importlib import import_module
proto = import_module("05_prototype_decoder")


def connect():
    return psycopg2.connect(
        host="localhost",
        port=5432,
        dbname="nickscan_production",
        user="postgres",
        password=os.environ["NICKSCAN_DB_PASSWORD"],
    )


def fetch_dll_cached(conn, container_number: str):
    cur = conn.cursor()
    cur.execute(
        """
        SELECT imagedata, width, height, length(imagedata), scantime
        FROM imagecaches
        WHERE containernumber = %s AND scannertype = 'ASE'
        ORDER BY scantime DESC
        LIMIT 1
        """,
        (container_number,),
    )
    row = cur.fetchone()
    cur.close()
    return row


def to_gray_array(pil_img: Image.Image) -> np.ndarray:
    return np.asarray(pil_img.convert("L"), dtype=np.float32)


def normalized_cross_correlation(a: np.ndarray, b: np.ndarray) -> float:
    a = a - a.mean()
    b = b - b.mean()
    denom = np.sqrt((a * a).sum() * (b * b).sum())
    if denom == 0:
        return 0.0
    return float((a * b).sum() / denom)


def align_and_score(proto_panel: Image.Image, dll_img: Image.Image) -> tuple[float, Image.Image]:
    """
    Search rotations (0, 90, 180, 270) + horizontal flip, scoring at FULL
    resolution after resizing to DLL dimensions. Returns (best_ncc, oriented_proto).

    The prototype already rotates 90° CCW in render_per_channel to match the
    DLL's landscape layout, so the expected winner is rot=0 flip=False.
    """
    target_size = dll_img.size  # (W, H)
    dll_arr = to_gray_array(dll_img)
    best = (-1.0, proto_panel)
    for rot in (0, 90, 180, 270):
        for flip in (False, True):
            img = proto_panel
            if rot:
                img = img.rotate(rot, expand=True)
            if flip:
                img = img.transpose(Image.FLIP_LEFT_RIGHT)
            if img.size != target_size:
                img_scored = img.resize(target_size)
            else:
                img_scored = img
            score = normalized_cross_correlation(to_gray_array(img_scored), dll_arr)
            if score > best[0]:
                best = (score, img_scored)
    return best


def main():
    DIFF_DIR.mkdir(parents=True, exist_ok=True)
    samples = sorted(SAMPLES_DIR.glob("*.ase"))
    if not samples:
        print("No samples found.")
        return

    conn = connect()
    try:
        results = []
        for ase_path in samples:
            meta_path = ase_path.with_suffix(".json")
            if not meta_path.exists():
                continue
            meta = json.loads(meta_path.read_text())
            container = meta.get("container_number")
            if not container:
                continue

            row = fetch_dll_cached(conn, container)
            if not row:
                results.append({"file": ase_path.name, "container": container, "status": "no_dll_cache"})
                continue

            dll_bytes, dll_w, dll_h, dll_len, scantime = row
            dll_img = Image.open(io.BytesIO(bytes(dll_bytes)))
            dll_img.load()

            img = proto.decode(ase_path.read_bytes())
            # For ch=3 the DLL probably picks/composites a specific panel;
            # for ch=2 the full image is already the view.
            # Use render_per_channel and grab the main panel (panel 0 is low-energy).
            panel_imgs = proto.render_per_channel(img, invert=False, rotate_to_dll=True)
            proto_panel = panel_imgs[0]

            score, oriented = align_and_score(proto_panel, dll_img)

            # write side-by-side (oriented proto | dll)
            combined = Image.new("L", (oriented.size[0] + dll_img.size[0], max(oriented.size[1], dll_img.size[1])))
            combined.paste(oriented.convert("L"), (0, 0))
            combined.paste(dll_img.convert("L"), (oriented.size[0], 0))
            out_path = DIFF_DIR / f"{ase_path.stem}.diff.png"
            combined.save(out_path)

            results.append(
                {
                    "file": ase_path.name,
                    "container": container,
                    "status": "ok",
                    "raw_dims": [img.width, img.height],
                    "channels": img.channels,
                    "dll_dims": [dll_img.size[0], dll_img.size[1]],
                    "dll_bytes": dll_len,
                    "ncc_best": round(score, 4),
                    "diff_png": str(out_path.name),
                }
            )
            print(
                f"{ase_path.name[:20]}  container={container}  "
                f"raw={img.width}x{img.height}x{img.channels}  "
                f"dll={dll_img.size[0]}x{dll_img.size[1]}  "
                f"ncc={score:.4f}  -> {out_path.name}"
            )
    finally:
        conn.close()

    (DIFF_DIR / "results.json").write_text(json.dumps(results, indent=2))
    ok = sum(1 for r in results if r.get("status") == "ok")
    print(f"\nCompared {ok}/{len(results)} samples. Results in {DIFF_DIR}")


if __name__ == "__main__":
    main()
