"""
ICUMS Y:\\ backlog ingestion into nickscan_icums_staging.

Walks Y:\\BatchData and Y:\\ContainerData, parses each non-archived JSON file,
and inserts BOE documents + manifest items into the staging DB.

- Mirrors the field mapping from IcumJsonIngestionService but is simpler since
  we don't need to integrate with downstream services — this is for analysis only.
- Vehicle detection uses the same ContainerNumberValidator logic (reimplemented
  in Python below).
- Idempotent by file path: re-running skips files already marked `ok` in
  staging_source_files.
- Batches inserts 1000 BOE documents at a time to keep the transaction log sane.

Usage:
    python ingest_yicums.py [--limit N] [--kind BatchData|ContainerData]
                            [--dry-run] [--reset-failed]

Env vars:
    NICKSCAN_DB_PASSWORD   — required
"""
import argparse
import glob
import json
import os
import re
import sys
import time
from datetime import datetime

import psycopg2
from psycopg2.extras import execute_batch, Json


SOURCE_ROOTS = {
    "BatchData":     r"Y:\BatchData",
    "ContainerData": r"Y:\ContainerData",
}
ARCHIVED_MARKER = "_archived_"


# ── Vehicle detection ────────────────────────────────────────────────────────
# Standard ISO 6346 container format: 4 letters + 7 digits (e.g. MSCU1234567)
ISO_CONTAINER_RE = re.compile(r"^[A-Z]{4}\d{7}$")
# VIN: 17 chars, alphanumeric, no I/O/Q
VIN_RE = re.compile(r"^[A-HJ-NPR-Z0-9]{17}$")

VEHICLE_KEYWORDS = [
    "toyota", "honda", "ford", "mercedes", "benz", "nissan", "hyundai", "kia",
    "mazda", "subaru", "audi", "bmw", "lexus", "chevrolet", "chevy", "gmc",
    "dodge", "jeep", "chrysler", "acura", "infiniti", "mitsubishi", "suzuki",
    "peugeot", "renault", "citroen", "volkswagen", "fiat", "volvo", "porsche",
    "land rover", "range rover", "cadillac", "buick", "lincoln",
    "vehicle", "motor vehicle", "motorcar", "automobile", "sedan", "suv",
    "pickup", "truck", "motorcycle", "motorbike",
    "used car", "new car", "vin:", "chassis",
]


def is_vehicle_container_number(cn: str) -> bool:
    """Return True if the container number looks like a VIN / vehicle identifier."""
    if not cn:
        return False
    s = cn.strip().upper()
    if ISO_CONTAINER_RE.match(s):
        return False
    if VIN_RE.match(s):
        return True
    # Short catch-all: if it's not ISO container format and it's long enough to be a VIN,
    # or if it contains only digits/letters of atypical length
    if len(s) == 17 and s.isalnum():
        return True
    if len(s) < 11 and s.isdigit():
        return True  # plate-style
    return False


def items_mention_vehicle(items: list) -> bool:
    """Return True if ANY manifest item description mentions a vehicle keyword."""
    for it in items or []:
        desc = (it.get("DESCRIPTION") or "").lower()
        if any(kw in desc for kw in VEHICLE_KEYWORDS):
            return True
    return False


