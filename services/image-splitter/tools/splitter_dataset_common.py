"""Shared helpers for image-splitter dataset export/evaluation tools.

These helpers intentionally use direct read-only SQL instead of importing the
runtime service models. That keeps the CLI tools independent from FastAPI app
startup and avoids mutating service behavior while still using the production
schema.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from collections import Counter, defaultdict
from datetime import date, datetime, time, timedelta, timezone
from decimal import Decimal
from pathlib import Path
from typing import Any, Iterable, Sequence
from urllib.parse import quote_plus

try:
    from dotenv import load_dotenv
except ImportError:  # pragma: no cover - optional convenience dependency
    load_dotenv = None


DATASET_MANIFEST_SCHEMA_VERSION = "nscim.image_splitter.dataset_manifest.v1"
DATASET_LABEL_SCHEMA_VERSION = "nscim.image_splitter.label.v1"
EXPORT_SUMMARY_SCHEMA_VERSION = "nscim.image_splitter.export_summary.v1"
EVALUATION_SUMMARY_SCHEMA_VERSION = "nscim.image_splitter.evaluation_summary.v1"
EVALUATION_DETAIL_SCHEMA_VERSION = "nscim.image_splitter.evaluation_detail.v1"

DATE_FIELD_SQL = {
    "created_at": "{alias}.created_at",
    "completed_at": "{alias}.completed_at",
    "reviewed_at": "{alias}.reviewed_at",
    "ground_truth_set_at": "{alias}.ground_truth_set_at",
}


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def normalize_database_url(database_url: str) -> str:
    """Convert SQLAlchemy-style postgres URLs into psycopg2 URLs."""
    return (
        database_url
        .replace("postgresql+psycopg2://", "postgresql://", 1)
        .replace("postgresql+asyncpg://", "postgresql://", 1)
    )


def connect_database(database_url: str | None = None):
    """Open a psycopg2 connection using CLI/env/default splitter settings."""
    if load_dotenv is not None:
        load_dotenv()

    try:
        import psycopg2
    except ImportError as exc:  # pragma: no cover - runtime environment issue
        raise SystemExit(
            "psycopg2 is required. Run inside services/image-splitter/venv or "
            "install psycopg2-binary from requirements.txt."
        ) from exc

    url = (
        database_url
        or os.getenv("IMAGE_SPLITTER_DATABASE_URL")
        or os.getenv("DATABASE_URL_SYNC")
        or os.getenv("DATABASE_URL")
    )
    if url:
        return psycopg2.connect(normalize_database_url(url))

    password = os.getenv("NICKSCAN_DB_PASSWORD", "")
    host = os.getenv("PGHOST", "localhost")
    port = int(os.getenv("PGPORT", "5432"))
    dbname = os.getenv("PGDATABASE", "nickscan_production")
    user = os.getenv("PGUSER", "postgres")
    return psycopg2.connect(
        host=host,
        port=port,
        dbname=dbname,
        user=user,
        password=password,
    )


def default_database_url_hint() -> str:
    password = quote_plus(os.getenv("NICKSCAN_DB_PASSWORD", ""))
    return f"postgresql://postgres:{password}@localhost:5432/nickscan_production"


def add_common_filter_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--database-url",
        help=(
            "PostgreSQL URL. Defaults to IMAGE_SPLITTER_DATABASE_URL, "
            "DATABASE_URL_SYNC, DATABASE_URL, then localhost nickscan_production."
        ),
    )
    parser.add_argument(
        "--scanner",
        "--scanner-type",
        dest="scanner_types",
        action="append",
        default=[],
        help=(
            "Filter by scanner type. May be repeated or comma-separated "
            "(for example: ASE,FS6000). Use unknown for null/blank scanners."
        ),
    )
    parser.add_argument(
        "--from-date",
        "--created-from",
        dest="from_date",
        help="Inclusive lower date/datetime bound for the selected date field.",
    )
    parser.add_argument(
        "--to-date",
        "--created-to",
        dest="to_date",
        help=(
            "Exclusive upper date/datetime bound. A YYYY-MM-DD value includes "
            "that whole day by advancing the exclusive bound one day."
        ),
    )
    parser.add_argument(
        "--date-field",
        choices=sorted(DATE_FIELD_SQL),
        default="created_at",
        help="Job timestamp column used by --from-date/--to-date.",
    )
    parser.add_argument(
        "--label-source",
        choices=("any", "ground_truth", "analyst"),
        default="any",
        help=(
            "Label source to use. any prefers ground_truth_split_x and falls "
            "back to correct_split_x."
        ),
    )
    parser.add_argument(
        "--status",
        action="append",
        default=[],
        help="Optional job status filter. May be repeated or comma-separated.",
    )
    parser.add_argument("--limit", type=int, help="Maximum jobs to read.")


def parse_csv_values(values: Sequence[str] | None) -> list[str]:
    out: list[str] = []
    for raw in values or []:
        for item in raw.split(","):
            item = item.strip()
            if item:
                out.append(item)
    return out


def parse_scanners(values: Sequence[str] | None) -> tuple[list[str], bool]:
    scanners = [s.lower() for s in parse_csv_values(values)]
    include_unknown = any(s in {"unknown", "null", "none", "(null)"} for s in scanners)
    known = [s for s in scanners if s not in {"unknown", "null", "none", "(null)"}]
    return known, include_unknown


def parse_datetime_bound(raw: str | None, *, is_end: bool) -> datetime | None:
    if not raw:
        return None

    text = raw.strip()
    if not text:
        return None

    if len(text) == 10 and text[4] == "-" and text[7] == "-":
        parsed_date = date.fromisoformat(text)
        dt = datetime.combine(parsed_date, time.min, tzinfo=timezone.utc)
        return dt + timedelta(days=1) if is_end else dt

    dt = datetime.fromisoformat(text.replace("Z", "+00:00"))
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def label_sql_parts(label_source: str, alias: str = "j") -> tuple[str, str]:
    negative_label_expr = (
        f"LOWER(COALESCE({alias}.analyst_verdict, '')) IN "
        "('single_container', 'visual_single', 'visualsingle', "
        "'bad_image', 'badimage', 'scanner_decode_failure', 'decode_failure')"
    )
    if label_source == "ground_truth":
        return (
            f"{alias}.ground_truth_split_x",
            (
                f"CASE WHEN {alias}.ground_truth_split_x IS NOT NULL "
                f"THEN 'ground_truth' ELSE NULL END"
            ),
        )
    if label_source == "analyst":
        return (
            f"CASE WHEN {alias}.correct_split_x IS NOT NULL THEN {alias}.correct_split_x "
            f"WHEN {negative_label_expr} THEN -1 ELSE NULL END",
            (
                f"CASE WHEN {alias}.correct_split_x IS NOT NULL THEN 'analyst_correct' "
                f"WHEN {negative_label_expr} THEN 'analyst_negative' ELSE NULL END"
            ),
        )
    return (
        f"COALESCE({alias}.ground_truth_split_x, {alias}.correct_split_x, "
        f"CASE WHEN {negative_label_expr} THEN -1 ELSE NULL END)",
        (
            f"CASE WHEN {alias}.ground_truth_split_x IS NOT NULL THEN 'ground_truth' "
            f"WHEN {alias}.correct_split_x IS NOT NULL THEN 'analyst_correct' "
            f"WHEN {negative_label_expr} THEN 'analyst_negative' "
            f"ELSE NULL END"
        ),
    )


def build_job_filters(args: argparse.Namespace, alias: str = "j") -> tuple[list[str], list[Any]]:
    where: list[str] = []
    params: list[Any] = []

    scanners, include_unknown = parse_scanners(getattr(args, "scanner_types", []))
    if scanners or include_unknown:
        scanner_clauses: list[str] = []
        if scanners:
            scanner_clauses.append(f"LOWER({alias}.scanner_type) = ANY(%s)")
            params.append(scanners)
        if include_unknown:
            scanner_clauses.append(f"({alias}.scanner_type IS NULL OR BTRIM({alias}.scanner_type) = '')")
        where.append("(" + " OR ".join(scanner_clauses) + ")")

    statuses = [s.lower() for s in parse_csv_values(getattr(args, "status", []))]
    if statuses:
        where.append(f"LOWER({alias}.status) = ANY(%s)")
        params.append(statuses)

    date_field = DATE_FIELD_SQL[getattr(args, "date_field", "created_at")].format(alias=alias)
    from_date = parse_datetime_bound(getattr(args, "from_date", None), is_end=False)
    to_date = parse_datetime_bound(getattr(args, "to_date", None), is_end=True)
    if from_date is not None:
        where.append(f"{date_field} >= %s")
        params.append(from_date)
    if to_date is not None:
        where.append(f"{date_field} < %s")
        params.append(to_date)

    return where, params


def fetch_jobs(
    conn,
    args: argparse.Namespace,
    *,
    include_image: bool,
    require_image: bool,
    include_unlabeled: bool = False,
    exclude_negatives: bool = False,
) -> list[dict[str, Any]]:
    """Fetch splitter jobs with derived label columns."""
    from psycopg2.extras import RealDictCursor

    label_expr, label_source_expr = label_sql_parts(args.label_source, alias="j")
    where, params = build_job_filters(args, alias="j")
    if require_image:
        where.append("j.image_data IS NOT NULL")
    if not include_unlabeled:
        where.append(f"({label_expr}) IS NOT NULL")
    if exclude_negatives:
        where.append(f"({label_expr}) >= 0")

    image_select = "j.image_data," if include_image else ""
    where_sql = "WHERE " + " AND ".join(where) if where else ""
    limit_sql = ""
    if getattr(args, "limit", None):
        limit_sql = "LIMIT %s"
        params.append(args.limit)

    sql = f"""
        SELECT
            j.id AS job_id,
            j.container_numbers,
            j.source_image_id,
            j.scanner_type,
            j.image_width,
            j.image_height,
            j.status,
            j.best_strategy,
            j.best_score,
            j.split_x AS best_split_x,
            j.created_at,
            j.completed_at,
            j.analyst_verdict,
            j.correct_split_x,
            j.reviewed_by,
            j.reviewed_at,
            j.ground_truth_split_x,
            j.ground_truth_set_by,
            j.ground_truth_set_at,
            j.ground_truth_notes,
            j.claude_vision_split_x,
            j.claude_vision_confidence,
            j.claude_vision_reasoning,
            j.claude_vision_model,
            j.claude_vision_ran_at,
            octet_length(j.image_data) AS image_bytes,
            {image_select}
            ({label_expr}) AS label_split_x,
            ({label_source_expr}) AS label_source
        FROM image_split_jobs j
        {where_sql}
        ORDER BY j.created_at ASC NULLS LAST, j.id ASC
        {limit_sql}
    """

    with conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(sql, params)
        return [dict(row) for row in cur.fetchall()]


def fetch_candidates(conn, job_ids: Iterable[Any]) -> dict[str, list[dict[str, Any]]]:
    job_id_text = [str(job_id) for job_id in job_ids]
    if not job_id_text:
        return {}

    from psycopg2.extras import RealDictCursor

    sql = """
        SELECT
            r.id AS result_id,
            r.job_id,
            r.strategy_name,
            r.split_x,
            r.confidence,
            r.processing_ms,
            r.metadata AS strategy_metadata,
            r.created_at
        FROM image_split_results r
        WHERE r.job_id::text = ANY(%s)
        ORDER BY r.job_id ASC, r.confidence DESC NULLS LAST, r.created_at ASC
    """

    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    with conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(sql, (job_id_text,))
        for row in cur.fetchall():
            item = dict(row)
            grouped[str(item["job_id"])].append(item)
    return grouped


def json_default(value: Any) -> Any:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    if isinstance(value, Decimal):
        return float(value)
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    if isinstance(value, memoryview):
        return bytes(value).decode("utf-8", errors="replace")
    return str(value)


def dump_json(data: Any, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(data, indent=2, sort_keys=True, default=json_default) + "\n",
        encoding="utf-8",
    )


def write_jsonl_record(handle, record: dict[str, Any]) -> None:
    handle.write(json.dumps(record, sort_keys=True, default=json_default))
    handle.write("\n")


def bytes_from_db(value: Any) -> bytes:
    if value is None:
        return b""
    if isinstance(value, bytes):
        return value
    if isinstance(value, bytearray):
        return bytes(value)
    if isinstance(value, memoryview):
        return value.tobytes()
    return bytes(value)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def image_extension(image_data: bytes) -> str:
    if image_data.startswith(b"\xff\xd8\xff"):
        return "jpg"
    if image_data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "png"
    if image_data.startswith(b"BM"):
        return "bmp"
    if image_data.startswith(b"II*\x00") or image_data.startswith(b"MM\x00*"):
        return "tif"
    return "bin"


def split_container_numbers(raw: str | None) -> list[str]:
    if not raw:
        return []
    return [item.strip() for item in raw.split(",") if item.strip()]


def normalize_group_text(raw: Any) -> str:
    text = "" if raw is None else str(raw)
    return "".join(text.lower().split())


def label_class(label_split_x: Any) -> str:
    if label_split_x is None:
        return "unlabeled"
    try:
        return "no_split" if int(label_split_x) < 0 else "split"
    except (TypeError, ValueError):
        return "unlabeled"


def normalized_split_x(split_x: Any, image_width: Any) -> float | None:
    if split_x is None or image_width in (None, 0):
        return None
    try:
        split_i = int(split_x)
        width_i = int(image_width)
    except (TypeError, ValueError):
        return None
    if split_i < 0 or width_i <= 0:
        return None
    return round(split_i / width_i, 8)


def parse_split_ratios(raw: str) -> tuple[float, float, float]:
    values = [float(item.strip()) for item in raw.replace("/", ",").split(",") if item.strip()]
    if len(values) != 3:
        raise argparse.ArgumentTypeError("split ratios must have three values, e.g. 80,10,10")
    if any(value < 0 for value in values):
        raise argparse.ArgumentTypeError("split ratios cannot be negative")
    total = sum(values)
    if total <= 0:
        raise argparse.ArgumentTypeError("split ratios must sum to a positive value")
    return values[0] / total, values[1] / total, values[2] / total


def stable_group_key(row: dict[str, Any], method: str, image_sha256: str | None = None) -> str:
    job_id = str(row.get("job_id"))
    scanner = normalize_group_text(row.get("scanner_type") or "unknown")
    created = row.get("created_at")
    created_day = created.date().isoformat() if isinstance(created, datetime) else "unknown-date"
    containers = normalize_group_text(row.get("container_numbers"))

    if method == "image_hash":
        return image_sha256 or job_id
    if method == "source_image_id":
        return str(row.get("source_image_id") or job_id)
    if method == "container_numbers":
        return containers or job_id
    if method == "scanner_container":
        return f"{scanner}:{containers or job_id}"
    if method == "scanner_date":
        return f"{scanner}:{created_day}"
    if method == "job_id":
        return job_id
    raise ValueError(f"Unknown group method: {method}")


def assign_dataset_split(
    group_key: str,
    ratios: tuple[float, float, float],
    seed: str,
) -> tuple[str, str, float]:
    digest = hashlib.sha256(f"{seed}:{group_key}".encode("utf-8")).hexdigest()
    bucket = int(digest[:16], 16) / float(0xFFFFFFFFFFFFFFFF)
    train, val, _test = ratios
    if bucket < train:
        return "train", digest, bucket
    if bucket < train + val:
        return "val", digest, bucket
    return "test", digest, bucket


def candidate_to_manifest(row: dict[str, Any]) -> dict[str, Any]:
    metadata = row.get("strategy_metadata")
    if metadata is None:
        metadata = {}
    return {
        "result_id": str(row.get("result_id")),
        "strategy_name": row.get("strategy_name"),
        "split_x": row.get("split_x"),
        "normalized_split_x": None,
        "confidence": row.get("confidence"),
        "processing_ms": row.get("processing_ms"),
        "created_at": row.get("created_at"),
        "metadata": metadata if isinstance(metadata, dict) else {},
    }


def summarize_counts(rows: Iterable[dict[str, Any]]) -> dict[str, Any]:
    scanner_counts: Counter[str] = Counter()
    label_source_counts: Counter[str] = Counter()
    label_class_counts: Counter[str] = Counter()
    for row in rows:
        scanner_counts[str(row.get("scanner_type") or "unknown")] += 1
        label_source_counts[str(row.get("label_source") or "unlabeled")] += 1
        label_class_counts[label_class(row.get("label_split_x"))] += 1
    return {
        "by_scanner": dict(sorted(scanner_counts.items())),
        "by_label_source": dict(sorted(label_source_counts.items())),
        "by_label_class": dict(sorted(label_class_counts.items())),
    }


def percentile(values: Sequence[float], pct: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return float(ordered[0])
    pos = (len(ordered) - 1) * pct
    lower = math.floor(pos)
    upper = math.ceil(pos)
    if lower == upper:
        return float(ordered[int(pos)])
    lower_value = ordered[lower] * (upper - pos)
    upper_value = ordered[upper] * (pos - lower)
    return float(lower_value + upper_value)


def mean(values: Sequence[float]) -> float | None:
    if not values:
        return None
    return float(sum(values) / len(values))


def round_optional(value: float | None, digits: int = 4) -> float | None:
    if value is None:
        return None
    return round(float(value), digits)


def path_as_manifest(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def filters_to_summary(args: argparse.Namespace) -> dict[str, Any]:
    scanners, include_unknown = parse_scanners(getattr(args, "scanner_types", []))
    return {
        "scanner_types": scanners + (["unknown"] if include_unknown else []),
        "from_date": getattr(args, "from_date", None),
        "to_date": getattr(args, "to_date", None),
        "date_field": getattr(args, "date_field", "created_at"),
        "label_source": getattr(args, "label_source", "any"),
        "status": parse_csv_values(getattr(args, "status", [])),
        "limit": getattr(args, "limit", None),
    }
