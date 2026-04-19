"""Comprehensive analysis of ICUMS batch JSON files vs database ingestion."""
import json, glob, os, sys
from collections import defaultdict

files = sorted(glob.glob(r'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup\BatchData\*.json'))
print(f'Total batch files: {len(files)}')

step = max(1, len(files) // 100)
sample = files[::step][:100]
print(f'Sampling {len(sample)} files: {os.path.basename(sample[0])} to {os.path.basename(sample[-1])}')

total = 0
stats = defaultdict(int)
clearance_types = defaultdict(int)
null_manifest_by_clearance = defaultdict(int)
dp_values = defaultdict(int)
manifest_fields = defaultdict(int)
sample_null_manifest = []
sample_null_dp_with_manifest = []

for fp in sample:
    try:
        with open(fp, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        records = data.get('BOEScanDocument', [])
        for rec in records:
            total += 1
            cd = rec.get('ContainerDetails') or {}
            cn = cd.get('ContainerNumber', '')
            header = rec.get('Header') or {}
            ct = header.get('ClearanceType', '')

            # Container type
            is_iso = len(cn) == 11 and cn[:4].isalpha()
            stats['iso'] += int(is_iso)
            stats['vin'] += int(len(cn) >= 15 and not is_iso)

            # Header clearance
            if ct and ct.strip():
                stats['has_ct'] += 1
                clearance_types[ct] += 1
            else:
                stats['null_ct'] += 1

            # ManifestDetails
            md = rec.get('ManifestDetails')
            if md and isinstance(md, dict):
                stats['has_md'] += 1
                for k, v in md.items():
                    if v is not None and str(v).strip():
                        manifest_fields[k] += 1

                dp = md.get('DeliveryPlace', '')
                if dp and dp.strip():
                    stats['has_dp'] += 1
                    dp_values[dp] += 1
                else:
                    stats['null_dp'] += 1
                    if len(sample_null_dp_with_manifest) < 5:
                        sample_null_dp_with_manifest.append({
                            'cn': cn, 'ct': ct, 'decl': header.get('DeclarationNumber'),
                            'md_keys': [k for k,v in md.items() if v], 'iso': is_iso
                        })

                for field, keys in [('bl', ['BLNumber','BlNumber']), ('rotation', ['RotationNumber']),
                                     ('consignee', ['ConsigneeName']), ('origin', ['CountryofOrigin','CountryOfOrigin']),
                                     ('housebl', ['HouseBL','HouseBl'])]:
                    val = None
                    for k in keys:
                        val = md.get(k)
                        if val: break
                    stats[f'has_{field}'] += int(bool(val and str(val).strip()))
                    stats[f'null_{field}'] += int(not (val and str(val).strip()))
            else:
                stats['null_md'] += 1
                if ct:
                    null_manifest_by_clearance[ct] += 1
                if len(sample_null_manifest) < 10:
                    items = rec.get('ManifestItems', [])
                    sample_null_manifest.append({
                        'cn': cn, 'ct': ct, 'decl': header.get('DeclarationNumber'),
                        'iso': is_iso, 'items': len(items) if items else 0,
                        'decl_name': header.get('DeclarantName', ''),
                        'total_duty': header.get('TotalDutyPaid')
                    })
    except Exception as e:
        print(f'Error: {os.path.basename(fp)}: {e}')

print(f'\n{"="*70}')
print(f'ANALYSIS: {total} RECORDS FROM {len(sample)} BATCH FILES')
print(f'{"="*70}')

print(f'\n--- Record Types ---')
print(f'  ISO containers:    {stats["iso"]:>6} ({stats["iso"]/total*100:.1f}%)')
print(f'  VIN/vehicles:      {stats["vin"]:>6} ({stats["vin"]/total*100:.1f}%)')
print(f'  Other:             {total-stats["iso"]-stats["vin"]:>6}')

print(f'\n--- Header.ClearanceType ---')
print(f'  Has value:         {stats["has_ct"]:>6} ({stats["has_ct"]/total*100:.1f}%)')
print(f'  Null:              {stats["null_ct"]:>6}')
for ct, n in sorted(clearance_types.items(), key=lambda x: -x[1]):
    print(f'    {ct}: {n}')

print(f'\n--- ManifestDetails ---')
print(f'  Present:           {stats["has_md"]:>6} ({stats["has_md"]/total*100:.1f}%)')
print(f'  NULL:              {stats["null_md"]:>6} ({stats["null_md"]/total*100:.1f}%)')
print(f'  By clearance type when NULL:')
for ct, n in sorted(null_manifest_by_clearance.items(), key=lambda x: -x[1]):
    print(f'    {ct}: {n}')

md_total = stats['has_md']
print(f'\n--- Fields within ManifestDetails (n={md_total}) ---')
for field in ['dp', 'bl', 'rotation', 'consignee', 'origin', 'housebl']:
    has = stats.get(f'has_{field}', 0)
    null = stats.get(f'null_{field}', 0)
    print(f'  {field:<15} has={has:>6} null={null:>6} ({has/max(1,has+null)*100:.1f}%)')

print(f'\n--- All ManifestDetails field names ---')
for f, n in sorted(manifest_fields.items(), key=lambda x: -x[1]):
    print(f'  {f:<25} {n:>6}')

print(f'\n--- DeliveryPlace values (top 15) ---')
for dp, n in sorted(dp_values.items(), key=lambda x: -x[1])[:15]:
    print(f'  {dp:<20} {n:>6}')

print(f'\n--- Sample: NULL ManifestDetails records ---')
for s in sample_null_manifest:
    print(f'  {s["cn"]:<22} ct={s["ct"]:<4} decl={s["decl"]} iso={s["iso"]} items={s["items"]} duty={s["total_duty"]}')

if sample_null_dp_with_manifest:
    print(f'\n--- Sample: ManifestDetails present but DeliveryPlace NULL ---')
    for s in sample_null_dp_with_manifest:
        print(f'  {s["cn"]:<22} ct={s["ct"]:<4} decl={s["decl"]} iso={s["iso"]}')
        print(f'    Non-null MD fields: {s["md_keys"]}')
