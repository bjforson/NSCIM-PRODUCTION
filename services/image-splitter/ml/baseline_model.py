"""Shadow-only baseline model for local splitter training.

The model learns which exported candidate/current-best source is historically
closest to analyst or ground-truth labels. It stores only JSON metadata and
does not require heavyweight ML dependencies.
"""

from __future__ import annotations

import json
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

from .dataset import (
    ManifestSample,
    clamp_split_x,
    normalize_split_x,
    prediction_sources,
    summarize_samples,
)


MODEL_SCHEMA_VERSION = "nscim.image_splitter.local_baseline.v1"
PREDICTION_SCHEMA_VERSION = "nscim.image_splitter.local_prediction.v1"
EVALUATION_SCHEMA_VERSION = "nscim.image_splitter.local_model_evaluation.v1"


DEFAULT_CONFIG: dict[str, Any] = {
    "model_name": "nscim-splitter-candidate-ranker-baseline",
    "primary_tolerance_px": 20,
    "min_samples_for_source": 3,
    "unknown_source_penalty_px": 9999.0,
    "fallback_split_ratio": 0.5,
    "allowed_source_kinds": ["current_best", "candidate", "teacher"],
    "no_split_handling": "not_modeled_shadow_only",
}


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def merged_config(config: dict[str, Any] | None) -> dict[str, Any]:
    out = dict(DEFAULT_CONFIG)
    if config:
        out.update(config)
    out["primary_tolerance_px"] = int(out["primary_tolerance_px"])
    out["min_samples_for_source"] = int(out["min_samples_for_source"])
    out["unknown_source_penalty_px"] = float(out["unknown_source_penalty_px"])
    out["fallback_split_ratio"] = float(out["fallback_split_ratio"])
    return out


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def dump_json(data: dict[str, Any], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(data, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def train_model(
    samples: list[ManifestSample],
    *,
    config: dict[str, Any] | None = None,
    model_version: str,
    training_run_id: str,
    manifest_path: str,
    manifest_sha256: str,
) -> dict[str, Any]:
    cfg = merged_config(config)
    allowed_kinds = set(cfg["allowed_source_kinds"])
    primary_tolerance = int(cfg["primary_tolerance_px"])

    errors_by_scope: dict[str, dict[str, list[int]]] = defaultdict(lambda: defaultdict(list))
    coverage_by_source: Counter[str] = Counter()
    positive_rows = [s for s in samples if s.has_positive_label]

    for sample in positive_rows:
        label = sample.label_split_x
        if label is None:
            continue
        for source in prediction_sources(sample):
            if source["kind"] not in allowed_kinds:
                continue
            split_x = source.get("split_x")
            if split_x is None:
                continue
            source_key = str(source["source_key"])
            error = int(split_x) - int(label)
            errors_by_scope["all"][source_key].append(error)
            errors_by_scope[f"scanner:{sample.scanner_type}"][source_key].append(error)
            coverage_by_source[source_key] += 1

    priors_by_scope: dict[str, dict[str, Any]] = {}
    for scope, by_source in errors_by_scope.items():
        priors_by_scope[scope] = {
            source_key: _source_stats(errors, primary_tolerance)
            for source_key, errors in sorted(by_source.items())
        }

    artifact = {
        "schema_version": MODEL_SCHEMA_VERSION,
        "model_name": cfg["model_name"],
        "model_version": model_version,
        "training_run_id": training_run_id,
        "created_at": utc_now_iso(),
        "production_mode": "shadow_only",
        "training_data": {
            "manifest_path": manifest_path,
            "manifest_sha256": manifest_sha256,
            "rows_used": len(samples),
            "positive_split_rows_used": len(positive_rows),
            "summary": summarize_samples(samples),
        },
        "config": cfg,
        "priors": {
            "by_scope": priors_by_scope,
            "source_coverage": dict(sorted(coverage_by_source.items())),
        },
        "limitations": [
            "This baseline ranks existing split candidates; it does not inspect pixels directly.",
            "No-split/single-container labels are counted in evaluation but not modeled yet.",
            "Artifacts are for shadow evaluation only until promoted by explicit production wiring.",
        ],
    }
    return artifact


def _source_stats(errors: list[int], primary_tolerance: int) -> dict[str, Any]:
    abs_errors = [abs(e) for e in errors]
    within = [e for e in abs_errors if e <= primary_tolerance]
    return {
        "count": len(errors),
        "bias_px_median": round(float(statistics.median(errors)), 4),
        "mean_error_px": round(float(statistics.fmean(errors)), 4),
        "mean_abs_error_px": round(float(statistics.fmean(abs_errors)), 4),
        "median_abs_error_px": round(float(statistics.median(abs_errors)), 4),
        "p90_abs_error_px": round(_percentile(abs_errors, 0.90), 4),
        "within_primary_tolerance_rate": round(len(within) / len(errors), 4) if errors else None,
    }


def _percentile(values: list[int] | list[float], pct: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(float(v) for v in values)
    if len(ordered) == 1:
        return ordered[0]
    pos = (len(ordered) - 1) * pct
    lower = int(pos)
    upper = min(lower + 1, len(ordered) - 1)
    if lower == upper:
        return ordered[lower]
    lower_weight = upper - pos
    upper_weight = pos - lower
    return ordered[lower] * lower_weight + ordered[upper] * upper_weight


def predict_sample(sample: ManifestSample, artifact: dict[str, Any]) -> dict[str, Any]:
    cfg = merged_config(artifact.get("config") or {})
    allowed_kinds = set(cfg["allowed_source_kinds"])
    sources = [s for s in prediction_sources(sample) if s["kind"] in allowed_kinds]
    width = sample.image_width

    if not sources:
        fallback_x = clamp_split_x(float(width or 0) * cfg["fallback_split_ratio"], width)
        return _prediction_document(
            sample,
            artifact,
            split_x=fallback_x,
            confidence=0.0,
            uncertainty=1.0,
            status="fallback_no_sources",
            source=None,
            source_stats=None,
        )

    selected_source = None
    selected_stats = None
    selected_score = None
    for source in sources:
        stats = _stats_for_source(sample, source, artifact, cfg)
        score = _source_score(source, stats, cfg)
        if selected_score is None or score < selected_score:
            selected_score = score
            selected_source = source
            selected_stats = stats

    assert selected_source is not None
    bias = float((selected_stats or {}).get("bias_px_median") or 0.0)
    corrected = clamp_split_x(float(selected_source["split_x"]) - bias, width)
    confidence = _prediction_confidence(selected_source, selected_stats)
    uncertainty = round(1.0 - confidence, 4)

    return _prediction_document(
        sample,
        artifact,
        split_x=corrected,
        confidence=confidence,
        uncertainty=uncertainty,
        status="completed",
        source=selected_source,
        source_stats=selected_stats,
    )


def _stats_for_source(
    sample: ManifestSample,
    source: dict[str, Any],
    artifact: dict[str, Any],
    cfg: dict[str, Any],
) -> dict[str, Any] | None:
    min_count = int(cfg["min_samples_for_source"])
    priors = ((artifact.get("priors") or {}).get("by_scope") or {})
    source_key = str(source["source_key"])
    scanner_scope = f"scanner:{sample.scanner_type}"
    scanner_stats = (priors.get(scanner_scope) or {}).get(source_key)
    if scanner_stats and int(scanner_stats.get("count") or 0) >= min_count:
        return scanner_stats
    all_stats = (priors.get("all") or {}).get(source_key)
    if all_stats:
        return all_stats
    return None


def _source_score(source: dict[str, Any], stats: dict[str, Any] | None, cfg: dict[str, Any]) -> tuple[float, float]:
    if not stats:
        base = float(cfg["unknown_source_penalty_px"])
    else:
        base = float(stats.get("median_abs_error_px") or cfg["unknown_source_penalty_px"])
    confidence = float(source.get("confidence") or 0.0)
    return base, -confidence


def _prediction_confidence(source: dict[str, Any], stats: dict[str, Any] | None) -> float:
    learned = None if not stats else stats.get("within_primary_tolerance_rate")
    native = source.get("confidence")
    values = [float(v) for v in (learned, native) if v is not None]
    if not values:
        return 0.0
    return round(max(0.0, min(1.0, sum(values) / len(values))), 4)


def _prediction_document(
    sample: ManifestSample,
    artifact: dict[str, Any],
    *,
    split_x: int,
    confidence: float,
    uncertainty: float,
    status: str,
    source: dict[str, Any] | None,
    source_stats: dict[str, Any] | None,
) -> dict[str, Any]:
    return {
        "schema_version": PREDICTION_SCHEMA_VERSION,
        "job_id": sample.job_id,
        "scanner_type": sample.scanner_type,
        "dataset_split": sample.dataset_split,
        "model": {
            "name": artifact.get("model_name"),
            "version": artifact.get("model_version"),
            "training_run_id": artifact.get("training_run_id"),
            "production_mode": artifact.get("production_mode"),
        },
        "status": status,
        "split_x": split_x,
        "normalized_split_x": normalize_split_x(split_x, sample.image_width),
        "confidence": confidence,
        "uncertainty": uncertainty,
        "source": source,
        "source_stats": source_stats,
        "metadata": {
            "image_width": sample.image_width,
            "image_height": sample.image_height,
            "no_split_handling": (artifact.get("config") or {}).get("no_split_handling"),
        },
    }


def evaluate_model(samples: list[ManifestSample], artifact: dict[str, Any]) -> dict[str, Any]:
    predictions = [predict_sample(sample, artifact) for sample in samples]
    positive_pairs = [
        (sample, prediction)
        for sample, prediction in zip(samples, predictions)
        if sample.has_positive_label and sample.label_split_x is not None
    ]
    negative_pairs = [
        (sample, prediction)
        for sample, prediction in zip(samples, predictions)
        if sample.has_negative_label
    ]

    primary_tolerance = int(merged_config(artifact.get("config") or {})["primary_tolerance_px"])
    abs_errors = [
        abs(int(pred["split_x"]) - int(sample.label_split_x))
        for sample, pred in positive_pairs
    ]
    within = [value for value in abs_errors if value <= primary_tolerance]
    false_splits = [
        pred for _sample, pred in negative_pairs
        if pred.get("split_x") is not None and pred.get("status") != "no_split"
    ]

    by_scanner: dict[str, list[tuple[ManifestSample, dict[str, Any]]]] = defaultdict(list)
    for sample, prediction in zip(samples, predictions):
        by_scanner[sample.scanner_type].append((sample, prediction))

    return {
        "schema_version": EVALUATION_SCHEMA_VERSION,
        "generated_at": utc_now_iso(),
        "model": {
            "name": artifact.get("model_name"),
            "version": artifact.get("model_version"),
            "training_run_id": artifact.get("training_run_id"),
        },
        "data": summarize_samples(samples),
        "metrics": _metrics_from_errors(abs_errors, len(positive_pairs), len(negative_pairs), len(false_splits), primary_tolerance),
        "by_scanner": {
            scanner: _scope_metrics(pairs, primary_tolerance)
            for scanner, pairs in sorted(by_scanner.items())
        },
    }


def _scope_metrics(
    pairs: list[tuple[ManifestSample, dict[str, Any]]],
    primary_tolerance: int,
) -> dict[str, Any]:
    positive_pairs = [
        (sample, prediction)
        for sample, prediction in pairs
        if sample.has_positive_label and sample.label_split_x is not None
    ]
    negative_pairs = [(sample, prediction) for sample, prediction in pairs if sample.has_negative_label]
    abs_errors = [
        abs(int(prediction["split_x"]) - int(sample.label_split_x))
        for sample, prediction in positive_pairs
    ]
    false_splits = [
        prediction for _sample, prediction in negative_pairs
        if prediction.get("split_x") is not None and prediction.get("status") != "no_split"
    ]
    return _metrics_from_errors(
        abs_errors,
        len(positive_pairs),
        len(negative_pairs),
        len(false_splits),
        primary_tolerance,
    )


def _metrics_from_errors(
    abs_errors: list[int],
    positive_count: int,
    negative_count: int,
    false_split_count: int,
    primary_tolerance: int,
) -> dict[str, Any]:
    within_count = sum(1 for value in abs_errors if value <= primary_tolerance)
    return {
        "positive_split_labels": positive_count,
        "negative_no_split_labels": negative_count,
        "mean_abs_error_px": round(float(statistics.fmean(abs_errors)), 4) if abs_errors else None,
        "median_abs_error_px": round(float(statistics.median(abs_errors)), 4) if abs_errors else None,
        "p90_abs_error_px": round(_percentile(abs_errors, 0.90), 4) if abs_errors else None,
        "within_primary_tolerance_count": within_count,
        "within_primary_tolerance_rate": round(within_count / positive_count, 4) if positive_count else None,
        "primary_tolerance_px": primary_tolerance,
        "false_split_count": false_split_count,
        "false_split_rate": round(false_split_count / negative_count, 4) if negative_count else None,
    }
