"""
Backfill BOEDocuments from RawJsonData.
Re-extracts ALL fields from stored raw JSON to fill gaps.
"""
import json
import psycopg2
import os

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_downloads', user='postgres', password=pw)
conn.autocommit = False

# Text fields from ManifestDetails
MANIFEST_FIELDS = {
    'rotationnumber': ['RotationNumber'],
    'consigneename': ['ConsigneeName'],
    'countryoforigin': ['CountryofOrigin', 'CountryOfOrigin'],
    'marksnumbers': ['MarksNumbers', 'MarksNumber'],
    'shippername': ['ShipperName'],
    'shipperaddress': ['ShipperAddress'],
    'blnumber': ['BLNumber', 'BlNumber', 'BL_NUMBER'],
    'deliveryplace': ['DeliveryPlace'],
    'housebl': ['HouseBL', 'HouseBl', 'HOUSE_BL'],
    'consigneeaddress': ['ConsigneeAddress'],
    'goodsdescription': ['GoodsDescription'],
    'masterblnumber': ['MasterBLNumber', 'MasterBlNumber'],
}

# Text fields from Header
HEADER_TEXT_FIELDS = {
    'clearancetype': ['ClearanceType', 'CLEARANCETYPE'],
    'impname': ['ImpName'],
    'crmslevel': ['CRMSLevel', 'CrmsLevel'],
    'expaddress': ['ExpAddress'],
    'declarationnumber': ['DeclarationNumber'],
    'regimecode': ['RegimeCode'],
    'compoffremarks': ['CompOffRemarks'],
    'declarantname': ['DeclarantName'],
    'expname': ['ExpName'],
    'impaddress': ['ImpAddress'],
    'impexpname': ['ImpExpName'],
    'ccvrintelremarks': ['CCVRIntelRemarks'],
    'impexpaddress': ['ImpExpAddress'],
    'declarationdate': ['DeclarationDate'],
    'declarantaddress': ['DeclarantAddress'],
}


def get_val(obj, keys):
    if not obj or not isinstance(obj, dict):
        return None
    for k in keys:
        v = obj.get(k)
        if v is not None and str(v).strip():
            return str(v).strip()
    return None


def parse_raw(raw):
    try:
        data = json.loads(raw)
    except:
        return None, None, None
    if 'CompleteDocument' in data:
        try:
            complete = json.loads(data['CompleteDocument'])
            return complete, complete.get('Header') or {}, complete.get('ManifestDetails')
        except:
            pass
    return data, data.get('Header') or {}, data.get('ManifestDetails')


cur = conn.cursor()
cur.execute("SELECT COUNT(*) FROM boedocuments WHERE rawjsondata IS NOT NULL AND rawjsondata != ''")
total = cur.fetchone()[0]
print(f"Total records: {total}")

BATCH = 500
offset = 0
updated = 0
errors = 0

while offset < total:
    cur.execute("""
        SELECT id, rawjsondata FROM boedocuments
        WHERE rawjsondata IS NOT NULL AND rawjsondata != ''
        ORDER BY id LIMIT %s OFFSET %s
    """, (BATCH, offset))
    rows = cur.fetchall()
    if not rows:
        break

    batch_count = 0
    for rec_id, raw in rows:
        try:
            complete, header, manifest = parse_raw(raw)
            if not complete:
                continue

            sets = []
            vals = []

            # Header text fields
            for col, keys in HEADER_TEXT_FIELDS.items():
                v = get_val(header, keys)
                if v:
                    sets.append(f"{col} = CASE WHEN {col} IS NULL OR {col} = '' THEN %s ELSE {col} END")
                    vals.append(v)

            # Numeric header fields — handle separately
            duty = header.get('TotalDutyPaid') if header else None
            if duty is not None:
                try:
                    duty_f = float(duty)
                    sets.append("totaldutypaid = CASE WHEN totaldutypaid IS NULL THEN %s ELSE totaldutypaid END")
                    vals.append(duty_f)
                except:
                    pass

            noc = header.get('NoofContainers') or header.get('NoOfContainers') if header else None
            if noc is not None:
                try:
                    noc_i = int(noc)
                    sets.append("noofcontainers = CASE WHEN noofcontainers IS NULL THEN %s ELSE noofcontainers END")
                    vals.append(noc_i)
                except:
                    pass

            dv = header.get('DeclarationVersion') if header else None
            if dv is not None:
                try:
                    dv_i = int(dv)
                    sets.append("declarationversion = CASE WHEN declarationversion IS NULL THEN %s ELSE declarationversion END")
                    vals.append(dv_i)
                except:
                    pass

            # ManifestDetails text fields
            if manifest and isinstance(manifest, dict):
                for col, keys in MANIFEST_FIELDS.items():
                    v = get_val(manifest, keys)
                    if v:
                        sets.append(f"{col} = CASE WHEN {col} IS NULL OR {col} = '' THEN %s ELSE {col} END")
                        vals.append(v)

            # CountryOfOrigin fallback from ManifestItems
            if not get_val(manifest, ['CountryofOrigin', 'CountryOfOrigin']) if manifest else True:
                items = complete.get('ManifestItems', [])
                if items and isinstance(items, list) and len(items) > 0:
                    co = items[0].get('COUNTRYOFORIGIN') or items[0].get('CountryOfOrigin')
                    if co and str(co).strip():
                        sets.append("countryoforigin = CASE WHEN countryoforigin IS NULL OR countryoforigin = '' THEN %s ELSE countryoforigin END")
                        vals.append(str(co).strip())

            if not sets:
                continue

            vals.append(rec_id)
            sql = f"UPDATE boedocuments SET {', '.join(sets)} WHERE id = %s"
            cur.execute(sql, vals)
            batch_count += 1

        except Exception as e:
            conn.rollback()
            errors += 1
            if errors <= 10:
                print(f"  Error id={rec_id}: {e}")
            # Start fresh transaction
            continue

    conn.commit()
    updated += batch_count
    offset += BATCH
    pct = min(100, offset / total * 100)
    print(f"  {offset}/{total} ({pct:.0f}%) — batch: {batch_count}, total updated: {updated}, errors: {errors}")

print(f"\n{'='*60}")
print(f"BACKFILL COMPLETE")
print(f"  Records processed: {total}")
print(f"  Records updated: {updated}")
print(f"  Errors: {errors}")
print(f"{'='*60}")

# Verify
print(f"\n--- Post-backfill gaps ---")
cur2 = conn.cursor()
for col in ['clearancetype', 'deliveryplace', 'blnumber', 'rotationnumber',
            'consigneename', 'countryoforigin', 'masterblnumber', 'shippername']:
    cur2.execute(f"SELECT COUNT(*) FROM boedocuments WHERE {col} IS NULL OR {col} = ''")
    n = cur2.fetchone()[0]
    cur2.execute("SELECT COUNT(*) FROM boedocuments")
    t = cur2.fetchone()[0]
    print(f"  {col:<25} null: {n:>6} ({n/t*100:.1f}%)")
conn.close()
