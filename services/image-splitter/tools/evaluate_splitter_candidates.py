"""Evaluate stored splitter candidates against analyst/ground-truth labels.

The evaluator reads image_split_jobs and image_split_results only. It does not
rerun strategies or modify database rows.
"""

from __future__ import annotations

import argparse
import math
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from splitter_dataset_common import (
    EVALUATION_DETAIL_SCHEMA_VERSION,
    EVALUATION_SUMMARY_SCHEMA_VERSION,
    add_common_filter_args,
    connect_database,
    dump_json,
    fetch_candidates,
    fetch_jobs,
    filters_to_summary,
    label_class,
    mean,
    normalized_split_x,
    percentile,
    round_optional,
    split_container_numbers,
    utc_now_iso,
    write_jsonl_record,
)


def parse_int_list(raw: str) -> list[int]:
    values = [int(item.strip()) for item in raw.split(",") if item.strip()]
    if not values:
        raise argparse.ArgumentTypeError("at least one tolerance is required")
    if any(value < 0 for value in values):
        raise argparse.ArgumentTypeError("tolerances cannot be negative")
    return sorted(set(values))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate existing image_split_results candidates and selected "
            "best/ranker outputs against labelled split_x values."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    add_common_filter_args(parser)
    parser.add_argument(
        "--tolerances",
        type=parse_int_list,
        default=parse_int_list("5,10,20,30,50"),
        help="Pixel-error tolerance buckets.",
    )
    parser.add_argument(
        "--primary-tolerance",
        type=int,
        default=20,
        help="Tolerance used for review-required/success summary.",
    )
    parser.add_argument(
        "--strategy",
        action="append",
        default=[],
        help="Optional stored strategy filter. May be repeated or comma-separated.",
    )
    parser.add_argument(
        "--exclude-negatives",
        action="store_true",
        help="Exclude no-split labels such as ground_truth_split_x=-1.",
    )
    parser.add_argument(
        "--no-current-best",
        action="store_true",
        help="Do not evaluate the job-level selected split_x as current_best.",
    )
    parser.add_argument(
        "--no-ranker-selected",
        action="store_true",
        help="Do not evaluate metadata.ranker_selected candidates as ranker_selected.",
    )
    parser.add_argument(
        "--summary-json",
        type=Path,
        help="Optional path for machine-readable summary JSON.",
    )
    parser.add_argument(
        "--details-jsonl",
        type=Path,
        help="Optional path for per-candidate evaluation rows.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Compute and print the summary without writing report files.",
    )
    return parser


def parse_strategy_filter(values: list[str]) -> set[str]:
    names: set[str] = set()
    for raw in values:
        for item in raw.split(","):
            item = item.strip()
            if item:
                names.add(item)
    return names


def truthy(value: Any) -> bool:
    if value is True:
        return True
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "on"}
    return False


def choose_one_per_strategy(candidates: list[dict[str, Any]]) -> list[dict[str, Any]]:
    chosen: dict[str, dict[str, Any]] = {}
    for candidate in candidates:
        strategy = str(candidate.get("strategy_name") or "unknown")
        previous = chosen.get(strategy)
        if previous is None:
            chosen[strategy] = candidate
            continue
        prev_conf = previous.get("confidence")
        next_conf = candidate.get("confidence")
        prev_score = float(prev_conf) if prev_conf is not None else -1.0
        next_score = float(next_conf) if next_conf is not None else -1.0
        if next_score > prev_score:
            chosen[strategy] = candidate
    return list(chosen.values())


