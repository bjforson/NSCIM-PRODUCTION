"""
Phase 1 — does LE matter?

For each (class, HE-bucket) cell that has enough pixels, split into two
sub-groups by LE (below vs. above median LE within the cell). If the
vendor's color depends on LE, the two sub-groups will have different
median RGB. If it depends only on (class, HE), the two sub-groups
will be identical within noise.

Output: per-cell (R, G, B) variance when LE varies. We pick the cells
with the largest LE-driven RGB variance and decide whether a 3D LUT
is needed.
"""
import io
import os
import struct
import sys
import psycopg2
from PIL import Image
import numpy as np

pwd = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', port=5432, dbname='nickscan_production',
                        user='postgres', password=pwd)
cur = conn.cursor()

HE_BUCKETS = 32     # ~2048 HE values per bucket (0..65535)
SCAN_LIMIT = 30


def decode_img(data):
    """Decode an FS6000 .img blob; returns (h, w) numpy array."""
    w = struct.unpack_from('>H', data, 2)[0]
    h = struct.unpack_from('>H', data, 4)[0]
    bd = struct.unpack_from('>H', data, 14)[0]
    payload = data[36:]
    if bd == 16:
        arr = np.frombuffer(payload[:w * h * 2], dtype='>u2').reshape(h, w).astype(np.uint16)
        return np.ascontiguousarray(arr[::-1, :])
    arr = np.frombuffer(payload[:w * h], dtype=np.uint8).reshape(h, w)
    return np.ascontiguousarray(arr[::-1, :])


def he_bucket(he):
    """Map HE 0..65535 → bucket 0..HE_BUCKETS-1."""
    return np.minimum((he.astype(np.int32) * HE_BUCKETS) >> 16, HE_BUCKETS - 1)


cur.execute("""
    WITH scans_with_all AS (
        SELECT s.id, s.containernumber
        FROM fs6000scans s
        WHERE EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='HighEnergy')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='LowEnergy')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='Material')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='Main')
        ORDER BY RANDOM()
        LIMIT %s
    )
    SELECT id, containernumber FROM scans_with_all
""", (SCAN_LIMIT,))
scans = cur.fetchall()
print(f'Analyzing {len(scans)} random scans to assess LE relevance...')

# Per (class, HE-bucket) cell, accumulate:
#   sum_rgb_lo (for pixels with LE below cell median)
#   sum_rgb_hi (for pixels with LE above cell median)
#   counts for each half
# We compute cell-medians streaming is painful; instead collect pixel-level
# samples per cell into small buffers then compute.
#
# Memory budget: target classes that matter — the ones from our earlier LUT
# extraction. We don't need every class, just the most common ones.
TARGET_CLASSES = [0, 7, 55, 65, 70, 80, 100, 131, 134, 137]
SAMPLES_PER_CELL = 5000

# For each (class, he_bucket) we keep a reservoir of (LE, R, G, B) tuples
reservoir = {(c, b): [] for c in TARGET_CLASSES for b in range(HE_BUCKETS)}

for scan_id, cn in scans:
    blobs = {}
    for t in ('HighEnergy', 'LowEnergy', 'Material', 'Main'):
        cur.execute('SELECT imagedata FROM fs6000images WHERE scanid=%s::uuid AND imagetype=%s',
                    (str(scan_id), t))
        r = cur.fetchone()
        if r:
            blobs[t] = bytes(r[0])
    if len(blobs) != 4:
        continue
    try:
        he = decode_img(blobs['HighEnergy']).reshape(-1)
        le = decode_img(blobs['LowEnergy']).reshape(-1)
        mat = decode_img(blobs['Material']).reshape(-1)
        img = Image.open(io.BytesIO(blobs['Main'])).convert('RGB')
        if img.size != (struct.unpack_from('>H', blobs['HighEnergy'], 2)[0],
                        struct.unpack_from('>H', blobs['HighEnergy'], 4)[0]):
            img = img.resize((struct.unpack_from('>H', blobs['HighEnergy'], 2)[0],
                              struct.unpack_from('>H', blobs['HighEnergy'], 4)[0]), Image.BILINEAR)
        vendor = np.array(img).reshape(-1, 3)

        he_b = he_bucket(he)
        for c in TARGET_CLASSES:
            for b in range(HE_BUCKETS):
                mask = (mat == c) & (he_b == b)
                cnt = int(mask.sum())
                if cnt == 0:
                    continue
                # Take up to SAMPLES_PER_CELL - len(existing reservoir) samples
                take = min(cnt, SAMPLES_PER_CELL - len(reservoir[(c, b)]))
                if take <= 0:
                    continue
                idxs = np.where(mask)[0]
                # Random sample without replacement
                if cnt > take:
                    idxs = np.random.choice(idxs, size=take, replace=False)
                for i in idxs:
                    reservoir[(c, b)].append((int(le[i]), int(vendor[i, 0]),
                                              int(vendor[i, 1]), int(vendor[i, 2])))
        print(f'  {cn}: done')
    except Exception as e:
        print(f'  {cn}: ERROR {e}')

conn.close()

# For each cell, split by LE median, compare RGB medians
print()
print('=== LE-driven RGB variance per cell ===')
print(f'{"class":>5s} {"bucket":>6s} {"n":>6s}  '
      f'{"lo RGB":>14s}  {"hi RGB":>14s}  {"|dRGB|":>6s}')
rows = []
for (c, b), pix in reservoir.items():
    if len(pix) < 100:
        continue
    arr = np.array(pix)
    le_med = np.median(arr[:, 0])
    lo_mask = arr[:, 0] <= le_med
    hi_mask = arr[:, 0] > le_med
    if lo_mask.sum() < 20 or hi_mask.sum() < 20:
        continue
    lo_rgb = np.median(arr[lo_mask, 1:4], axis=0).astype(int)
    hi_rgb = np.median(arr[hi_mask, 1:4], axis=0).astype(int)
    drgb = np.abs(hi_rgb - lo_rgb).sum()
    rows.append((c, b, len(pix), lo_rgb, hi_rgb, drgb))

# Sort by LE impact
rows.sort(key=lambda r: -r[5])
for c, b, n, lo_rgb, hi_rgb, drgb in rows[:40]:
    print(f'{c:>5d} {b:>6d} {n:>6d}  ({lo_rgb[0]:>3d},{lo_rgb[1]:>3d},{lo_rgb[2]:>3d})'
          f'  ({hi_rgb[0]:>3d},{hi_rgb[1]:>3d},{hi_rgb[2]:>3d})  {drgb:>6d}')

# Summary statistics: how often does LE split produce a non-trivial RGB difference?
dvals = np.array([r[5] for r in rows])
print()
print(f'Total cells analyzed: {len(dvals)}')
print(f'  |dRGB| <= 5:   {(dvals <= 5).sum()} cells ({100.0 * (dvals <= 5).sum() / len(dvals):.1f}%)')
print(f'  |dRGB| <= 10:  {(dvals <= 10).sum()} cells ({100.0 * (dvals <= 10).sum() / len(dvals):.1f}%)')
print(f'  |dRGB| > 30:   {(dvals > 30).sum()} cells ({100.0 * (dvals > 30).sum() / len(dvals):.1f}%)')
print(f'  max |dRGB|:    {dvals.max()}')
