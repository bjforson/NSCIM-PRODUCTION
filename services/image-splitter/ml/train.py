"""Train the shadow-only local splitter baseline from an exported manifest."""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

if __package__ in (None, ""):
    sys.path.append(str(Path(__file__).resolve().parents[1]))
    from ml.baseline_model import dump_json, evaluate_model, load_json, train_model
    from ml.dataset import load_manifest, sha256_file, summarize_samples
else:
    from .baseline_model import dump_json, evaluate_model, load_json, train_model
    from .dataset import load_manifest, sha256_file, summarize_samples


DEFAULT_CONFIG_PATH = Path(__file__).resolve().parent / "configs" / "baseline_candidate_ranker.json"
DEFAULT_TEMPLATE_PATH = Path(__file__).resolve().parent / "templates" / "model_card_template.md"


def utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def parse_csv_set(raw: str) -> set[str]:
    return {item.strip() for item in raw.split(",") if item.strip()}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Train a shadow-only candidate-ranker baseline from the exported "
            "image-splitter manifest JSONL."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--manifest", type=Path, required=True, help="Path to split_manifest.jsonl.")
    parser.add_argument("--output-dir", type=Path, required=True, help="Directory for model artifacts.")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG_PATH, help="Baseline config JSON.")
    parser.add_argument("--model-version", help="Model version. Defaults to local-baseline-<utc>.")
    parser.add_argument("--training-run-id", help="Training run id. Defaults to train-<utc>.")
    parser.add_argument(
        "--train-splits",
        default="train",
        help="Comma-separated dataset_split names used for fitting, or all.",
    )
    parser.add_argument(
        "--eval-splits",
        default="val,test",
        help="Comma-separated dataset_split names evaluated after training, or all.",
    )
    parser.add_argument(
        "--min-positive-samples",
        type=int,
        default=5,
        help="Minimum positive split labels required unless --allow-small-dataset is set.",
    )
    parser.add_argument(
        "--allow-small-dataset",
        action="store_true",
        help="Allow training with fewer positive labels; useful for smoke tests only.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Print summaries without writing artifacts.")
    return parser


def select_by_split(samples, split_arg: str):
    selected = parse_csv_set(split_arg)
    if "all" in selected:
        return list(samples)
    return [sample for sample in samples if sample.dataset_split in selected]


def render_model_card(template_path: Path, values: dict[str, Any]) -> str:
    template = template_path.read_text(encoding="utf-8")
    rendered = template
    for key, value in values.items():
        rendered = rendered.replace("{{" + key + "}}", str(value))
    return rendered


def validate_training_rows(args: argparse.Namespace, train_rows) -> None:
    positive_count = sum(1 for sample in train_rows if sample.has_positive_label)
    if positive_count >= args.min_positive_samples:
        return
    if args.allow_small_dataset:
        return
    raise SystemExit(
        "Not enough positive split labels to train: "
        f"{positive_count} found, {args.min_positive_samples} required. "
        "Export more analyst/ground-truth labels or rerun with --allow-small-dataset "
        "for a smoke test."
    )


def run(args: argparse.Namespace) -> dict[str, Any]:
    if not args.config.exists():
        raise SystemExit(f"Config file not found: {args.config}")

    config = load_json(args.config)
    all_rows = load_manifest(args.manifest)
    train_rows = select_by_split(all_rows, args.train_splits)
    eval_rows = select_by_split(all_rows, args.eval_splits)
    validate_training_rows(args, train_rows)

    stamp = utc_stamp()
    model_version = args.model_version or f"local-baseline-{stamp}"
    training_run_id = args.training_run_id or f"train-{stamp}"
    manifest_hash = sha256_file(args.manifest)

    artifact = train_model(
        train_rows,
        config=config,
        model_version=model_version,
        training_run_id=training_run_id,
        manifest_path=str(args.manifest.resolve()),
        manifest_sha256=manifest_hash,
    )

    train_eval = evaluate_model(train_rows, artifact)
    eval_summary = evaluate_model(eval_rows, artifact) if eval_rows else None
    artifact["evaluation"] = {
        "train": train_eval,
        "requested_eval": eval_summary,
    }

    summary = {
        "model_name": artifact["model_name"],
        "model_version": artifact["model_version"],
        "training_run_id": artifact["training_run_id"],
        "dry_run": args.dry_run,
        "manifest": str(args.manifest.resolve()),
        "manifest_sha256": manifest_hash,
        "all_rows": summarize_samples(all_rows),
        "train_rows": summarize_samples(train_rows),
        "eval_rows": summarize_samples(eval_rows),
        "output_dir": str(args.output_dir.resolve()),
    }

    if args.dry_run:
        return summary

    args.output_dir.mkdir(parents=True, exist_ok=True)
    model_path = args.output_dir / "model.json"
    metadata_path = args.output_dir / "metadata.json"
    eval_path = args.output_dir / "evaluation_summary.json"
    config_path = args.output_dir / "config_effective.json"
    model_card_path = args.output_dir / "model_card.md"

    dump_json(artifact, model_path)
    artifact_sha = sha256_file(model_path)
    dump_json(config, config_path)
    dump_json({"train": train_eval, "requested_eval": eval_summary}, eval_path)

    metadata = {
        "schema_version": "nscim.image_splitter.local_model_artifact_metadata.v1",
        "created_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "model_name": artifact["model_name"],
        "model_version": artifact["model_version"],
        "training_run_id": artifact["training_run_id"],
        "production_mode": artifact["production_mode"],
        "model_artifact_uri": str(model_path.resolve()),
        "model_artifact_sha256": artifact_sha,
        "manifest_path": str(args.manifest.resolve()),
        "manifest_sha256": manifest_hash,
        "files": {
            "model": str(model_path.resolve()),
            "metadata": str(metadata_path.resolve()),
            "evaluation_summary": str(eval_path.resolve()),
            "config_effective": str(config_path.resolve()),
            "model_card": str(model_card_path.resolve()),
        },
    }
    dump_json(metadata, metadata_path)

    model_card = render_model_card(
        DEFAULT_TEMPLATE_PATH,
        {
            "model_name": artifact["model_name"],
            "model_version": artifact["model_version"],
            "training_run_id": artifact["training_run_id"],
            "created_at": artifact["created_at"],
            "manifest_path": str(args.manifest.resolve()),
            "manifest_sha256": manifest_hash,
            "train_rows": summary["train_rows"]["rows"],
            "train_positive_labels": summary["train_rows"]["positive_split_labels"],
            "train_negative_labels": summary["train_rows"]["negative_no_split_labels"],
            "eval_rows": summary["eval_rows"]["rows"],
            "production_mode": artifact["production_mode"],
            "artifact_sha256": artifact_sha,
        },
    )
    model_card_path.write_text(model_card, encoding="utf-8", newline="\n")

    summary["artifact"] = metadata
    return summary


def print_summary(summary: dict[str, Any]) -> None:
    print("Local splitter baseline training")
    print(f"  Dry run: {summary['dry_run']}")
    print(f"  Model: {summary['model_name']} {summary['model_version']}")
    print(f"  Training run: {summary['training_run_id']}")
    print(f"  Manifest rows: {summary['all_rows']['rows']}")
    print(f"  Train positive labels: {summary['train_rows']['positive_split_labels']}")
    print(f"  Eval rows: {summary['eval_rows']['rows']}")
    print(f"  Output dir: {summary['output_dir']}")
    if summary.get("artifact"):
        print(f"  Artifact sha256: {summary['artifact']['model_artifact_sha256']}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.min_positive_samples < 1:
        parser.error("--min-positive-samples must be >= 1")
    summary = run(args)
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
