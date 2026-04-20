"""
Phase 2+3 — build the 3D vendor-LUT (class × HE × LE → RGB) from production data.

Strategy:
  * Class: 0..255 (direct index, no bucketing)
  * HE:    32 buckets (each 2048 wide, covers 0..65535)
  * LE:    32 buckets (same)
  → 256 * 32 * 32 = 262,144 cells × 3 bytes = 768 KB LUT

For each scan, decode raw channels + vendor JPEG, then for each pixel:
  sum_rgb[class, he_bucket, le_bucket]  += (R, G, B)
  count[class, he_bucket, le_bucket]    += 1

After aggregation → mean_rgb = sum_rgb / count for populated cells.
Sparse cells (count < MIN_SAMPLES) get filled by nearest-neighbour in
HE×LE space using only populated cells within the same class.

Output:
  vendor_lut_v1.npz  — float mean_rgb [256, 32, 32, 3], uint32 count [256, 32, 32]
  vendor_lut_v1.bin  — C#-ready: uint8 rgb [256, 32, 32, 3] (raw bytes, C-order)
  coverage_report.txt — how many cells have sufficient samples

Scan selection: random sample, separate training/holdout split 80/20.
"""
import io
import os
import struct
import sys
import time
import psycopg2
from PIL import Image
import numpy as np

NUM_CLASSES = 256
HE_BUCKETS = 32
LE_BUCKETS = 32
SCAN_LIMIT = 80         # ~60 training, ~20 holdout
MIN_SAMPLES = 20        # cells with fewer samples treated as missing
HOLDOUT_FRAC = 0.20

pwd = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', port=5432, dbname='nickscan_production',
                        user='postgres', password=pwd)
cur = conn.cursor()


def decode_img(data):
    w = struct.unpack_from('>H', data, 2)[0]
    h = struct.unpack_from('>H', data, 4)[0]
    bd = struct.unpack_from('>H', data, 14)[0]
    payload = data[36:]
    if bd == 16:
        arr = np.frombuffer(payload[:w * h * 2], dtype='>u2').reshape(h, w).astype(np.uint16)
        return np.ascontiguousarray(arr[::-1, :])
    arr = np.frombuffer(payload[:w * h], dtype=np.uint8).reshape(h, w)
    return np.ascontiguousarray(arr[::-1, :])


def bucket16(arr, nbuckets):
    """Map uint16 → bucket 0..nbuckets-1."""
    return np.minimum((arr.astype(np.int32) * nbuckets) >> 16, nbuckets - 1).astype(np.uint8)


# --- Select scans
cur.execute("""
    WITH s4 AS (
        SELECT s.id, s.containernumber
        FROM fs6000scans s
        WHERE EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='HighEnergy')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='LowEnergy')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='Material')
          AND EXISTS(SELECT 1 FROM fs6000images WHERE scanid=s.id AND imagetype='Main')
        ORDER BY RANDOM()
        LIMIT %s
    )
    SELECT id, containernumber FROM s4
""", (SCAN_LIMIT,))
all_scans = cur.fetchall()
holdout = all_scans[:int(SCAN_LIMIT * HOLDOUT_FRAC)]
training = all_scans[int(SCAN_LIMIT * HOLDOUT_FRAC):]
print(f'[phase2] training={len(training)} scans, holdout={len(holdout)} scans')

# --- Accumulators (int64 avoids overflow on sum)
sum_rgb = np.zeros((NUM_CLASSES, HE_BUCKETS, LE_BUCKETS, 3), dtype=np.int64)
count = np.zeros((NUM_CLASSES, HE_BUCKETS, LE_BUCKETS), dtype=np.int64)

t0 = time.time()
for n, (scan_id, cn) in enumerate(training, start=1):
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
        he = decode_img(blobs['HighEnergy'])
        le = decode_img(blobs['LowEnergy'])
        mat = decode_img(blobs['Material'])
        img = Image.open(io.BytesIO(blobs['Main'])).convert('RGB')
        raw_w = struct.unpack_from('>H', blobs['HighEnergy'], 2)[0]
        raw_h = struct.unpack_from('>H', blobs['HighEnergy'], 4)[0]
        if img.size != (raw_w, raw_h):
            img = img.resize((raw_w, raw_h), Image.BILINEAR)
        vendor = np.array(img)

        he_b = bucket16(he.reshape(-1), HE_BUCKETS)
        le_b = bucket16(le.reshape(-1), LE_BUCKETS)
        mat_f = mat.reshape(-1)
        vend_f = vendor.reshape(-1, 3).astype(np.int64)

        # Vectorized aggregation using flat indexing:
        #   flat_idx = class * (HE_BUCKETS * LE_BUCKETS) + he_b * LE_BUCKETS + le_b
        # np.bincount does the counting and weighted sums in O(N).
        flat_idx = (mat_f.astype(np.int64) * HE_BUCKETS * LE_BUCKETS
                    + he_b.astype(np.int64) * LE_BUCKETS
                    + le_b.astype(np.int64))
        total_cells = NUM_CLASSES * HE_BUCKETS * LE_BUCKETS
        cnt_local = np.bincount(flat_idx, minlength=total_cells).reshape(
            NUM_CLASSES, HE_BUCKETS, LE_BUCKETS)
        count += cnt_local.astype(np.int64)
        for ch in range(3):
            sum_rgb[..., ch] += np.bincount(
                flat_idx,
                weights=vend_f[:, ch].astype(np.float64),
                minlength=total_cells
            ).astype(np.int64).reshape(NUM_CLASSES, HE_BUCKETS, LE_BUCKETS)

        if n % 10 == 0 or n == len(training):
            dt = time.time() - t0
            print(f'  [{n}/{len(training)}] {cn}  ({dt:.1f}s elapsed, {count.sum() / 1e9:.2f} gigapixels total)')
    except Exception as e:
        print(f'  ERROR on {cn}: {e}')

