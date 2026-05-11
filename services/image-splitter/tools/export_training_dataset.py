"""Export labelled image-splitter training data to files + JSONL manifest.

The export is read-only against PostgreSQL. It writes only to the output
directory supplied on the command line and supports --dry-run for planning.
"""

from __future__ import annotations

import argparse
import io
from collections import Counter
from pathlib import Path
from typing import Any

from splitter_dataset_common import (
    DATASET_LABEL_SCHEMA_VERSION,
    DATASET_MANIFEST_SCHEMA_VERSION,
    EXPORT_SUMMARY_SCHEMA_VERSION,
    add_common_filter_args,
    assign_dataset_split,
    bytes_from_db,
    candidate_to_manifest,
    connect_database,
    dump_json,
    fetch_candidates,
    fetch_jobs,
    filters_to_summary,
    image_extension,
    label_class,
    normalized_split_x,
    parse_split_ratios,
    path_as_manifest,
    sha256_bytes,
    sha256_file,
    split_container_numbers,
    stable_group_key,
    summarize_counts,
    utc_now_iso,
    write_jsonl_record,
)


GROUP_BY_CHOICES = (
    "image_hash",
    "source_image_id",
    "container_numbers",
    "scanner_container",
    "scanner_date",
    "job_id",
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Export labelled image-splitter scans, labels, stored candidate "
            "predictions, and deterministic train/val/test manifest rows."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    add_common_filter_args(parser)
    parser.add_argument(
        "--output-dir",
        type=Path,
        help="Dataset output directory. Required unless --dry-run is used.",
    )
    parser.add_argument(
        "--manifest-name",
        default="manifests/split_manifest.jsonl",
        help="Manifest path relative to --output-dir.",
    )
    parser.add_argument(
        "--summary-name",
        default="manifests/export_summary.json",
        help="Summary JSON path relative to --output-dir.",
    )
    parser.add_argument(
        "--split-ratios",
        type=parse_split_ratios,
        default=parse_split_ratios("80,10,10"),
        help="Train/val/test ratios as three comma-separated numbers.",
    )
    parser.add_argument(
        "--split-seed",
        default="nscim-image-splitter-v1",
        help="Seed included in deterministic group hashing.",
    )
    parser.add_argument(
        "--group-by",
        choices=GROUP_BY_CHOICES,
        default="source_image_id",
        help="Group key used before hashing into train/val/test splits.",
    )
    parser.add_argument(
        "--include-unlabeled",
        action="store_true",
        help="Include jobs without a usable label as label_class=unlabeled.",
    )
    parser.add_argument(
        "--exclude-negatives",
        action="store_true",
        help="Exclude negative/no-split labels such as ground_truth_split_x=-1.",
    )
    parser.add_argument(
        "--no-candidates",
        action="store_true",
        help="Do not include image_split_results candidates in manifest rows.",
    )
    parser.add_argument(
        "--no-derivatives",
        action="store_true",
        help="Only write original images, not top-strip or preview JPEGs.",
    )
    parser.add_argument(
        "--top-strip-ratio",
        type=float,
        default=0.22,
        help="Height ratio used when writing images/top_strip derivatives.",
    )
    parser.add_argument(
        "--preview-max-width",
        type=int,
        default=1400,
        help="Maximum preview/top-strip width in pixels; 0 disables resizing.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Read and summarize matching rows without writing files.",
    )
    return parser


def validate_args(parser: argparse.ArgumentParser, args: argparse.Namespace) -> None:
    if not args.dry_run and args.output_dir is None:
        parser.error("--output-dir is required unless --dry-run is used")
    if args.top_strip_ratio <= 0 or args.top_strip_ratio > 1:
        parser.error("--top-strip-ratio must be in the range (0, 1]")
    if args.preview_max_width < 0:
        parser.error("--preview-max-width cannot be negative")


def resize_to_max_width(image, max_width: int):
    if max_width <= 0 or image.width <= max_width:
        return image, 1.0
    scale = max_width / float(image.width)
    new_height = max(1, int(round(image.height * scale)))
    return image.resize((max_width, new_height)), scale


def write_derivatives(
    image_bytes: bytes,
    output_dir: Path,
    split_name: str,
    job_id: str,
    label_split_x: int | None,
    top_strip_ratio: float,
    preview_max_width: int,
) -> dict[str, str]:
    from PIL import Image, ImageDraw

    paths: dict[str, str] = {}
    with Image.open(io.BytesIO(image_bytes)) as img:
        rgb = img.convert("RGB")

        top_h = max(1, int(round(rgb.height * top_strip_ratio)))
        top_strip = rgb.crop((0, 0, rgb.width, top_h))
        top_strip, _ = resize_to_max_width(top_strip, preview_max_width)
        top_path = output_dir / "images" / "top_strip" / split_name / f"{job_id}.jpg"
        top_path.parent.mkdir(parents=True, exist_ok=True)
        top_strip.save(top_path, format="JPEG", quality=90, optimize=True)
        paths["top_strip_path"] = path_as_manifest(top_path, output_dir)

        preview, scale = resize_to_max_width(rgb.copy(), preview_max_width)
        if label_split_x is not None and int(label_split_x) >= 0:
            draw = ImageDraw.Draw(preview)
            x = int(round(int(label_split_x) * scale))
            line_width = max(2, int(round(preview.width * 0.003)))
            draw.line([(x, 0), (x, preview.height)], fill=(0, 220, 60), width=line_width)
        preview_path = output_dir / "images" / "previews" / split_name / f"{job_id}.jpg"
        preview_path.parent.mkdir(parents=True, exist_ok=True)
        preview.save(preview_path, format="JPEG", quality=88, optimize=True)
        paths["preview_path"] = path_as_manifest(preview_path, output_dir)

    return paths


def build_label_document(row: dict[str, Any]) -> dict[str, Any]:
    split_x = row.get("label_split_x")
    return {
        "schema_version": DATASET_LABEL_SCHEMA_VERSION,
        "job_id": str(row.get("job_id")),
        "label": {
            "class": label_class(split_x),
            "split_x": split_x,
            "normalized_split_x": normalized_split_x(split_x, row.get("image_width")),
            "source": row.get("label_source"),
            "ground_truth_split_x": row.get("ground_truth_split_x"),
            "ground_truth_set_by": row.get("ground_truth_set_by"),
            "ground_truth_set_at": row.get("ground_truth_set_at"),
            "ground_truth_notes": row.get("ground_truth_notes"),
            "correct_split_x": row.get("correct_split_x"),
            "analyst_verdict": row.get("analyst_verdict"),
            "reviewed_by": row.get("reviewed_by"),
            "reviewed_at": row.get("reviewed_at"),
        },
    }


def build_manifest_record(
    row: dict[str, Any],
    *,
    image_sha256: str,
    image_path: str | None,
    derivative_paths: dict[str, str],
    label_path: str | None,
    group_by: str,
    group_key: str,
    group_hash: str,
    split_bucket: float,
    split_name: str,
    candidates: list[dict[str, Any]],
) -> dict[str, Any]:
    split_x = row.get("label_split_x")
    for candidate in candidates:
        candidate["normalized_split_x"] = normalized_split_x(
            candidate.get("split_x"),
            row.get("image_width"),
        )

    return {
        "schema_version": DATASET_MANIFEST_SCHEMA_VERSION,
        "job": {
            "id": str(row.get("job_id")),
            "source_image_id": str(row.get("source_image_id")) if row.get("source_image_id") else None,
            "scanner_type": row.get("scanner_type"),
            "status": row.get("status"),
            "container_numbers": split_container_numbers(row.get("container_numbers")),
            "container_numbers_raw": row.get("container_numbers"),
            "created_at": row.get("created_at"),
            "completed_at": row.get("completed_at"),
        },
        "image": {
            "original_path": image_path,
            "top_strip_path": derivative_paths.get("top_strip_path"),
            "preview_path": derivative_paths.get("preview_path"),
            "sha256": image_sha256,
            "bytes": row.get("image_bytes"),
            "width": row.get("image_width"),
            "height": row.get("image_height"),
        },
        "label": {
            "path": label_path,
            "schema_version": DATASET_LABEL_SCHEMA_VERSION,
            "class": label_class(split_x),
            "split_x": split_x,
            "normalized_split_x": normalized_split_x(split_x, row.get("image_width")),
            "source": row.get("label_source"),
            "reviewed_by": row.get("reviewed_by"),
            "reviewed_at": row.get("reviewed_at"),
            "ground_truth_set_by": row.get("ground_truth_set_by"),
            "ground_truth_set_at": row.get("ground_truth_set_at"),
        },
        "dataset_split": {
            "name": split_name,
            "group_by": group_by,
            "group_key": group_key,
            "group_hash": group_hash,
            "bucket": round(split_bucket, 8),
        },
        "current_best": {
            "strategy_name": row.get("best_strategy"),
            "split_x": row.get("best_split_x"),
            "normalized_split_x": normalized_split_x(row.get("best_split_x"), row.get("image_width")),
            "score": row.get("best_score"),
        },
        "teacher_predictions": {
            "claude_vision": {
                "split_x": row.get("claude_vision_split_x"),
                "normalized_split_x": normalized_split_x(
                    row.get("claude_vision_split_x"),
                    row.get("image_width"),
                ),
                "confidence": row.get("claude_vision_confidence"),
                "model": row.get("claude_vision_model"),
                "ran_at": row.get("claude_vision_ran_at"),
                "reasoning": row.get("claude_vision_reasoning"),
            }
        },
        "candidates": candidates,
    }


def print_summary(summary: dict[str, Any]) -> None:
    print("Image-splitter dataset export summary")
    print(f"  Dry run: {summary['dry_run']}")
    print(f"  Matched jobs: {summary['matched_jobs']}")
    print(f"  Manifest rows: {summary['manifest_rows']}")
    print(f"  Groups: {summary['groups']['count']} ({summary['groups']['duplicate_groups']} duplicate group(s))")
    print("  Dataset split counts:")
    for name in ("train", "val", "test"):
        print(f"    {name}: {summary['split_counts'].get(name, 0)}")
    print("  Label classes:")
    for name, count in summary["source_counts"]["by_label_class"].items():
        print(f"    {name}: {count}")
    print("  Scanners:")
    for name, count in summary["source_counts"]["by_scanner"].items():
        print(f"    {name}: {count}")
    if summary.get("output"):
        print(f"  Output dir: {summary['output']['output_dir']}")
        print(f"  Manifest: {summary['output']['manifest_path']}")
        if summary["output"].get("manifest_sha256"):
            print(f"  Manifest sha256: {summary['output']['manifest_sha256']}")


def run(args: argparse.Namespace) -> dict[str, Any]:
    include_image = not args.dry_run or args.group_by == "image_hash"
    conn = connect_database(args.database_url)
    try:
        rows = fetch_jobs(
            conn,
            args,
            include_image=include_image,
            require_image=True,
            include_unlabeled=args.include_unlabeled,
            exclude_negatives=args.exclude_negatives,
        )
        candidates_by_job = {} if args.no_candidates else fetch_candidates(
            conn,
            [row["job_id"] for row in rows],
        )
    finally:
        conn.close()

    output_dir = args.output_dir.resolve() if args.output_dir else None
    manifest_path = output_dir / args.manifest_name if output_dir else None
    summary_path = output_dir / args.summary_name if output_dir else None
    if output_dir and not args.dry_run:
        manifest_path.parent.mkdir(parents=True, exist_ok=True)

    split_counts: Counter[str] = Counter()
    group_counts: Counter[str] = Counter()
    candidate_strategy_counts: Counter[str] = Counter()
    manifest_rows = 0
    total_image_bytes = 0

    manifest_handle = None
    try:
        if manifest_path and not args.dry_run:
            manifest_handle = manifest_path.open("w", encoding="utf-8", newline="\n")

        for row in rows:
            image_bytes = bytes_from_db(row.pop("image_data", None))
            image_sha256 = sha256_bytes(image_bytes) if image_bytes else None
            group_key = stable_group_key(row, args.group_by, image_sha256)
            split_name, group_hash, split_bucket = assign_dataset_split(
                group_key,
                args.split_ratios,
                args.split_seed,
            )
            group_counts[group_key] += 1
            split_counts[split_name] += 1
            total_image_bytes += int(row.get("image_bytes") or len(image_bytes) or 0)

            candidates = [
                candidate_to_manifest(candidate)
                for candidate in candidates_by_job.get(str(row["job_id"]), [])
            ]
            for candidate in candidates:
                candidate_strategy_counts[str(candidate.get("strategy_name") or "unknown")] += 1

            image_path = None
            label_path = None
            derivative_paths: dict[str, str] = {}

            if output_dir and not args.dry_run:
                job_id = str(row["job_id"])
                ext = image_extension(image_bytes)
                original_path = output_dir / "images" / "original" / split_name / f"{job_id}.{ext}"
                original_path.parent.mkdir(parents=True, exist_ok=True)
                original_path.write_bytes(image_bytes)
                image_path = path_as_manifest(original_path, output_dir)

                label_doc = build_label_document(row)
                label_file = output_dir / "labels" / split_name / f"{job_id}.json"
                dump_json(label_doc, label_file)
                label_path = path_as_manifest(label_file, output_dir)

                if not args.no_derivatives:
                    derivative_paths = write_derivatives(
                        image_bytes,
                        output_dir,
                        split_name,
                        job_id,
                        row.get("label_split_x"),
                        args.top_strip_ratio,
                        args.preview_max_width,
                    )

            record = build_manifest_record(
                row,
                image_sha256=image_sha256,
                image_path=image_path,
                derivative_paths=derivative_paths,
                label_path=label_path,
                group_by=args.group_by,
                group_key=group_key,
                group_hash=group_hash,
                split_bucket=split_bucket,
                split_name=split_name,
                candidates=candidates,
            )

            if manifest_handle is not None:
                write_jsonl_record(manifest_handle, record)
            manifest_rows += 1
    finally:
        if manifest_handle is not None:
            manifest_handle.close()

    duplicate_groups = [count for count in group_counts.values() if count > 1]
    output_summary = None
    if output_dir:
        output_summary = {
            "output_dir": str(output_dir),
            "manifest_path": str(manifest_path),
            "summary_path": str(summary_path),
            "manifest_sha256": (
                sha256_file(manifest_path)
                if manifest_path and manifest_path.exists() and not args.dry_run
                else None
            ),
        }

    summary = {
        "schema_version": EXPORT_SUMMARY_SCHEMA_VERSION,
        "manifest_schema_version": DATASET_MANIFEST_SCHEMA_VERSION,
        "generated_at": utc_now_iso(),
        "dry_run": args.dry_run,
        "filters": filters_to_summary(args),
        "export_options": {
            "group_by": args.group_by,
            "split_seed": args.split_seed,
            "split_ratios": {
                "train": args.split_ratios[0],
                "val": args.split_ratios[1],
                "test": args.split_ratios[2],
            },
            "include_unlabeled": args.include_unlabeled,
            "exclude_negatives": args.exclude_negatives,
            "include_candidates": not args.no_candidates,
            "write_derivatives": not args.no_derivatives,
        },
        "matched_jobs": len(rows),
        "manifest_rows": manifest_rows,
        "total_image_bytes": total_image_bytes,
        "source_counts": summarize_counts(rows),
        "split_counts": dict(split_counts),
        "candidate_strategy_counts": dict(sorted(candidate_strategy_counts.items())),
        "groups": {
            "count": len(group_counts),
            "duplicate_groups": len(duplicate_groups),
            "largest_group_size": max(duplicate_groups) if duplicate_groups else 1 if group_counts else 0,
        },
        "output": output_summary,
    }

    if summary_path and not args.dry_run:
        dump_json(summary, summary_path)

    return summary


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    validate_args(parser, args)
    summary = run(args)
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