def build_predictions(
    jobs: list[dict[str, Any]],
    candidates_by_job: dict[str, list[dict[str, Any]]],
    *,
    strategy_filter: set[str],
    include_current_best: bool,
    include_ranker_selected: bool,
) -> list[dict[str, Any]]:
    predictions: list[dict[str, Any]] = []

    for job in jobs:
        job_id = str(job["job_id"])
        candidates = choose_one_per_strategy(candidates_by_job.get(job_id, []))

        for candidate in candidates:
            strategy_name = str(candidate.get("strategy_name") or "unknown")
            if strategy_filter and strategy_name not in strategy_filter:
                continue
            predictions.append({
                "prediction_name": strategy_name,
                "prediction_kind": "stored_candidate",
                "source_strategy": strategy_name,
                "job": job,
                "result_id": str(candidate.get("result_id")),
                "split_x": candidate.get("split_x"),
                "confidence": candidate.get("confidence"),
                "processing_ms": candidate.get("processing_ms"),
                "metadata": candidate.get("strategy_metadata") if isinstance(candidate.get("strategy_metadata"), dict) else {},
            })

        if include_current_best and job.get("best_split_x") is not None:
            predictions.append({
                "prediction_name": "current_best",
                "prediction_kind": "job_selection",
                "source_strategy": job.get("best_strategy"),
                "job": job,
                "result_id": None,
                "split_x": job.get("best_split_x"),
                "confidence": job.get("best_score"),
                "processing_ms": None,
                "metadata": {},
            })

        if include_ranker_selected:
            selected = [
                candidate for candidate in candidates
                if truthy((candidate.get("strategy_metadata") or {}).get("ranker_selected"))
            ]
            if selected:
                selected.sort(
                    key=lambda item: float(item.get("confidence") or 0.0),
                    reverse=True,
                )
                candidate = selected[0]
                predictions.append({
                    "prediction_name": "ranker_selected",
                    "prediction_kind": "metadata_selection",
                    "source_strategy": candidate.get("strategy_name"),
                    "job": job,
                    "result_id": str(candidate.get("result_id")),
                    "split_x": candidate.get("split_x"),
                    "confidence": candidate.get("confidence"),
                    "processing_ms": candidate.get("processing_ms"),
                    "metadata": candidate.get("strategy_metadata") if isinstance(candidate.get("strategy_metadata"), dict) else {},
                })

    return predictions


def prediction_detail(
    prediction: dict[str, Any],
    tolerances: list[int],
    primary_tolerance: int,
) -> dict[str, Any]:
    job = prediction["job"]
    label_split_x = job.get("label_split_x")
    predicted_split_x = prediction.get("split_x")
    cls = label_class(label_split_x)
    image_width = job.get("image_width")

    error_px = None
    abs_error_px = None
    normalized_abs_error = None
    within = {str(tol): None for tol in tolerances}
    if cls == "split" and predicted_split_x is not None:
        error_px = int(predicted_split_x) - int(label_split_x)
        abs_error_px = abs(error_px)
        normalized_abs_error = normalized_split_x(abs_error_px, image_width)
        within = {str(tol): abs_error_px <= tol for tol in tolerances}

    out_of_bounds = False
    if predicted_split_x is not None and image_width:
        out_of_bounds = int(predicted_split_x) < 0 or int(predicted_split_x) > int(image_width)

    return {
        "schema_version": EVALUATION_DETAIL_SCHEMA_VERSION,
        "job_id": str(job.get("job_id")),
        "source_image_id": str(job.get("source_image_id")) if job.get("source_image_id") else None,
        "scanner_type": job.get("scanner_type"),
        "container_numbers": split_container_numbers(job.get("container_numbers")),
        "created_at": job.get("created_at"),
        "image_width": image_width,
        "image_height": job.get("image_height"),
        "label": {
            "class": cls,
            "split_x": label_split_x,
            "normalized_split_x": normalized_split_x(label_split_x, image_width),
            "source": job.get("label_source"),
        },
        "prediction": {
            "name": prediction.get("prediction_name"),
            "kind": prediction.get("prediction_kind"),
            "source_strategy": prediction.get("source_strategy"),
            "result_id": prediction.get("result_id"),
            "split_x": predicted_split_x,
            "normalized_split_x": normalized_split_x(predicted_split_x, image_width),
            "confidence": prediction.get("confidence"),
            "processing_ms": prediction.get("processing_ms"),
        },
        "error": {
            "px": error_px,
            "abs_px": abs_error_px,
            "normalized_abs": normalized_abs_error,
            "within_tolerances": within,
            "within_primary_tolerance": (
                abs_error_px <= primary_tolerance if abs_error_px is not None else None
            ),
            "out_of_bounds": out_of_bounds,
        },
    }