# --- Compute mean RGB where count >= MIN_SAMPLES
print()
print('[phase3] computing LUT means...')
valid = count >= MIN_SAMPLES
mean_rgb = np.zeros_like(sum_rgb, dtype=np.float32)
# Safe divide
counts_3 = np.repeat(count[..., None], 3, axis=-1)
np.divide(sum_rgb, counts_3, out=mean_rgb, where=counts_3 > 0)

# Coverage report
total_cells = NUM_CLASSES * HE_BUCKETS * LE_BUCKETS
covered_cells = int(valid.sum())
print(f'Total cells:     {total_cells:,d}')
print(f'Covered cells:   {covered_cells:,d}  ({100.0 * covered_cells / total_cells:.1f}%)')
print(f'Total pixels:    {int(count.sum()):,d}')
print()

# Per-class coverage — which classes are densely vs sparsely covered?
print('Top 25 classes by total pixel count:')
class_counts = count.sum(axis=(1, 2))
top_classes = np.argsort(-class_counts)[:25]
for c in top_classes:
    cls_cov = int(valid[c].sum())
    print(f'  class {c:>3d}: {int(class_counts[c]):>11,d} pixels, {cls_cov:>4d}/{HE_BUCKETS * LE_BUCKETS} cells covered')

# --- Fill sparse cells by nearest-neighbour within the same class
print()
print('[phase3] nearest-neighbour fill for sparse cells...')
filled_rgb = mean_rgb.copy()
fill_count = 0
for c in range(NUM_CLASSES):
    if class_counts[c] < 100:
        # This class barely appears; leave unfilled (fallback will be "grey")
        continue
    valid_c = valid[c]
    if not valid_c.any():
        continue
    if valid_c.all():
        continue
    # Build list of valid (he, le) cells for this class
    valid_coords = np.argwhere(valid_c)  # shape (N, 2) of (he_b, le_b)
    for he_b in range(HE_BUCKETS):
        for le_b in range(LE_BUCKETS):
            if valid_c[he_b, le_b]:
                continue
            # Find nearest valid cell
            d = np.abs(valid_coords - np.array([he_b, le_b])).sum(axis=1)
            nearest = valid_coords[d.argmin()]
            filled_rgb[c, he_b, le_b] = mean_rgb[c, nearest[0], nearest[1]]
            fill_count += 1
print(f'Filled {fill_count:,d} sparse cells by nearest-neighbour.')

# --- Export
out_dir = os.path.dirname(os.path.abspath(__file__))
np.savez(os.path.join(out_dir, 'vendor_lut_v1.npz'),
         mean_rgb=filled_rgb, count=count, valid=valid)
# Binary LUT: uint8 RGB, C-order, shape (256, 32, 32, 3)
lut_u8 = np.clip(np.round(filled_rgb), 0, 255).astype(np.uint8)
with open(os.path.join(out_dir, 'vendor_lut_v1.bin'), 'wb') as f:
    # Header so C# can sanity-check on load
    f.write(b'VLUT')                                       # magic
    f.write(struct.pack('<I', 1))                          # version
    f.write(struct.pack('<III', NUM_CLASSES, HE_BUCKETS, LE_BUCKETS))  # dims
    f.write(lut_u8.tobytes(order='C'))                     # data
print(f'Wrote vendor_lut_v1.bin ({16 + lut_u8.nbytes:,d} bytes)')

# --- Save holdout scan IDs for phase 4
with open(os.path.join(out_dir, 'holdout_scans.txt'), 'w') as f:
    for sid, cn in holdout:
        f.write(f'{sid}\t{cn}\n')
print(f'Wrote holdout_scans.txt ({len(holdout)} entries)')

conn.close()
print()
print('[done]')
