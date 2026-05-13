"""Run shadow predictions from a local splitter model artifact."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.append(str(Path(__file__).resolve().parents[1]))
    from ml.baseline_model import load_json, predict_sample
    from ml.dataset import load_manifest
else:
    from .baseline_model import load_json, predict_sample
    from .dataset import load_manifest


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Generate append-only shadow predictions from a local splitter "
            "model JSON artifact. This does not write to the database."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--manifest", type=Path, required=True, help="Path to split_manifest.jsonl.")
    parser.add_argument("--model", type=Path, required=True, help="Path to model.json.")
    parser.add_argument("--output-jsonl", type=Path, help="Prediction JSONL path. Defaults to stdout.")
    parser.add_argument("--limit", type=int, help="Maximum rows to predict.")
    parser.add_argument(
        "--include-labels",
        action="store_true",
        help="Include manifest label fields in output for offline debugging.",
    )
    return parser


def run(args: argparse.Namespace) -> dict[str, int | str | None]:
    samples = load_manifest(args.manifest, limit=args.limit)
    artifact = load_json(args.model)

    handle = None
    try:
        if args.output_jsonl:
            args.output_jsonl.parent.mkdir(parents=True, exist_ok=True)
            handle = args.output_jsonl.open("w", encoding="utf-8", newline="\n")

        for sample in samples:
            prediction = predict_sample(sample, artifact)
            if args.include_labels:
                prediction["label"] = sample.raw.get("label")
            line = json.dumps(prediction, sort_keys=True)
            if handle:
                handle.write(line + "\n")
            else:
                print(line)
    finally:
        if handle:
            handle.close()

    return {
        "rows": len(samples),
        "output_jsonl": str(args.output_jsonl.resolve()) if args.output_jsonl else None,
    }


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.limit is not None and args.limit < 1:
        parser.error("--limit must be >= 1")
    summary = run(args)
    if args.output_jsonl:
        print("Local splitter shadow predictions")
        print(f"  Rows: {summary['rows']}")
        print(f"  Output: {summary['output_jsonl']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
