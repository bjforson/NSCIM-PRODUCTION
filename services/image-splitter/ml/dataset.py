"""Manifest loading helpers for the local image-splitter model scaffold."""

from __future__ import annotations

import hashlib
import json
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Iterator


EXPECTED_MANIFEST_SCHEMA_VERSION = "nscim.image_splitter.dataset_manifest.v1"


@dataclass(frozen=True)
class ManifestSample:
    """A single exported splitter manifest row."""

    row_number: int
    raw: dict[str, Any]

    @property
    def job_id(self) -> str:
        return str((self.raw.get("job") or {}).get("id") or f"row-{self.row_number}")

    @property
    def scanner_type(self) -> str:
        return str((self.raw.get("job") or {}).get("scanner_type") or "unknown")

    @property
    def dataset_split(self) -> str:
        return str((self.raw.get("dataset_split") or {}).get("name") or "unknown")

    @property
    def image_width(self) -> int | None:
        return optional_int((self.raw.get("image") or {}).get("width"))

    @property
    def image_height(self) -> int | None:
        return optional_int((self.raw.get("image") or {}).get("height"))

    @property
    def label_class(self) -> str:
        return str((self.raw.get("label") or {}).get("class") or "unlabeled")

    @property
    def label_source(self) -> str:
        return str((self.raw.get("label") or {}).get("source") or "unknown")

    @property
    def label_split_x(self) -> int | None:
        return optional_int((self.raw.get("label") or {}).get("split_x"))

    @property
    def has_positive_label(self) -> bool:
        return self.label_class == "split" and self.label_split_x is not None

    @property
    def has_negative_label(self) -> bool:
        return self.label_class == "no_split"


def optional_int(value: Any) -> int | None:
    if value is None:
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def optional_float(value: Any) -> float | None:
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def load_manifest(
    path: Path,
    *,
    dataset_splits: set[str] | None = None,
    label_classes: set[str] | None = None,
    limit: int | None = None,
) -> list[ManifestSample]:
    """Load an exported JSONL manifest.

    The loader validates only the stable fields this scaffold consumes. Unknown
    fields are preserved in ManifestSample.raw so future tools can reuse them.
    """
    if not path.exists():
        raise FileNotFoundError(f"Manifest not found: {path}")

    samples: list[ManifestSample] = []
    with path.open("r", encoding="utf-8") as handle:
        for row_number, line in enumerate(handle, start=1):
            text = line.strip()
            if not text:
                continue
            try:
                row = json.loads(text)
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSON on manifest line {row_number}: {exc}") from exc

            schema_version = row.get("schema_version")
            if schema_version and schema_version != EXPECTED_MANIFEST_SCHEMA_VERSION:
                raise ValueError(
                    "Unsupported manifest schema on line "
                    f"{row_number}: {schema_version!r}"
                )

            sample = ManifestSample(row_number=row_number, raw=row)
            if dataset_splits and sample.dataset_split not in dataset_splits:
                continue
            if label_classes and sample.label_class not in label_classes:
                continue
            samples.append(sample)
            if limit is not None and len(samples) >= limit:
                break

    return samples


def prediction_sources(sample: ManifestSample) -> Iterator[dict[str, Any]]:
    """Yield stored split candidates available at prediction time.

    The labels are not included here. These are the same production outputs the
    manifest already captured: current_best, stored candidate results, and
    optional teacher vision outputs.
    """
    width = sample.image_width
    current = sample.raw.get("current_best") or {}
    current_split_x = optional_int(current.get("split_x"))
    if current_split_x is not None:
        yield {
            "source_key": f"current_best:{current.get('strategy_name') or 'unknown'}",
            "kind": "current_best",
            "strategy_name": current.get("strategy_name") or "unknown",
            "result_id": None,
            "split_x": current_split_x,
            "normalized_split_x": normalize_split_x(current_split_x, width),
            "confidence": optional_float(current.get("score")),
        }

    for index, candidate in enumerate(sample.raw.get("candidates") or []):
        split_x = optional_int(candidate.get("split_x"))
        if split_x is None:
            continue
        strategy_name = candidate.get("strategy_name") or "unknown"
        yield {
            "source_key": f"candidate:{strategy_name}",
            "kind": "candidate",
            "strategy_name": strategy_name,
            "result_id": candidate.get("result_id"),
            "candidate_index": index,
            "split_x": split_x,
            "normalized_split_x": normalize_split_x(split_x, width),
            "confidence": optional_float(candidate.get("confidence")),
        }

    teacher = ((sample.raw.get("teacher_predictions") or {}).get("claude_vision") or {})
    teacher_split_x = optional_int(teacher.get("split_x"))
    if teacher_split_x is not None:
        yield {
            "source_key": "teacher:claude_vision",
            "kind": "teacher",
            "strategy_name": "claude_vision",
            "result_id": None,
            "split_x": teacher_split_x,
            "normalized_split_x": normalize_split_x(teacher_split_x, width),
            "confidence": optional_float(teacher.get("confidence")),
        }


def normalize_split_x(split_x: int | None, width: int | None) -> float | None:
    if split_x is None or not width or width <= 0:
        return None
    return round(float(split_x) / float(width), 8)


def clamp_split_x(split_x: float, width: int | None) -> int:
    rounded = int(round(split_x))
    if width is None or width <= 0:
        return max(0, rounded)
    return max(0, min(int(width), rounded))


def summarize_samples(samples: Iterable[ManifestSample]) -> dict[str, Any]:
    total = 0
    by_split: Counter[str] = Counter()
    by_scanner: Counter[str] = Counter()
    by_label_class: Counter[str] = Counter()
    by_label_source: Counter[str] = Counter()
    positive = 0
    negative = 0
    with_sources = 0

    for sample in samples:
        total += 1
        by_split[sample.dataset_split] += 1
        by_scanner[sample.scanner_type] += 1
        by_label_class[sample.label_class] += 1
        by_label_source[sample.label_source] += 1
        positive += 1 if sample.has_positive_label else 0
        negative += 1 if sample.has_negative_label else 0
        with_sources += 1 if any(prediction_sources(sample)) else 0

    return {
        "rows": total,
        "positive_split_labels": positive,
        "negative_no_split_labels": negative,
        "rows_with_prediction_sources": with_sources,
        "by_dataset_split": dict(sorted(by_split.items())),
        "by_scanner": dict(sorted(by_scanner.items())),
        "by_label_class": dict(sorted(by_label_class.items())),
        "by_label_source": dict(sorted(by_label_source.items())),
    }