def metric_denominator(jobs: list[dict[str, Any]], scanner: str | None = None) -> list[dict[str, Any]]:
    if scanner is None:
        return jobs
    return [job for job in jobs if str(job.get("scanner_type") or "unknown") == scanner]


def compute_metrics_for_scope(
    jobs: list[dict[str, Any]],
    predictions: list[dict[str, Any]],
    *,
    tolerances: list[int],
    primary_tolerance: int,
    scanner: str | None = None,
) -> list[dict[str, Any]]:
    scoped_jobs = metric_denominator(jobs, scanner)
    scoped_job_ids = {str(job["job_id"]) for job in scoped_jobs}
    positive_jobs = [job for job in scoped_jobs if label_class(job.get("label_split_x")) == "split"]
    negative_jobs = [job for job in scoped_jobs if label_class(job.get("label_split_x")) == "no_split"]
    positive_job_ids = {str(job["job_id"]) for job in positive_jobs}
    negative_job_ids = {str(job["job_id"]) for job in negative_jobs}

    by_name: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for prediction in predictions:
        job = prediction["job"]
        if str(job["job_id"]) in scoped_job_ids:
            by_name[str(prediction["prediction_name"])].append(prediction)

    metrics: list[dict[str, Any]] = []
    for name in sorted(by_name):
        preds = by_name[name]
        pred_job_ids = {str(pred["job"]["job_id"]) for pred in preds}
        positive_preds = [pred for pred in preds if str(pred["job"]["job_id"]) in positive_job_ids]
        negative_preds = [pred for pred in preds if str(pred["job"]["job_id"]) in negative_job_ids]

        details = [
            prediction_detail(prediction, tolerances, primary_tolerance)
            for prediction in positive_preds
        ]
        abs_errors = [
            float(detail["error"]["abs_px"])
            for detail in details
            if detail["error"]["abs_px"] is not None
        ]
        errors = [
            float(detail["error"]["px"])
            for detail in details
            if detail["error"]["px"] is not None
        ]
        normalized_errors = [
            float(detail["error"]["normalized_abs"])
            for detail in details
            if detail["error"]["normalized_abs"] is not None
        ]
        out_of_bounds = sum(1 for detail in details if detail["error"]["out_of_bounds"])
        within_counts = {
            str(tol): sum(1 for detail in details if detail["error"]["within_tolerances"].get(str(tol)) is True)
            for tol in tolerances
        }
        within_rates_predicted = {
            key: round_optional(value / len(abs_errors), 4) if abs_errors else None
            for key, value in within_counts.items()
        }
        success_rates_all_positive = {
            key: round_optional(value / len(positive_jobs), 4) if positive_jobs else None
            for key, value in within_counts.items()
        }

        within_primary = sum(1 for detail in details if detail["error"]["within_primary_tolerance"] is True)
        missed_split_count = len(positive_jobs) - len({str(pred["job"]["job_id"]) for pred in positive_preds})
        false_split_count = len({str(pred["job"]["job_id"]) for pred in negative_preds})
        review_required_count = (
            missed_split_count
            + (len(positive_preds) - within_primary)
            + false_split_count
        )

        rmse = math.sqrt(sum(error * error for error in errors) / len(errors)) if errors else None
        metrics.append({
            "strategy": name,
            "scope": scanner or "all",
            "labelled_jobs": len(scoped_jobs),
            "positive_label_jobs": len(positive_jobs),
            "negative_label_jobs": len(negative_jobs),
            "predictions": len(preds),
            "coverage": round_optional(len(pred_job_ids) / len(scoped_jobs), 4) if scoped_jobs else None,
            "positive_predictions": len(positive_preds),
            "negative_predictions": len(negative_preds),
            "missed_split_count": missed_split_count,
            "missed_split_rate": round_optional(missed_split_count / len(positive_jobs), 4) if positive_jobs else None,
            "false_split_count": false_split_count,
            "false_split_rate": round_optional(false_split_count / len(negative_jobs), 4) if negative_jobs else None,
            "review_required_count": review_required_count,
            "review_required_rate": round_optional(review_required_count / len(scoped_jobs), 4) if scoped_jobs else None,
            "mean_error_px": round_optional(mean(errors), 4),
            "mean_abs_error_px": round_optional(mean(abs_errors), 4),
            "median_abs_error_px": round_optional(percentile(abs_errors, 0.50), 4),
            "p90_abs_error_px": round_optional(percentile(abs_errors, 0.90), 4),
            "p95_abs_error_px": round_optional(percentile(abs_errors, 0.95), 4),
            "max_abs_error_px": round_optional(max(abs_errors) if abs_errors else None, 4),
            "rmse_px": round_optional(rmse, 4),
            "mean_normalized_abs_error": round_optional(mean(normalized_errors), 6),
            "out_of_bounds_count": out_of_bounds,
            "within_tolerance_counts": within_counts,
            "within_tolerance_rates_predicted": within_rates_predicted,
            "success_rates_all_positive": success_rates_all_positive,
            "primary_tolerance_px": primary_tolerance,
        })

    return metrics


