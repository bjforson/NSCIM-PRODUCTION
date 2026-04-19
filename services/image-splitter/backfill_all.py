"""
Backfill MasterBLNumber + any remaining gaps from ALL JSON files.
Covers: Backup/BatchData, Archive/BatchData, Archive/ContainerData,
        Backup/ContainerData, Outbox
"""
import json, glob, psycopg2, os
from collections import defaultdict

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_downloads', user='postgres', password=pw)
conn.autocommit = False
cur = conn.cursor()

# Collect ALL JSON file paths
patterns = [
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup\BatchData\*.json',
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads\Archive\BatchData\*.json',
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup\ContainerData\*.json',
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads\Archive\ContainerData\*.json',
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox\*.json',
    r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox\ICUMS\Acknowledged\*.json',
]

all_files = []
for p in patterns:
    found = sorted(glob.glob(p))
    print(f"  {p}: {len(found)} files")
    all_files.extend(found)

print(f"\nTotal JSON files to process: {len(all_files)}")

# Fields to extract from ManifestDetails
MANIFEST_KEYS = {
    'deliveryplace': ['DeliveryPlace'],
    'blnumber': ['BLNumber', 'BlNumber'],
    'rotationnumber': ['RotationNumber'],
    'consigneename': ['ConsigneeName'],
    'shippername': ['ShipperName'],
    'shipperaddress': ['ShipperAddress'],
    'consigneeaddress': ['ConsigneeAddress'],
    'goodsdescription': ['GoodsDescription'],
    'masterblnumber': ['MasterBLNumber', 'MasterBlNumber'],
    'housebl': ['HouseBL', 'HouseBl'],
    'marksnumbers': ['MarksNumbers', 'MarksNumber'],
    'countryoforigin': ['CountryofOrigin', 'CountryOfOrigin'],
}

HEADER_KEYS = {
    'clearancetype': ['ClearanceType'],
}


def get_val(obj, keys):
    if not obj or not isinstance(obj, dict):
        return None
    for k in keys:
        v = obj.get(k)
        if v is not None and str(v).strip():
            return str(v).strip()
    return None


def extract_records(filepath):
    """Extract records from batch or container JSON files."""
    try:
        with open(filepath, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
    except:
        return []

    # Batch format: BOEScanDocument list
    records = data.get('BOEScanDocument', [])
    if records:
        return records

    # Container format: single record with ContainerDetails at top
    if 'ContainerDetails' in data or 'Header' in data:
        return [data]

    # Nested data format
    inner = data.get('Data', {})
    if isinstance(inner, dict):
        recs = inner.get('BoeScanDocuments', inner.get('BOEScanDocument', []))
        if recs:
            return recs

    # Try any list at top level
    for k, v in data.items():
        if isinstance(v, list) and len(v) > 0 and isinstance(v[0], dict):
            return v

    return []


total_records = 0
updated_count = defaultdict(int)
errors = 0
file_count = 0

for fp in all_files:
    file_count += 1
    try:
        records = extract_records(fp)
        batch_updates = 0

        for rec in records:
            total_records += 1
            cd = rec.get('ContainerDetails') or {}
            cn = cd.get('ContainerNumber', '')
            if not cn:
                continue

            header = rec.get('Header') or {}
            md = rec.get('ManifestDetails')

            updates = {}
            vals = []

            # Header fields
            for col, keys in HEADER_KEYS.items():
                v = get_val(header, keys)
                if v:
                    updates[col] = v

            # ManifestDetails fields
            if md and isinstance(md, dict):
                for col, keys in MANIFEST_KEYS.items():
                    v = get_val(md, keys)
                    if v:
                        updates[col] = v

            # CountryOfOrigin fallback from ManifestItems
            if 'countryoforigin' not in updates:
                items = rec.get('ManifestItems', [])
                if items and isinstance(items, list) and len(items) > 0:
                    co = items[0].get('COUNTRYOFORIGIN') or items[0].get('CountryOfOrigin')
                    if co and str(co).strip():
                        updates['countryoforigin'] = str(co).strip()

            if not updates:
                continue

            # Build UPDATE — only fill gaps
            set_parts = []
            param_vals = []
            for col, v in updates.items():
                set_parts.append(f"{col} = CASE WHEN {col} IS NULL OR {col} = '' THEN %s ELSE {col} END")
                param_vals.append(v)

            param_vals.append(cn)
            null_checks = ' OR '.join(f"({col} IS NULL OR {col} = '')" for col in updates)
            sql = f"UPDATE boedocuments SET {', '.join(set_parts)} WHERE containernumber = %s AND ({null_checks})"
            cur.execute(sql, param_vals)
            if cur.rowcount > 0:
                batch_updates += 1
                for col in updates:
                    updated_count[col] += 1

        conn.commit()

    except Exception as e:
        conn.rollback()
        errors += 1
        if errors <= 5:
            print(f"  Error in {os.path.basename(fp)}: {e}")

    if file_count % 100 == 0:
        print(f"  Files: {file_count}/{len(all_files)} — Records: {total_records} — Updates: {sum(updated_count.values())} — Errors: {errors}")

print(f"\n{'='*60}")
print(f"FULL BACKFILL COMPLETE")
print(f"  Files processed: {file_count}")
print(f"  Records scanned: {total_records}")
print(f"  Errors: {errors}")
print(f"\n  Fields updated:")
for col, cnt in sorted(updated_count.items(), key=lambda x: -x[1]):
    print(f"    {col:<25} {cnt:>6}")

# Final verification
print(f"\n--- Final gap check ---")
for col in ['clearancetype', 'deliveryplace', 'blnumber', 'rotationnumber',
            'consigneename', 'countryoforigin', 'masterblnumber', 'shippername']:
    cur.execute(f"SELECT COUNT(*) FROM boedocuments WHERE {col} IS NULL OR {col} = ''")
    n = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM boedocuments")
    t = cur.fetchone()[0]
    print(f"  {col:<25} null: {n:>6} ({n/t*100:.1f}%)")

conn.close()