# ── Parsing ──────────────────────────────────────────────────────────────────
def parse_file(path):
    """Return a (boe_docs, manifest_items, error) tuple."""
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except Exception as e:
        return None, None, f"read/parse failed: {e}"

    docs = data.get("BOEScanDocument") or []
    if not isinstance(docs, list):
        return None, None, f"BOEScanDocument not a list (got {type(docs).__name__})"

    boe_rows = []
    item_rows = []

    for idx, d in enumerate(docs):
        try:
            cd = d.get("ContainerDetails") or {}
            h = d.get("Header") or {}
            m = d.get("ManifestDetails") or {}
            items = d.get("ManifestItems") or []

            cn_raw = (cd.get("ContainerNumber") or "").strip()
            is_veh = is_vehicle_container_number(cn_raw) or items_mention_vehicle(items)
            veh_id = cn_raw if is_veh else None

            boe = {
                "document_index": idx,
                "container_number": cn_raw.upper() if cn_raw else "UNKNOWN",
                "container_description": cd.get("Description"),
                "container_iso": cd.get("ISO") or cd.get("ContainerISO"),
                "container_quantity": _int(cd.get("Quantity")),
                "container_weight": _num(cd.get("Weight") or cd.get("ContainerWeight")),
                "is_vehicle": is_veh,
                "vehicle_identifier": veh_id,

                "imp_name": h.get("IMPNAME") or h.get("ImpName"),
                "imp_address": h.get("IMPADDRESS") or h.get("ImpAddress"),
                "exp_name": h.get("EXPNAME") or h.get("ExpName"),
                "exp_address": h.get("EXPADDRESS") or h.get("ExpAddress"),
                "declarant_name": h.get("DeclarantName"),
                "declarant_address": h.get("DECLARANTADDRESS") or h.get("DeclarantAddress"),
                "total_duty_paid": _num(h.get("TotalDutyPaid")),
                "crms_level": h.get("CRMSLevel") or h.get("CrmsLevel"),
                "declaration_number": (h.get("DeclarationNumber") or "").strip() or None,
                "regime_code": h.get("RegimeCode"),
                "no_of_containers": _int(h.get("NoofContainers") or h.get("NoOfContainers")),
                "comp_off_remarks": h.get("CompOffRemarks"),
                "impexp_name": h.get("IMPEXPNAME") or h.get("ImpExpName"),
                "impexp_address": h.get("IMPEXPADDRESS") or h.get("ImpExpAddress"),
                "declaration_version": _int(h.get("DeclarationVersion")),
                "declaration_date": h.get("DeclarationDate"),
                "clearance_type": h.get("ClearanceType"),
                "ccvr_intel_remarks": h.get("CCVRIntelRemarks"),

                "rotation_number": m.get("RotationNumber"),
                "bl_number": m.get("BLNumber") or m.get("BlNumber"),
                "house_bl": m.get("HouseBL") or m.get("HouseBl"),
                "master_bl_number": m.get("MasterBLNumber") or m.get("MasterBlNumber"),
                "delivery_place": m.get("DeliveryPlace"),
                "consignee_name": m.get("ConsigneeName"),
                "consignee_address": m.get("ConsigneeAddress"),
                "country_of_origin": m.get("CountryOfOrigin"),
                "marks_numbers": m.get("MarksNumbers"),
                "shipper_name": m.get("ShipperName"),
                "shipper_address": m.get("ShipperAddress"),
                "goods_description": m.get("GoodsDescription"),
                "is_consolidated": bool(m.get("IsConsolidated")) if m else False,

                "raw_json": d,
            }
            boe_rows.append(boe)

            for i_idx, it in enumerate(items):
                item_rows.append({
                    "_boe_index": idx,  # resolved to FK after BOE insert
                    "item_index": i_idx,
                    "item_no": _int(it.get("ITEMNO")),
                    "hs_code": it.get("HSCODE"),
                    "description": it.get("DESCRIPTION"),
                    "quantity": _num(it.get("QUANTITY")),
                    "unit": it.get("UNIT"),
                    "weight": _num(it.get("WEIGHT")),
                    "item_fob": _num(it.get("ITEMFOB")),
                    "item_duty_paid": _num(it.get("ITEMDUTYPAID")),
                    "fob_currency": it.get("FOBCURRENCY"),
                    "country_of_origin": it.get("COUNTRYOFORIGIN"),
                    "cpc": it.get("CPC"),
                })
        except Exception as e:
            return None, None, f"doc {idx} failed: {e}"

    return boe_rows, item_rows, None


def _num(v):
    if v is None or v == "":
        return None
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def _int(v):
    if v is None or v == "":
        return None
    try:
        return int(v)
    except (TypeError, ValueError):
        return None