def label_summary(jobs: list[dict[str, Any]]) -> dict[str, Any]:
    scanners: Counter[str] = Counter()
    classes: Counter[str] = Counter()
    sources: Counter[str] = Counter()
    for job in jobs:
        scanners[str(job.get("scanner_type") or "unknown")] += 1
        classes[label_class(job.get("label_split_x"))] += 1
        sources[str(job.get("label_source") or "unknown")] += 1
    return {
        "total": len(jobs),
        "by_scanner": dict(sorted(scanners.items())),
        "by_label_class": dict(sorted(classes.items())),
        "by_label_source": dict(sorted(sources.items())),
    }


def print_summary(summary: dict[str, Any]) -> None:
    print("Image-splitter candidate evaluation summary")
    print(f"  Dry run: {summary['dry_run']}")
    print(f"  Labelled jobs: {summary['labels']['total']}")
    print("  Label classes:")
    for name, count in summary["labels"]["by_label_class"].items():
        print(f"    {name}: {count}")
    print("  Scanners:")
    for name, count in summary["labels"]["by_scanner"].items():
        print(f"    {name}: {count}")

    primary = str(summary["options"]["primary_tolerance_px"])
    print(f"\nOverall metrics (primary tolerance <= {primary}px)")
    header = (
        "strategy",
        "pred",
        "cov",
        "mae",
        "med",
        "p95",
        f"<={primary}px",
        "miss",
        "false",
        "review",
    )
    print("  " + "{:<24} {:>5} {:>6} {:>8} {:>8} {:>8} {:>8} {:>5} {:>5} {:>7}".format(*header))
    for metric in summary["overall_metrics"]:
        within = metric["within_tolerance_rates_predicted"].get(primary)
        print(
            "  "
            + "{:<24} {:>5} {:>6} {:>8} {:>8} {:>8} {:>8} {:>5} {:>5} {:>7}".format(
                metric["strategy"][:24],
                metric["predictions"],
                fmt_rate(metric["coverage"]),
                fmt_num(metric["mean_abs_error_px"]),
                fmt_num(metric["median_abs_error_px"]),
                fmt_num(metric["p95_abs_error_px"]),
                fmt_rate(within),
                metric["missed_split_count"],
                metric["false_split_count"],
                fmt_rate(metric["review_required_rate"]),
            )
        )

    if summary.get("outputs"):
        for key, value in summary["outputs"].items():
            print(f"  {key}: {value}")


