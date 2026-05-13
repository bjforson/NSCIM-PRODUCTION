"""Evaluate a local splitter model artifact against an exported manifest."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.append(str(Path(__file__).resolve().parents[1]))
    from ml.baseline_model import dump_json, evaluate_model, load_json
    from ml.dataset import load_manifest, summarize_samples
else:
    from .baseline_model import dump_json, evaluate_model, load_json
    from .dataset import load_manifest, summarize_samples


def parse_csv_set(raw: str) -> set[str]:
    return {item.strip() for item in raw.split(",") if item.strip()}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Evaluate a shadow local splitter model JSON artifact.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--manifest", type=Path, required=True, help="Path to split_manifest.jsonl.")
    parser.add_argument("--model", type=Path, required=True, help="Path to model.json.")
    parser.add_argument("--splits", default="all", help="Comma-separated dataset_split names, or all.")
    parser.add_argument("--output-json", type=Path, help="Optional evaluation summary path.")
    return parser


def run(args: argparse.Namespace):
    split_filter = parse_csv_set(args.splits)
    dataset_splits = None if "all" in split_filter else split_filter
    samples = load_manifest(args.manifest, dataset_splits=dataset_splits)
    artifact = load_json(args.model)
    summary = evaluate_model(samples, artifact)
    summary["input"] = {
        "manifest": str(args.manifest.resolve()),
        "model": str(args.model.resolve()),
        "splits": sorted(split_filter),
        "samples": summarize_samples(samples),
    }
    if args.output_json:
        dump_json(summary, args.output_json)
    return summary


def print_summary(summary) -> None:
    metrics = summary["metrics"]
    print("Local splitter model evaluation")
    print(f"  Model: {summary['model']['name']} {summary['model']['version']}")
    print(f"  Rows: {summary['data']['rows']}")
    print(f"  Positive labels: {metrics['positive_split_labels']}")
    print(f"  Negative labels: {metrics['negative_no_split_labels']}")
    print(f"  Mean abs error px: {metrics['mean_abs_error_px']}")
    print(f"  Median abs error px: {metrics['median_abs_error_px']}")
    print(
        "  Within "
        f"{metrics['primary_tolerance_px']}px: "
        f"{metrics['within_primary_tolerance_rate']}"
    )
    print(f"  False split rate: {metrics['false_split_rate']}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    summary = run(args)
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
