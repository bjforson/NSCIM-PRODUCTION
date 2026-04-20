"""
Phase 4 — validate the learned LUT on held-out scans.

For each holdout scan:
  1. Decode raw HE/LE/Material.
  2. For every pixel, look up RGB in the fitted LUT at (class, he_bucket, le_bucket).
  3. Compare to the vendor's own JPEG at the same pixel.
  4. Compute mean absolute error per channel + distribution.

Also write sample reconstructions to disk so we can eyeball them in Chrome
next to the vendor originals.
"""
import io
import os
import struct
import psycopg2
from PIL import Image
import numpy as np

NUM_CLASSES = 256
HE_BUCKETS = 32
LE_BUCKETS = 32
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

# --- Load the fitted LUT
with open(os.path.join(OUT_DIR, 'vendor_lut_v1.bin'), 'rb') as f:
    magic = f.read(4)
    assert magic == b'VLUT', f'bad magic: {magic!r}'
    version = struct.unpack('<I', f.read(4))[0]
    nc, nh, nl = struct.unpack('<III', f.read(12))
    assert nc == NUM_CLASSES and nh == HE_BUCKETS and nl == LE_BUCKETS, \
        f'dim mismatch: expected ({NUM_CLASSES},{HE_BUCKETS},{LE_BUCKETS}) got ({nc},{nh},{nl})'
    lut = np.frombuffer(f.read(), dtype=np.uint8).reshape(NUM_CLASSES, HE_BUCKETS, LE_BUCKETS, 3)
print(f'[load] LUT shape={lut.shape}, dtype={lut.dtype}')

with open(os.path.join(OUT_DIR, 'holdout_scans.txt')) as f:
    holdout = [line.strip().split('\t') for line in f if line.strip()]
print(f'[load] holdout scans: {len(holdout)}')


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
    return np.minimum((arr.astype(np.int32) * nbuckets) >> 16, nbuckets - 1).astype(np.int32)


pwd = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', port=5432, dbname='nickscan_production',
                        user='postgres', password=pwd)
cur = conn.cursor()

all_errors = {'r': [], 'g': [], 'b': [], 'total': []}
sample_outputs = []  # save a few for visual inspection

for n, (scan_id, cn) in enumerate(holdout):
    blobs = {}
    for t in ('HighEnergy', 'LowEnergy', 'Material', 'Main'):
        cur.execute('SELECT imagedata FROM fs6000images WHERE scanid=%s::uuid AND imagetype=%s',
                    (scan_id, t))
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

        # Apply LUT
        he_b = bucket16(he, HE_BUCKETS)
        le_b = bucket16(le, LE_BUCKETS)
        reconstructed = lut[mat, he_b, le_b]   # fancy indexing → (H, W, 3)

        # Per-channel absolute error
        diff = np.abs(vendor.astype(np.int16) - reconstructed.astype(np.int16))
        err_r = diff[..., 0].mean()
        err_g = diff[..., 1].mean()
        err_b = diff[..., 2].mean()
        err_total = diff.mean()
        all_errors['r'].append(err_r)
        all_errors['g'].append(err_g)
        all_errors['b'].append(err_b)
        all_errors['total'].append(err_total)

        print(f'  [{n + 1}/{len(holdout)}] {cn:20s} err  R={err_r:5.2f}  G={err_g:5.2f}  B={err_b:5.2f}  total={err_total:5.2f}')

        # Save first 3 scans for visual inspection
        if n < 3:
            Image.fromarray(reconstructed.astype(np.uint8)).save(
                os.path.join('C:/temp', f'lut-recon-{cn}.jpg'), quality=88)
            Image.fromarray(vendor.astype(np.uint8)).save(
                os.path.join('C:/temp', f'lut-vendor-{cn}.jpg'), quality=88)
            # Also a diff visualization (scaled up)
            diff_vis = np.clip(diff * 4, 0, 255).astype(np.uint8)
            Image.fromarray(diff_vis).save(
                os.path.join('C:/temp', f'lut-diff-{cn}.jpg'), quality=88)
            sample_outputs.append(cn)
    except Exception as e:
        print(f'  ERROR on {cn}: {e}')

conn.close()

# --- Aggregate
print()
print('=== Aggregate reconstruction error across holdout ===')
for ch in ('r', 'g', 'b', 'total'):
    vals = np.array(all_errors[ch])
    if len(vals) == 0:
        continue
    print(f'  {ch:>5s}:  mean={vals.mean():5.2f}  median={np.median(vals):5.2f}  '
          f'min={vals.min():5.2f}  max={vals.max():5.2f}  n={len(vals)}')

print()
print('Target: total mean < 10 RGB units per channel → acceptable reconstruction')
print(f'Sample reconstructions saved to C:/temp/lut-recon-*.jpg, lut-vendor-*.jpg, lut-diff-*.jpg for: {sample_outputs}')