# ── DB insert ────────────────────────────────────────────────────────────────
BOE_COLS = [
    "source_file_id", "document_index",
    "container_number", "container_description", "container_iso",
    "container_quantity", "container_weight", "is_vehicle", "vehicle_identifier",
    "imp_name", "imp_address", "exp_name", "exp_address",
    "declarant_name", "declarant_address",
    "total_duty_paid", "crms_level", "declaration_number", "regime_code",
    "no_of_containers", "comp_off_remarks",
    "impexp_name", "impexp_address", "declaration_version", "declaration_date",
    "clearance_type", "ccvr_intel_remarks",
    "rotation_number", "bl_number", "house_bl", "master_bl_number",
    "delivery_place", "consignee_name", "consignee_address", "country_of_origin",
    "marks_numbers", "shipper_name", "shipper_address", "goods_description",
    "is_consolidated", "raw_json",
]

ITEM_COLS = [
    "boe_document_id", "item_index", "item_no", "hs_code", "description",
    "quantity", "unit", "weight", "item_fob", "item_duty_paid",
    "fob_currency", "country_of_origin", "cpc",
]


def insert_file(conn, cur, file_path, file_name, file_kind, file_size, boe_rows, item_rows):
    """Insert one file's data. Returns (source_file_id, boe_count, item_count)."""
    # Source file row
    cur.execute(
        """
        INSERT INTO staging_source_files (file_path, file_name, file_kind, file_size_bytes, parse_status, parsed_at, document_count, item_count)
        VALUES (%s, %s, %s, %s, 'ok', now(), %s, %s)
        ON CONFLICT (file_path) DO UPDATE
            SET parse_status = 'ok', parsed_at = now(),
                document_count = EXCLUDED.document_count,
                item_count = EXCLUDED.item_count,
                parse_error = NULL
        RETURNING id
        """,
        (file_path, file_name, file_kind, file_size, len(boe_rows), len(item_rows)),
    )
    source_file_id = cur.fetchone()[0]

    # Delete any prior BOE rows for this file (idempotency on re-ingest)
    cur.execute("DELETE FROM staging_boe_documents WHERE source_file_id = %s", (source_file_id,))

    # Insert BOE docs and capture ids in order
    boe_ids_by_index = {}
    if boe_rows:
        cols = BOE_COLS[1:]  # skip source_file_id, set explicitly below
        placeholders = ",".join(["%s"] * (len(cols) + 1))  # +1 for source_file_id
        insert_sql = (
            f"INSERT INTO staging_boe_documents (source_file_id, {', '.join(cols)}) "
            f"VALUES ({placeholders}) RETURNING id"
        )
        for boe in boe_rows:
            values = tuple(
                Json(boe[c]) if c == "raw_json" else boe[c]
                for c in cols
            )
            cur.execute(insert_sql, (source_file_id, *values))
            boe_ids_by_index[boe["document_index"]] = cur.fetchone()[0]

    # Insert manifest items with resolved FK
    if item_rows:
        item_data = []
        for it in item_rows:
            boe_id = boe_ids_by_index.get(it["_boe_index"])
            if boe_id is None:
                continue
            item_data.append((
                boe_id, it["item_index"], it["item_no"], it["hs_code"], it["description"],
                it["quantity"], it["unit"], it["weight"], it["item_fob"], it["item_duty_paid"],
                it["fob_currency"], it["country_of_origin"], it["cpc"],
            ))
        if item_data:
            placeholders = ",".join(["(" + ",".join(["%s"] * len(ITEM_COLS)) + ")"] * len(item_data))
            flat = [v for row in item_data for v in row]
            cur.execute(
                f"INSERT INTO staging_manifest_items ({', '.join(ITEM_COLS)}) VALUES " + placeholders,
                flat,
            )

    return source_file_id, len(boe_rows), len(item_rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--kind", choices=list(SOURCE_ROOTS.keys()), default=None,
                        help="Only ingest one kind (default: both)")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--reset-failed", action="store_true",
                        help="Re-attempt files that previously failed")
    args = parser.parse_args()

    pw = os.environ.get("NICKSCAN_DB_PASSWORD", "")
    if not pw:
        print("ERROR: NICKSCAN_DB_PASSWORD must be set", file=sys.stderr)
        sys.exit(1)

    conn = psycopg2.connect(
        host="localhost", dbname="nickscan_icums_staging",
        user="postgres", password=pw,
    )
    conn.autocommit = False
    cur = conn.cursor()

    # Find files not yet ingested successfully
    kinds = [args.kind] if args.kind else list(SOURCE_ROOTS.keys())

    candidate_files = []
    for kind in kinds:
        root = SOURCE_ROOTS[kind]
        if not os.path.isdir(root):
            print(f"WARNING: {root} not accessible, skipping {kind}")
            continue
        files = glob.glob(os.path.join(root, "*.json"))
        # Exclude archived copies
        files = [f for f in files if ARCHIVED_MARKER not in os.path.basename(f)]
        candidate_files.extend((f, kind) for f in files)

    print(f"Found {len(candidate_files)} candidate non-archived files across {len(kinds)} roots")

    # Query existing parse status
    cur.execute("SELECT file_path, parse_status FROM staging_source_files")
    known = {row[0]: row[1] for row in cur.fetchall()}
    if args.reset_failed:
        candidate_files = [(f, k) for (f, k) in candidate_files if known.get(f) != "ok"]
    else:
        candidate_files = [(f, k) for (f, k) in candidate_files if known.get(f) not in ("ok",)]
    print(f"After dedup against staging_source_files: {len(candidate_files)} files to process")

    if args.limit:
        candidate_files = candidate_files[: args.limit]
        print(f"Limited to {len(candidate_files)} files")

    if args.dry_run:
        for f, k in candidate_files[:5]:
            print(f"  DRY {k}: {os.path.basename(f)}")
        if len(candidate_files) > 5:
            print(f"  ... and {len(candidate_files) - 5} more")
        return

    ok = 0
    failed = 0
    total_docs = 0
    total_items = 0
    t0 = time.monotonic()

    for i, (file_path, kind) in enumerate(candidate_files, 1):
        file_name = os.path.basename(file_path)
        try:
            file_size = os.path.getsize(file_path)
        except OSError:
            file_size = None

        boe_rows, item_rows, err = parse_file(file_path)
        if err is not None:
            cur.execute(
                """
                INSERT INTO staging_source_files (file_path, file_name, file_kind, file_size_bytes, parse_status, parse_error, parsed_at)
                VALUES (%s, %s, %s, %s, 'failed', %s, now())
                ON CONFLICT (file_path) DO UPDATE
                    SET parse_status = 'failed', parse_error = EXCLUDED.parse_error, parsed_at = now()
                """,
                (file_path, file_name, kind, file_size, err),
            )
            conn.commit()
            failed += 1
            continue

        try:
            _, d_cnt, i_cnt = insert_file(conn, cur, file_path, file_name, kind, file_size, boe_rows, item_rows)
            conn.commit()
            ok += 1
            total_docs += d_cnt
            total_items += i_cnt
        except Exception as e:
            conn.rollback()
            print(f"  ERR {file_name}: {e}")
            try:
                cur.execute(
                    """
                    INSERT INTO staging_source_files (file_path, file_name, file_kind, file_size_bytes, parse_status, parse_error, parsed_at)
                    VALUES (%s, %s, %s, %s, 'failed', %s, now())
                    ON CONFLICT (file_path) DO UPDATE
                        SET parse_status = 'failed', parse_error = EXCLUDED.parse_error, parsed_at = now()
                    """,
                    (file_path, file_name, kind, file_size, str(e)[:2000]),
                )
                conn.commit()
            except Exception:
                conn.rollback()
            failed += 1

        if i % 500 == 0:
            elapsed = time.monotonic() - t0
            rate = i / elapsed if elapsed > 0 else 0
            print(f"  progress: {i}/{len(candidate_files)} files ({ok} ok, {failed} failed) {rate:.1f} files/sec, {total_docs} docs, {total_items} items")

    elapsed = time.monotonic() - t0
    print(f"\nDone in {elapsed:.1f}s: {ok} ok, {failed} failed, {total_docs} BOE docs, {total_items} manifest items")
    conn.close()


if __name__ == "__main__":
    main()