def fmt_num(value: Any) -> str:
    if value is None:
        return "-"
    return f"{float(value):.2f}"


def fmt_rate(value: Any) -> str:
    if value is None:
        return "-"
    return f"{float(value) * 100:.1f}%"


def run(args: argparse.Namespace) -> dict[str, Any]:
    strategy_filter = parse_strategy_filter(args.strategy)
    conn = connect_database(args.database_url)
    try:
        jobs = fetch_jobs(
            conn,
            args,
            include_image=False,
            require_image=False,
            include_unlabeled=False,
            exclude_negatives=args.exclude_negatives,
        )
        candidates_by_job = fetch_candidates(conn, [job["job_id"] for job in jobs])
    finally:
        conn.close()

    predictions = build_predictions(
        jobs,
        candidates_by_job,
        strategy_filter=strategy_filter,
        include_current_best=not args.no_current_best,
        include_ranker_selected=not args.no_ranker_selected,
    )

    overall_metrics = compute_metrics_for_scope(
        jobs,
        predictions,
        tolerances=args.tolerances,
        primary_tolerance=args.primary_tolerance,
    )

    scanners = sorted({str(job.get("scanner_type") or "unknown") for job in jobs})
    by_scanner = {
        scanner: compute_metrics_for_scope(
            jobs,
            predictions,
            tolerances=args.tolerances,
            primary_tolerance=args.primary_tolerance,
            scanner=scanner,
        )
        for scanner in scanners
    }

    details = [
        prediction_detail(prediction, args.tolerances, args.primary_tolerance)
        for prediction in predictions
    ]

    outputs: dict[str, str] = {}
    if args.summary_json and not args.dry_run:
        args.summary_json.parent.mkdir(parents=True, exist_ok=True)
    if args.details_jsonl and not args.dry_run:
        args.details_jsonl.parent.mkdir(parents=True, exist_ok=True)

    summary = {
        "schema_version": EVALUATION_SUMMARY_SCHEMA_VERSION,
        "detail_schema_version": EVALUATION_DETAIL_SCHEMA_VERSION,
        "generated_at": utc_now_iso(),
        "dry_run": args.dry_run,
        "filters": filters_to_summary(args),
        "options": {
            "tolerances_px": args.tolerances,
            "primary_tolerance_px": args.primary_tolerance,
            "strategy_filter": sorted(strategy_filter),
            "exclude_negatives": args.exclude_negatives,
            "include_current_best": not args.no_current_best,
            "include_ranker_selected": not args.no_ranker_selected,
        },
        "labels": label_summary(jobs),
        "prediction_count": len(predictions),
        "overall_metrics": overall_metrics,
        "by_scanner": by_scanner,
    }

    if args.summary_json:
        outputs["summary_json"] = str(args.summary_json.resolve())
        if not args.dry_run:
            dump_json(summary, args.summary_json)

    if args.details_jsonl:
        outputs["details_jsonl"] = str(args.details_jsonl.resolve())
        if not args.dry_run:
            with args.details_jsonl.open("w", encoding="utf-8", newline="\n") as handle:
                for detail in details:
                    write_jsonl_record(handle, detail)

    if args.dry_run and outputs:
        outputs = {key: f"{value} (not written: dry-run)" for key, value in outputs.items()}
    summary["outputs"] = outputs
    return summary


def validate_args(parser: argparse.ArgumentParser, args: argparse.Namespace) -> None:
    if args.primary_tolerance < 0:
        parser.error("--primary-tolerance cannot be negative")
    if args.primary_tolerance not in args.tolerances:
        args.tolerances = sorted(set(args.tolerances + [args.primary_tolerance]))


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    validate_args(parser, args)
    summary = run(args)
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
