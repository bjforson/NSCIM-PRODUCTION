"""
Audit completed FS6000 splitter jobs for likely visual contamination.

The FS6000 intake can list two container numbers even when the image is
visually one container or too ambiguous to split. This tool reads existing
image_split_jobs rows, applies a lightweight visual eligibility gate, and
writes JSON/CSV summaries.

Default behavior is read-only. The only DB write path is --write-audit-notes,
and it is rejected unless --allow-db-mutations is also provided.

Usage:
    python tools/staging/audit_fs6000_splitter_contamination.py --limit 25
    python tools/staging/audit_fs6000_splitter_contamination.py --status all --out-dir C:\\tmp\\fs6000_audit
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import os
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

import numpy as np
import psycopg2
import psycopg2.extras
from PIL import Image


DEFAULT_OUT_DIR = Path(r"C:\tmp\fs6000_splitter_audit")
CONTAINER_SPLIT_RE = re.compile(r"[,;|/]+")
REPO_ROOT = Path(__file__).resolve().parents[2]
SPLITTER_DIR = REPO_ROOT / "services" / "image-splitter"


@dataclass
class GateThresholds:
    max_width: int = 900
    min_dual_span_ratio: float = 0.62
    single_span_ratio: float = 0.54
    dual_gap_score: float = 0.055
    split_gap_score: float = 0.045
    central_wall_score: float = 0.56
    min_side_span_ratio: float = 0.22


@dataclass
class GapCandidate:
    score: float = 0.0
    x: int | None = None
    width: int = 0
    valley: float = 0.0
    left_support: float = 0.0
    right_support: float = 0.0


@dataclass
class GateResult:
    eligibility: str
    confidence: float
    reason_codes: list[str]
    metrics: dict[str, Any]


def connect():
    return psycopg2.connect(
        host=os.environ.get("NICKSCAN_DB_HOST", "localhost"),
        port=int(os.environ.get("NICKSCAN_DB_PORT", "5432")),
        dbname=os.environ.get("NICKSCAN_DB_NAME", "nickscan_production"),
        user=os.environ.get("NICKSCAN_DB_USER", "postgres"),
        password=os.environ.get("NICKSCAN_DB_PASSWORD", ""),
    )


def parse_container_numbers(raw: str | None) -> list[str]:
    tokens = []
    for token in CONTAINER_SPLIT_RE.split(raw or ""):
        cleaned = token.strip().upper()
        if cleaned and cleaned != "UNKNOWN":
            tokens.append(cleaned)
    return tokens


def moving_average(values: np.ndarray, width: int) -> np.ndarray:
    width = max(3, int(width))
    if width % 2 == 0:
        width += 1
    kernel = np.ones(width, dtype=np.float32) / float(width)
    return np.convolve(values, kernel, mode="same")


def find_runs(mask: np.ndarray, min_width: int) -> list[tuple[int, int]]:
    runs: list[tuple[int, int]] = []
    start: int | None = None
    for i, value in enumerate(mask):
        if value and start is None:
            start = i
        elif not value and start is not None:
            if i - start >= min_width:
                runs.append((start, i))
            start = None
    if start is not None and len(mask) - start >= min_width:
        runs.append((start, len(mask)))
    return runs


def merge_close_runs(runs: list[tuple[int, int]], max_gap: int) -> list[tuple[int, int]]:
    merged: list[tuple[int, int]] = []
    for start, end in runs:
        if merged and start - merged[-1][1] <= max_gap:
            merged[-1] = (merged[-1][0], end)
        else:
            merged.append((start, end))
    return merged


def pick_peaks(profile: np.ndarray, threshold: float, min_distance: int, limit: int = 12) -> list[tuple[float, int]]:
    peaks: list[tuple[float, int]] = []
    for i in range(2, len(profile) - 2):
        if profile[i] < threshold:
            continue
        if profile[i] >= profile[i - 1] and profile[i] >= profile[i + 1]:
            peaks.append((float(profile[i]), i))

    selected: list[tuple[float, int]] = []
    for value, x in sorted(peaks, reverse=True):
        if all(abs(x - existing_x) >= min_distance for _, existing_x in selected):
            selected.append((value, x))
        if len(selected) >= limit:
            break
    return selected


def load_scaled_rgb(image_data: bytes, max_width: int) -> tuple[np.ndarray, tuple[int, int], float]:
    image = Image.open(io.BytesIO(image_data)).convert("RGB")
    original_width, original_height = image.size
    scale = 1.0
    if original_width > max_width:
        scale = max_width / float(original_width)
        image = image.resize(
            (max_width, max(1, int(round(original_height * scale)))),
            Image.Resampling.BILINEAR,
        )
    return np.asarray(image, dtype=np.float32), (original_width, original_height), scale


def score_gap_segments(
    foreground_profile: np.ndarray,
    left: int,
    right: int,
    scale: float,
    original_width: int,
    split_x: int | None = None,
) -> tuple[GapCandidate, GapCandidate]:
    if right <= left + 12:
        return GapCandidate(), GapCandidate()

    width = len(foreground_profile)
    span = right - left
    inside = foreground_profile[left:right]
    low_threshold = max(
        float(np.percentile(foreground_profile, 8)),
        float(np.percentile(inside, 22)),
    )
    low_runs = find_runs(inside < low_threshold, max(4, int(width * 0.006)))

    split_scaled: int | None = None
    split_window = max(8, int(width * 0.045))
    if split_x is not None and original_width > 0:
        split_scaled = int(round(split_x * scale))

    best = GapCandidate()
    split_best = GapCandidate()
    flank_width = max(8, int(width * 0.025))

    for run_start, run_end in low_runs:
        start = left + run_start
        end = left + run_end
        if start < left + span * 0.18 or end > left + span * 0.82:
            continue

        flank_left = max(left, start - flank_width)
        flank_right = min(right, end + flank_width)
        left_support = (
            float(np.percentile(foreground_profile[flank_left:start], 75))
            if start > flank_left
            else 0.0
        )
        right_support = (
            float(np.percentile(foreground_profile[end:flank_right], 75))
            if flank_right > end
            else 0.0
        )
        valley = float(np.mean(foreground_profile[start:end]))
        support = min(left_support, right_support)
        scaled_width = end - start
        score = float((support - valley) * min(1.0, scaled_width / max(1.0, width * 0.025)))

        candidate = GapCandidate(
            score=score,
            x=int(round(((start + end) / 2.0) / scale)),
            width=int(round(scaled_width / scale)),
            valley=valley,
            left_support=left_support,
            right_support=right_support,
        )
        if candidate.score > best.score:
            best = candidate

        if split_scaled is not None:
            gap_mid = (start + end) // 2
            if abs(gap_mid - split_scaled) <= split_window and candidate.score > split_best.score:
                split_best = candidate

    return best, split_best


def classify_visual_eligibility_local(
    image_data: bytes | memoryview,
    split_x: int | None,
    thresholds: GateThresholds,
) -> GateResult:
    reasons: list[str] = []
    try:
        rgb, (original_width, original_height), scale = load_scaled_rgb(
            bytes(image_data), thresholds.max_width
        )
    except Exception as exc:
        return GateResult(
            eligibility="uncertain",
            confidence=0.0,
            reason_codes=["image_decode_failed"],
            metrics={"decode_error": str(exc)},
        )

    height, width = rgb.shape[:2]
    if width < 200 or height < 100:
        return GateResult(
            eligibility="uncertain",
            confidence=0.1,
            reason_codes=["image_too_small"],
            metrics={"image_width": original_width, "image_height": original_height},
        )

    roi_top = int(height * 0.10)
    roi_bottom = int(height * 0.86)
    roi = rgb[roi_top:roi_bottom, :, :]
    red = roi[:, :, 0]
    green = roi[:, :, 1]
    blue = roi[:, :, 2]
    max_channel = roi.max(axis=2)
    min_channel = roi.min(axis=2)
    saturation = (max_channel - min_channel) / (max_channel + 1.0)
    luma = 0.2126 * red + 0.7152 * green + 0.0722 * blue

    dark_cutoff = float(np.percentile(luma, 45))
    foreground = (saturation > 0.08) | (luma < dark_cutoff)
    foreground_profile = moving_average(
        foreground.mean(axis=0).astype(np.float32),
        max(7, int(width * 0.012)),
    )

    fg_q25 = float(np.percentile(foreground_profile, 25))
    fg_q90 = float(np.percentile(foreground_profile, 90))
    content_threshold = max(0.08, fg_q25 + 0.15 * (fg_q90 - fg_q25))
    content_runs = find_runs(
        foreground_profile > content_threshold,
        min_width=max(8, int(width * 0.03)),
    )
    merged_runs = merge_close_runs(content_runs, max_gap=max(8, int(width * 0.04)))

    if content_runs:
        content_left = content_runs[0][0]
        content_right = content_runs[-1][1]
        content_span_ratio = (content_right - content_left) / float(width)
    else:
        content_left = 0
        content_right = 0
        content_span_ratio = 0.0
        reasons.append("no_large_foreground_span")

    blue_mask = (
        (blue > red + 18.0)
        & (blue > green + 8.0)
        & (saturation > 0.12)
    )
    blue_profile = moving_average(
        blue_mask.mean(axis=0).astype(np.float32),
        max(7, int(width * 0.009)),
    )
    blue_threshold = max(0.30, float(np.percentile(blue_profile, 92)))
    blue_peaks = pick_peaks(
        blue_profile,
        threshold=blue_threshold,
        min_distance=max(8, int(width * 0.025)),
    )
    central_blue_peaks = [
        (score, x)
        for score, x in blue_peaks
        if width * 0.25 <= x <= width * 0.75
    ]
    central_wall_score = max((score for score, _ in central_blue_peaks), default=0.0)
    central_wall_x = None
    if central_blue_peaks:
        central_wall_x = int(round(max(central_blue_peaks)[1] / scale))

    best_gap, split_gap = score_gap_segments(
        foreground_profile,
        content_left,
        content_right,
        scale,
        original_width,
        split_x=split_x,
    )

    candidate_x = split_gap.x or best_gap.x or central_wall_x
    if candidate_x is not None and content_runs:
        candidate_scaled = int(round(candidate_x * scale))
        left_side_ratio = (candidate_scaled - content_left) / max(1.0, content_right - content_left)
        right_side_ratio = (content_right - candidate_scaled) / max(1.0, content_right - content_left)
    else:
        left_side_ratio = 0.0
        right_side_ratio = 0.0

    balanced_sides = (
        left_side_ratio >= thresholds.min_side_span_ratio
        and right_side_ratio >= thresholds.min_side_span_ratio
    )
    has_plausible_gap = best_gap.score >= thresholds.dual_gap_score
    has_split_gap = split_gap.score >= thresholds.split_gap_score
    has_central_wall = central_wall_score >= thresholds.central_wall_score
    has_dual_span = content_span_ratio >= thresholds.min_dual_span_ratio

    if has_dual_span and balanced_sides and (has_plausible_gap or has_split_gap or has_central_wall):
        eligibility = "dual_container"
        reasons.append("dual_span_with_balanced_seam")
        if has_plausible_gap:
            reasons.append("interior_air_gap_candidate")
        if has_split_gap:
            reasons.append("split_x_near_gap_candidate")
        if has_central_wall:
            reasons.append("central_fs6000_wall_candidate")
        confidence = min(
            0.98,
            0.52
            + min(0.22, content_span_ratio * 0.22)
            + min(0.16, max(best_gap.score, split_gap.score) * 1.4)
            + min(0.14, central_wall_score * 0.14),
        )
    elif (
        content_span_ratio <= thresholds.single_span_ratio
        and central_wall_score < thresholds.central_wall_score
        and best_gap.score < thresholds.dual_gap_score
    ):
        eligibility = "single_container"
        reasons.append("narrow_foreground_span")
        reasons.append("no_balanced_central_seam")
        confidence = min(
            0.95,
            0.62
            + min(0.20, (thresholds.single_span_ratio - content_span_ratio) * 1.8)
            + min(0.12, max(0.0, thresholds.central_wall_score - central_wall_score) * 0.4),
        )
    elif (
        len(merged_runs) <= 1
        and content_span_ratio < thresholds.min_dual_span_ratio
        and central_wall_score < thresholds.central_wall_score
    ):
        eligibility = "single_container"
        reasons.append("single_contiguous_foreground_block")
        reasons.append("no_central_fs6000_wall_candidate")
        confidence = 0.58
    else:
        eligibility = "uncertain"
        if not has_dual_span:
            reasons.append("foreground_span_below_dual_threshold")
        if not balanced_sides:
            reasons.append("candidate_seam_not_balanced")
        if not (has_plausible_gap or has_split_gap or has_central_wall):
            reasons.append("no_strong_visual_seam")
        confidence = min(
            0.74,
            0.35
            + min(0.16, content_span_ratio * 0.16)
            + min(0.12, max(best_gap.score, split_gap.score) * 1.2)
            + min(0.11, central_wall_score * 0.11),
        )

    metrics = {
        "image_width": original_width,
        "image_height": original_height,
        "analysis_width": width,
        "scale": round(scale, 6),
        "content_span_ratio": round(float(content_span_ratio), 4),
        "content_run_count": len(content_runs),
        "merged_content_run_count": len(merged_runs),
        "content_threshold": round(float(content_threshold), 4),
        "foreground_q25": round(fg_q25, 4),
        "foreground_q90": round(fg_q90, 4),
        "best_gap_score": round(float(best_gap.score), 4),
        "best_gap_x": best_gap.x,
        "best_gap_width_px": best_gap.width,
        "split_gap_score": round(float(split_gap.score), 4),
        "split_gap_x": split_gap.x,
        "split_gap_width_px": split_gap.width,
        "central_wall_score": round(float(central_wall_score), 4),
        "central_wall_x": central_wall_x,
        "blue_peak_count": len(blue_peaks),
        "central_blue_peak_count": len(central_blue_peaks),
        "candidate_seam_x": candidate_x,
        "left_side_ratio": round(float(left_side_ratio), 4),
        "right_side_ratio": round(float(right_side_ratio), 4),
    }
    return GateResult(
        eligibility=eligibility,
        confidence=round(float(confidence), 4),
        reason_codes=reasons,
        metrics=metrics,
    )


def load_runtime_gate() -> Callable[[bytes, str | None], Any] | None:
    if not SPLITTER_DIR.exists():
        return None
    splitter_path = str(SPLITTER_DIR)
    if splitter_path not in sys.path:
        sys.path.insert(0, splitter_path)
    try:
        from pipeline.visual_eligibility import classify_visual_eligibility as runtime_gate
    except Exception:
        return None
    return runtime_gate


def json_compact(value: Any) -> str:
    return json.dumps(value, default=str, separators=(",", ":"))


def gate_result_from_runtime(result: Any) -> GateResult:
    if hasattr(result, "to_metadata"):
        metadata = result.to_metadata()
    else:
        metadata = {}

    metrics: dict[str, Any] = {
        "gate_source": "runtime_visual_eligibility",
        "gate_processing_ms": getattr(result, "processing_ms", None),
        "runtime_image_width": getattr(result, "image_width", None),
        "runtime_image_height": getattr(result, "image_height", None),
    }

    candidate_frames = metadata.get("candidate_frame_positions", [])
    metrics["candidate_frame_positions_json"] = json_compact(candidate_frames)

    for key, value in (getattr(result, "metrics", None) or {}).items():
        metric_key = f"runtime_{key}"
        if isinstance(value, (dict, list, tuple)):
            metrics[metric_key] = json_compact(value)
        else:
            metrics[metric_key] = value

    return GateResult(
        eligibility=getattr(result, "label", "uncertain"),
        confidence=round(float(getattr(result, "confidence", 0.0)), 4),
        reason_codes=list(getattr(result, "reason_codes", ()) or ()),
        metrics=metrics,
    )


def classify_with_selected_gate(
    row: dict[str, Any],
    thresholds: GateThresholds,
    runtime_gate: Callable[[bytes, str | None], Any] | None,
    gate_mode: str,
) -> GateResult:
    image_data = row["image_data"]
    if runtime_gate is not None and gate_mode in ("auto", "runtime"):
        try:
            return gate_result_from_runtime(
                runtime_gate(bytes(image_data), row.get("scanner_type"))
            )
        except Exception as exc:
            if gate_mode == "runtime":
                raise
            local = classify_visual_eligibility_local(image_data, row.get("split_x"), thresholds)
            local.reason_codes.insert(0, "runtime_gate_failed")
            local.metrics["runtime_gate_error"] = str(exc)
            local.metrics["gate_source"] = "local_fallback_after_runtime_error"
            return local

    local = classify_visual_eligibility_local(image_data, row.get("split_x"), thresholds)
    local.metrics["gate_source"] = "local_downscaled_fallback"
    return local


def fetch_jobs(conn, args: argparse.Namespace) -> list[dict[str, Any]]:
    status_clause = ""
    params: list[Any] = []
    if args.status != "all":
        status_clause = "AND j.status = %s"
        params.append(args.status)

    sql = f"""
        SELECT
            j.id::text AS job_id,
            j.container_numbers,
            j.source_image_id::text AS source_image_id,
            j.scanner_type,
            j.status,
            j.best_strategy,
            j.best_score,
            j.split_x,
            j.image_width,
            j.image_height,
            j.created_at,
            j.completed_at,
            j.analyst_verdict,
            j.correct_split_x,
            j.ground_truth_split_x,
            j.image_data,
            fs.id::text AS fs6000_scan_id,
            fs.containernumber AS fs6000_container_number,
            fs.picnumber AS fs6000_picnumber,
            COUNT(a.id) AS assignment_count,
            COUNT(DISTINCT a.container_number) AS assigned_container_count,
            STRING_AGG(DISTINCT a.container_number, ',' ORDER BY a.container_number) AS assigned_containers
        FROM image_split_jobs j
        LEFT JOIN fs6000scans fs ON fs.id = j.source_image_id
        LEFT JOIN image_split_assignments a ON a.job_id = j.id
        WHERE j.image_data IS NOT NULL
          AND j.container_numbers <> 'TEST001,TEST002'
          AND (
              UPPER(COALESCE(j.scanner_type, '')) = 'FS6000'
              OR fs.id IS NOT NULL
          )
          {status_clause}
        GROUP BY
            j.id, fs.id, fs.containernumber, fs.picnumber
        ORDER BY j.created_at DESC
    """
    if args.limit is not None:
        sql += "\n        LIMIT %s"
        params.append(args.limit)

    cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
    cur.execute(sql, params)
    rows = [dict(row) for row in cur.fetchall()]
    cur.close()
    return rows


def flatten_row(row: dict[str, Any], gate: GateResult) -> dict[str, Any]:
    containers = parse_container_numbers(row.get("container_numbers"))
    status = row.get("status") or ""
    assignment_count = int(row.get("assignment_count") or 0)
    high_risk = (
        status.lower() == "completed"
        and len(containers) >= 2
        and gate.eligibility != "dual_container"
        and (row.get("split_x") is not None or assignment_count >= 2)
    )

    out = {
        "job_id": row.get("job_id"),
        "container_numbers": row.get("container_numbers"),
        "container_count": len(containers),
        "scanner_type": row.get("scanner_type"),
        "status": row.get("status"),
        "visual_eligibility": gate.eligibility,
        "visual_confidence": gate.confidence,
        "reason_codes": "|".join(gate.reason_codes),
        "high_risk_completed_job": high_risk,
        "assignment_count": assignment_count,
        "assigned_container_count": int(row.get("assigned_container_count") or 0),
        "assigned_containers": row.get("assigned_containers"),
        "best_strategy": row.get("best_strategy"),
        "best_score": row.get("best_score"),
        "split_x": row.get("split_x"),
        "image_width": row.get("image_width"),
        "image_height": row.get("image_height"),
        "created_at": row.get("created_at").isoformat() if row.get("created_at") else None,
        "completed_at": row.get("completed_at").isoformat() if row.get("completed_at") else None,
        "source_image_id": row.get("source_image_id"),
        "fs6000_scan_id": row.get("fs6000_scan_id"),
        "fs6000_container_number": row.get("fs6000_container_number"),
        "fs6000_picnumber": row.get("fs6000_picnumber"),
        "analyst_verdict": row.get("analyst_verdict"),
        "correct_split_x": row.get("correct_split_x"),
        "ground_truth_split_x": row.get("ground_truth_split_x"),
    }
    out.update(gate.metrics)
    return out


def write_outputs(out_dir: Path, rows: list[dict[str, Any]], summary: dict[str, Any]) -> tuple[Path, Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    json_path = out_dir / f"fs6000_splitter_contamination_audit_{timestamp}.json"
    csv_path = out_dir / f"fs6000_splitter_contamination_audit_{timestamp}.csv"

    with json_path.open("w", encoding="utf-8") as f:
        json.dump({"summary": summary, "jobs": rows}, f, indent=2, default=str)

    fieldnames: list[str] = []
    for row in rows:
        for key in row:
            if key not in fieldnames:
                fieldnames.append(key)
    with csv_path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    return json_path, csv_path


def write_audit_notes(conn, rows: list[dict[str, Any]]) -> int:
    high_risk_rows = [row for row in rows if row["high_risk_completed_job"]]
    if not high_risk_rows:
        return 0

    cur = conn.cursor()
    stamp = datetime.now(timezone.utc).isoformat()
    updated = 0
    try:
        for row in high_risk_rows:
            note = (
                f"[{stamp}] FS6000 splitter contamination audit: "
                f"visual_eligibility={row['visual_eligibility']} "
                f"confidence={row['visual_confidence']} "
                f"reasons={row['reason_codes']}"
            )
            cur.execute(
                """
                UPDATE image_split_jobs
                   SET ground_truth_notes =
                       CASE
                         WHEN ground_truth_notes IS NULL OR ground_truth_notes = ''
                           THEN %s
                         ELSE ground_truth_notes || E'\n' || %s
                       END
                 WHERE id = %s::uuid
                """,
                (note, note, row["job_id"]),
            )
            updated += cur.rowcount
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        cur.close()
    return updated


def build_summary(
    rows: list[dict[str, Any]],
    args: argparse.Namespace,
    thresholds: GateThresholds,
    db_updates: int,
    gate_source: str,
) -> dict[str, Any]:
    counts = {
        "dual_container": 0,
        "single_container": 0,
        "uncertain": 0,
    }
    for row in rows:
        counts[row["visual_eligibility"]] = counts.get(row["visual_eligibility"], 0) + 1

    return {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "read_only": db_updates == 0,
        "db_updates": db_updates,
        "filters": {
            "status": args.status,
            "limit": args.limit,
            "gate": args.gate,
        },
        "gate_source": gate_source,
        "thresholds": thresholds.__dict__,
        "total_jobs": len(rows),
        "classification_counts": counts,
        "high_risk_completed_jobs": sum(1 for row in rows if row["high_risk_completed_job"]),
        "high_risk_job_ids": [
            row["job_id"] for row in rows if row["high_risk_completed_job"]
        ],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Audit FS6000 image_split_jobs for visually single/uncertain two-container contamination."
    )
    parser.add_argument("--limit", type=int, default=None, help="Maximum jobs to audit.")
    parser.add_argument(
        "--status",
        choices=["completed", "pending", "processing", "failed", "all"],
        default="completed",
        help="Job status filter (default: completed).",
    )
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=DEFAULT_OUT_DIR,
        help=f"Output directory for CSV/JSON (default: {DEFAULT_OUT_DIR}).",
    )
    parser.add_argument(
        "--max-width",
        type=int,
        default=GateThresholds.max_width,
        help="Downscaled analysis width for the local fallback gate.",
    )
    parser.add_argument(
        "--gate",
        choices=["auto", "runtime", "local"],
        default="auto",
        help="Visual gate source: auto prefers services/image-splitter/pipeline/visual_eligibility.py when present.",
    )
    parser.add_argument(
        "--allow-db-mutations",
        action="store_true",
        help="Required guard for any DB mutation. Has no effect without a write action.",
    )
    parser.add_argument(
        "--write-audit-notes",
        action="store_true",
        help="Append audit notes to ground_truth_notes for high-risk rows. Requires --allow-db-mutations.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.limit is not None and args.limit < 0:
        print("ERROR: --limit must be zero or greater.", file=sys.stderr)
        return 2
    if args.write_audit_notes and not args.allow_db_mutations:
        print(
            "ERROR: --write-audit-notes mutates image_split_jobs and requires --allow-db-mutations.",
            file=sys.stderr,
        )
        return 2

    thresholds = GateThresholds(max_width=args.max_width)
    runtime_gate = None if args.gate == "local" else load_runtime_gate()
    if args.gate == "runtime" and runtime_gate is None:
        print(
            "ERROR: --gate runtime requested, but services/image-splitter/pipeline/visual_eligibility.py could not be imported.",
            file=sys.stderr,
        )
        return 2
    gate_source = (
        "runtime_visual_eligibility"
        if runtime_gate is not None and args.gate in ("auto", "runtime")
        else "local_downscaled_fallback"
    )

    conn = connect()
    try:
        raw_rows = fetch_jobs(conn, args)
        print(
            f"[audit] fetched {len(raw_rows)} FS6000 splitter jobs "
            f"(status={args.status}, gate={gate_source})"
        )

        audited: list[dict[str, Any]] = []
        for index, row in enumerate(raw_rows, 1):
            gate = classify_with_selected_gate(row, thresholds, runtime_gate, args.gate)
            flat = flatten_row(row, gate)
            audited.append(flat)
            print(
                "[audit] "
                f"{index}/{len(raw_rows)} {flat['job_id'][:8]} "
                f"{flat['visual_eligibility']} conf={flat['visual_confidence']:.2f} "
                f"high_risk={str(flat['high_risk_completed_job']).lower()} "
                f"{flat['container_numbers']}",
                flush=True,
            )

        db_updates = 0
        if args.write_audit_notes:
            db_updates = write_audit_notes(conn, audited)

        summary = build_summary(audited, args, thresholds, db_updates, gate_source)
        json_path, csv_path = write_outputs(args.out_dir, audited, summary)

        print()
        print("[audit] summary")
        print(f"  total_jobs: {summary['total_jobs']}")
        for label, count in summary["classification_counts"].items():
            print(f"  {label}: {count}")
        print(f"  high_risk_completed_jobs: {summary['high_risk_completed_jobs']}")
        print(f"  db_updates: {db_updates}")
        print(f"  json: {json_path}")
        print(f"  csv:  {csv_path}")
    finally:
        conn.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
