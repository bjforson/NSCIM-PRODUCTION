"""Read-only operational report for the NSCIM image splitter.

The report intentionally uses the same lightweight database helper as the
dataset/evaluation CLIs. It opens a read-only PostgreSQL transaction and only
reads splitter tables, so it is safe to run during production review work.
"""

from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import date, datetime
from decimal import Decimal
from typing import Any

from splitter_dataset_common import connect_database, label_sql_parts, utc_now_iso


NEGATIVE_VERDICTS = {
    "single_container",
    "visual_single",
    "visualsingle",
    "bad_image",
    "badimage",
    "scanner_decode_failure",
    "decode_failure",
}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Print a read-only operational snapshot for splitter queues, label "
            "coverage, prediction runs, strategy distribution, and training readiness."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--database-url",
        help=(
            "PostgreSQL URL. Defaults to IMAGE_SPLITTER_DATABASE_URL, "
            "DATABASE_URL_SYNC, DATABASE_URL, then localhost nickscan_production."
        ),
    )
    parser.add_argument(
        "--recent-hours",
        type=int,
        default=72,
        help="Recent window used for review-ready queue counts.",
    )
    parser.add_argument(
        "--strategy-limit",
        type=int,
        default=12,
        help="Maximum current-best strategies to print.",
    )
    parser.add_argument(
        "--min-total-labels",
        type=int,
        default=500,
        help="Training readiness warning threshold for all labelled examples.",
    )
    parser.add_argument(
        "--min-positive-labels",
        type=int,
        default=300,
        help="Training readiness warning threshold for positive split examples.",
    )
    parser.add_argument(
        "--min-negative-labels",
        type=int,
        default=50,
        help="Training readiness warning threshold for no-split/negative examples.",
    )
    parser.add_argument(
        "--min-labels-per-scanner",
        type=int,
        default=50,
        help="Training readiness warning threshold for each scanner with completed jobs.",
    )
    parser.add_argument(
        "--max-review-backlog",
        type=int,
        default=200,
        help="Warning threshold for completed unreviewed jobs.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit machine-readable JSON instead of the console report.",
    )
    return parser


def normalize_scanner(value: Any) -> str:
    text = "" if value is None else str(value).strip()
    return text or "unknown"


def normalize_token(value: Any) -> str:
    return str(value or "").strip().lower()


def json_default(value: Any) -> Any:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    if isinstance(value, Decimal):
        return float(value)
    return str(value)


def fetch_all(conn, sql: str, params: tuple[Any, ...] = ()) -> list[dict[str, Any]]:
    from psycopg2.extras import RealDictCursor

    with conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(sql, params)
        return [dict(row) for row in cur.fetchall()]


def fetch_one(conn, sql: str, params: tuple[Any, ...] = ()) -> dict[str, Any]:
    rows = fetch_all(conn, sql, params)
    return rows[0] if rows else {}


def table_exists(conn, table_name: str) -> bool:
    row = fetch_one(conn, "SELECT to_regclass(%s) AS table_name", (table_name,))
    return row.get("table_name") is not None


def count_rows_by(conn, table_name: str, group_columns: list[str]) -> dict[str, Any]:
    if not table_exists(conn, table_name):
        return {"exists": False, "total": 0, "groups": {}}

    groups: dict[str, dict[str, int]] = {}
    total = int(fetch_one(conn, f"SELECT COUNT(*) AS n FROM {table_name}")["n"])
    for column in group_columns:
        rows = fetch_all(
            conn,
            f"""
            SELECT COALESCE(NULLIF({column}::text, ''), 'unknown') AS key, COUNT(*) AS n
            FROM {table_name}
            GROUP BY 1
            ORDER BY n DESC, key ASC
            """,
        )
        groups[column] = {str(row["key"]): int(row["n"]) for row in rows}

    return {"exists": True, "total": total, "groups": groups}


def fetch_queue_counts(conn, recent_hours: int) -> dict[str, Any]:
    by_status_rows = fetch_all(
        conn,
        """
        SELECT COALESCE(NULLIF(LOWER(status), ''), 'unknown') AS status, COUNT(*) AS n
        FROM image_split_jobs
        GROUP BY 1
        ORDER BY n DESC, status ASC
        """,
    )

    summary = fetch_one(
        conn,
        """
        SELECT
            COUNT(*) AS total_jobs,
            COUNT(*) FILTER (WHERE LOWER(COALESCE(status, '')) = 'pending') AS pending,
            COUNT(*) FILTER (WHERE LOWER(COALESCE(status, '')) = 'processing') AS processing,
            COUNT(*) FILTER (WHERE LOWER(COALESCE(status, '')) = 'completed') AS completed,
            COUNT(*) FILTER (WHERE LOWER(COALESCE(status, '')) = 'failed') AS failed,
            COUNT(*) FILTER (
                WHERE LOWER(COALESCE(status, '')) = 'completed'
                  AND analyst_verdict IS NULL
            ) AS completed_unreviewed,
            COUNT(*) FILTER (
                WHERE LOWER(COALESCE(status, '')) = 'failed'
                  AND analyst_verdict IS NULL
            ) AS failed_unreviewed,
            COUNT(*) FILTER (
                WHERE LOWER(COALESCE(status, '')) = 'completed'
                  AND analyst_verdict IS NOT NULL
            ) AS completed_reviewed
        FROM image_split_jobs
        """,
    )

    review_ready = fetch_one(
        conn,
        """
        SELECT
            COUNT(DISTINCT j.id) AS all_time,
            COUNT(DISTINCT j.id) FILTER (
                WHERE j.created_at >= NOW() - (%s * INTERVAL '1 hour')
            ) AS recent,
            COUNT(r.id) AS candidate_results,
            COUNT(r.id) FILTER (
                WHERE r.left_image IS NOT NULL AND r.right_image IS NOT NULL
            ) AS candidate_results_with_crops
        FROM image_split_jobs j
        JOIN image_split_results r ON r.job_id = j.id
        WHERE LOWER(COALESCE(j.status, '')) = 'completed'
          AND j.analyst_verdict IS NULL
          AND r.left_image IS NOT NULL
          AND r.right_image IS NOT NULL
        """,
        (recent_hours,),
    )

    return {
        "by_status": {str(row["status"]): int(row["n"]) for row in by_status_rows},
        "summary": {key: int(value or 0) for key, value in summary.items()},
        "review_ready": {
            "recent_hours": recent_hours,
            "all_time": int(review_ready.get("all_time") or 0),
            "recent": int(review_ready.get("recent") or 0),
            "candidate_results": int(review_ready.get("candidate_results") or 0),
            "candidate_results_with_crops": int(
                review_ready.get("candidate_results_with_crops") or 0
            ),
        },
    }


def fetch_label_coverage(conn) -> dict[str, Any]:
    label_expr, label_source_expr = label_sql_parts("any", alias="j")
    rows = fetch_all(
        conn,
        f"""
        WITH labels AS (
            SELECT
                COALESCE(NULLIF(j.scanner_type, ''), 'unknown') AS scanner,
                LOWER(COALESCE(j.status, '')) AS status,
                LOWER(COALESCE(j.analyst_verdict, '')) AS verdict,
                ({label_expr}) AS label_split_x,
                ({label_source_expr}) AS label_source,
                j.ground_truth_split_x,
                j.correct_split_x
            FROM image_split_jobs j
        )
        SELECT
            scanner,
            COUNT(*) AS total_jobs,
            COUNT(*) FILTER (WHERE status = 'completed') AS completed_jobs,
            COUNT(*) FILTER (WHERE label_split_x IS NOT NULL) AS labelled_jobs,
            COUNT(*) FILTER (WHERE label_split_x >= 0) AS positive_labels,
            COUNT(*) FILTER (WHERE label_split_x < 0) AS negative_labels,
            COUNT(*) FILTER (WHERE label_source = 'ground_truth') AS ground_truth_labels,
            COUNT(*) FILTER (WHERE label_source = 'analyst_correct') AS analyst_correct_labels,
            COUNT(*) FILTER (WHERE label_source = 'analyst_negative') AS analyst_negative_labels,
            COUNT(*) FILTER (WHERE verdict = 'approved') AS approved_verdicts,
            COUNT(*) FILTER (WHERE verdict = 'rejected') AS rejected_verdicts,
            COUNT(*) FILTER (WHERE verdict = 'single_container') AS single_container_verdicts,
            COUNT(*) FILTER (WHERE verdict = 'bad_image') AS bad_image_verdicts,
            COUNT(*) FILTER (WHERE verdict = 'uncertain') AS uncertain_verdicts
        FROM labels
        GROUP BY scanner
        ORDER BY labelled_jobs DESC, completed_jobs DESC, scanner ASC
        """,
    )

    by_scanner: list[dict[str, Any]] = []
    totals: Counter[str] = Counter()
    for row in rows:
        item = {
            key: int(value or 0) if key != "scanner" else str(value)
            for key, value in row.items()
        }
        completed = item["completed_jobs"]
        item["coverage_rate"] = (
            round(item["labelled_jobs"] / completed, 4) if completed else None
        )
        by_scanner.append(item)
        for key, value in item.items():
            if key not in {"scanner", "coverage_rate"}:
                totals[key] += int(value or 0)

    source_rows = fetch_all(
        conn,
        f"""
        WITH labels AS (
            SELECT ({label_source_expr}) AS label_source
            FROM image_split_jobs j
        )
        SELECT COALESCE(label_source, 'unlabelled') AS source, COUNT(*) AS n
        FROM labels
        GROUP BY 1
        ORDER BY n DESC, source ASC
        """,
    )
    verdict_rows = fetch_all(
        conn,
        """
        SELECT COALESCE(NULLIF(LOWER(analyst_verdict), ''), 'unreviewed') AS verdict,
               COUNT(*) AS n
        FROM image_split_jobs
        GROUP BY 1
        ORDER BY n DESC, verdict ASC
        """,
    )

    total_completed = totals["completed_jobs"]
    total_labelled = totals["labelled_jobs"]
    return {
        "totals": {
            **dict(totals),
            "coverage_rate": (
                round(total_labelled / total_completed, 4) if total_completed else None
            ),
        },
        "by_scanner": by_scanner,
        "by_label_source": {str(row["source"]): int(row["n"]) for row in source_rows},
        "by_verdict": {str(row["verdict"]): int(row["n"]) for row in verdict_rows},
    }


def fetch_strategy_distribution(conn, limit: int) -> list[dict[str, Any]]:
    rows = fetch_all(
        conn,
        """
        SELECT
            COALESCE(NULLIF(best_strategy, ''), 'unknown') AS strategy,
            COUNT(*) AS jobs,
            COUNT(*) FILTER (WHERE analyst_verdict IS NOT NULL) AS reviewed_jobs,
            AVG(best_score) AS avg_score
        FROM image_split_jobs
        WHERE LOWER(COALESCE(status, '')) = 'completed'
        GROUP BY 1
        ORDER BY jobs DESC, strategy ASC
        LIMIT %s
        """,
        (limit,),
    )
    return [
        {
            "strategy": str(row["strategy"]),
            "jobs": int(row["jobs"] or 0),
            "reviewed_jobs": int(row["reviewed_jobs"] or 0),
            "avg_score": (
                round(float(row["avg_score"]), 4) if row.get("avg_score") is not None else None
            ),
        }
        for row in rows
    ]


def fetch_prediction_runs(conn) -> dict[str, Any]:
    local = count_rows_by(
        conn,
        "image_split_local_model_prediction_runs",
        ["status", "run_purpose", "model_name", "model_version"],
    )
    remote = count_rows_by(
        conn,
        "image_split_remote_vision_runs",
        ["status", "run_purpose", "provider", "model_name"],
    )
    legacy_claude = fetch_one(
        conn,
        """
        SELECT
            COUNT(*) FILTER (WHERE claude_vision_ran_at IS NOT NULL) AS rows,
            COUNT(*) FILTER (WHERE claude_vision_split_x IS NOT NULL) AS split_predictions,
            COUNT(DISTINCT claude_vision_model) FILTER (
                WHERE claude_vision_model IS NOT NULL
            ) AS models
        FROM image_split_jobs
        """,
    )
    return {
        "local": local,
        "remote": remote,
        "legacy_claude_columns": {
            "rows": int(legacy_claude.get("rows") or 0),
            "split_predictions": int(legacy_claude.get("split_predictions") or 0),
            "distinct_models": int(legacy_claude.get("models") or 0),
        },
    }


def fetch_consensus_corpus(conn) -> dict[str, Any]:
    if not table_exists(conn, "splitter_consensus_corpus"):
        return {"exists": False, "total": 0, "by_source": {}}

    rows = fetch_all(
        conn,
        """
        SELECT COALESCE(NULLIF(verification_source, ''), 'unknown') AS source, COUNT(*) AS n
        FROM splitter_consensus_corpus
        GROUP BY 1
        ORDER BY n DESC, source ASC
        """,
    )
    return {
        "exists": True,
        "total": sum(int(row["n"] or 0) for row in rows),
        "by_source": {str(row["source"]): int(row["n"]) for row in rows},
    }


def build_training_warnings(
    *,
    labels: dict[str, Any],
    queue: dict[str, Any],
    prediction_runs: dict[str, Any],
    consensus: dict[str, Any],
    args: argparse.Namespace,
) -> list[str]:
    warnings: list[str] = []
    totals = labels["totals"]
    labelled = int(totals.get("labelled_jobs") or 0)
    positives = int(totals.get("positive_labels") or 0)
    negatives = int(totals.get("negative_labels") or 0)
    ground_truth = int(totals.get("ground_truth_labels") or 0)
    analyst_correct = int(totals.get("analyst_correct_labels") or 0)
    backlog = int(queue["summary"].get("completed_unreviewed") or 0)
    failed_unreviewed = int(queue["summary"].get("failed_unreviewed") or 0)

    if labelled < args.min_total_labels:
        warnings.append(
            f"Label volume is low: {labelled} labelled jobs "
            f"(< {args.min_total_labels})."
        )
    if positives < args.min_positive_labels:
        warnings.append(
            f"Positive split labels are low: {positives} "
            f"(< {args.min_positive_labels})."
        )
    if negatives < args.min_negative_labels:
        warnings.append(
            f"Negative/no-split labels are low: {negatives} "
            f"(< {args.min_negative_labels}); single-container/bad-image examples "
            "are needed before training a robust classifier."
        )
    if ground_truth == 0 and analyst_correct:
        warnings.append(
            "All usable positive labels appear to come from analyst-approved "
            "candidates, so evaluation may be biased toward the current strategy."
        )
    elif ground_truth < max(25, labelled // 10):
        warnings.append(
            f"Independent ground-truth labels are thin: {ground_truth}; add manual "
            "click labels for a less biased validation set."
        )
    if backlog > args.max_review_backlog:
        warnings.append(
            f"Review backlog is high: {backlog} completed unreviewed jobs "
            f"(> {args.max_review_backlog})."
        )
    if failed_unreviewed:
        warnings.append(
            f"{failed_unreviewed} failed unreviewed splitter job(s) need triage."
        )

    for scanner in labels["by_scanner"]:
        completed = int(scanner.get("completed_jobs") or 0)
        labelled_for_scanner = int(scanner.get("labelled_jobs") or 0)
        if completed and labelled_for_scanner < args.min_labels_per_scanner:
            warnings.append(
                f"{scanner['scanner']} has only {labelled_for_scanner} labelled "
                f"job(s) (< {args.min_labels_per_scanner}) across {completed} "
                "completed job(s)."
            )
        if completed and int(scanner.get("negative_labels") or 0) == 0:
            warnings.append(
                f"{scanner['scanner']} has no negative/no-split labels yet."
            )

    if int(prediction_runs["local"].get("total") or 0) == 0:
        warnings.append(
            "No local model prediction runs exist yet; the model is not in shadow mode."
        )
    remote_total = int(prediction_runs["remote"].get("total") or 0)
    legacy_remote = int(prediction_runs["legacy_claude_columns"].get("rows") or 0)
    if remote_total == 0 and legacy_remote == 0:
        warnings.append(
            "No remote teacher/vision prediction runs are recorded for comparison."
        )
    if consensus.get("exists") and int(consensus.get("total") or 0) < 100:
        warnings.append(
            f"Consensus corpus is small: {consensus['total']} row(s); grow it before "
            "using it as a major teacher signal."
        )

    return warnings


def build_report(args: argparse.Namespace) -> dict[str, Any]:
    conn = connect_database(args.database_url)
    try:
        conn.set_session(readonly=True, autocommit=True)
        queue = fetch_queue_counts(conn, args.recent_hours)
        labels = fetch_label_coverage(conn)
        strategy_distribution = fetch_strategy_distribution(conn, args.strategy_limit)
        prediction_runs = fetch_prediction_runs(conn)
        consensus = fetch_consensus_corpus(conn)
    finally:
        conn.close()

    report = {
        "generated_at": utc_now_iso(),
        "read_only": True,
        "queue": queue,
        "labels": labels,
        "prediction_runs": prediction_runs,
        "current_best_strategy_distribution": strategy_distribution,
        "consensus_corpus": consensus,
        "training_readiness_warnings": [],
    }
    report["training_readiness_warnings"] = build_training_warnings(
        labels=labels,
        queue=queue,
        prediction_runs=prediction_runs,
        consensus=consensus,
        args=args,
    )
    return report


def fmt_rate(value: Any) -> str:
    if value is None:
        return "-"
    return f"{float(value) * 100:.1f}%"


def print_mapping(mapping: dict[str, int], *, indent: str = "    ") -> None:
    if not mapping:
        print(f"{indent}-")
        return
    for key, value in mapping.items():
        print(f"{indent}{key}: {value}")


def print_report(report: dict[str, Any]) -> None:
    print("NSCIM image splitter operational report")
    print(f"  Generated: {report['generated_at']}")
    print(f"  Read only: {report['read_only']}")

    queue = report["queue"]
    print("\nQueue counts")
    summary = queue["summary"]
    print(f"    total_jobs: {summary['total_jobs']}")
    print(f"    pending: {summary['pending']}")
    print(f"    processing: {summary['processing']}")
    print(f"    completed: {summary['completed']}")
    print(f"    failed: {summary['failed']}")
    review_ready = queue["review_ready"]
    print(f"    completed_unreviewed: {summary['completed_unreviewed']}")
    print(f"    failed_unreviewed: {summary['failed_unreviewed']}")
    print(
        "    review_ready_with_crops: "
        f"{review_ready['recent']} recent/{review_ready['all_time']} all-time "
        f"({review_ready['recent_hours']}h window)"
    )
    print(f"    candidate_results_with_crops: {review_ready['candidate_results_with_crops']}")

    labels = report["labels"]
    totals = labels["totals"]
    print("\nLabel coverage")
    print(
        "    total labelled: "
        f"{totals.get('labelled_jobs', 0)} / {totals.get('completed_jobs', 0)} "
        f"completed ({fmt_rate(totals.get('coverage_rate'))})"
    )
    print(f"    positive split labels: {totals.get('positive_labels', 0)}")
    print(f"    negative/no-split labels: {totals.get('negative_labels', 0)}")
    print(f"    ground truth labels: {totals.get('ground_truth_labels', 0)}")
    print(f"    analyst correct labels: {totals.get('analyst_correct_labels', 0)}")
    print("    by scanner:")
    for scanner in labels["by_scanner"]:
        print(
            "      "
            f"{scanner['scanner']}: labelled {scanner['labelled_jobs']}/"
            f"{scanner['completed_jobs']} ({fmt_rate(scanner['coverage_rate'])}), "
            f"positive {scanner['positive_labels']}, negative {scanner['negative_labels']}, "
            f"uncertain {scanner['uncertain_verdicts']}"
        )
    print("    by label source:")
    print_mapping(labels["by_label_source"], indent="      ")
    print("    by verdict:")
    print_mapping(labels["by_verdict"], indent="      ")

    print("\nPrediction runs")
    runs = report["prediction_runs"]
    for name in ("local", "remote"):
        block = runs[name]
        status = "present" if block["exists"] else "missing table"
        print(f"    {name}: {block['total']} ({status})")
        for group_name, group_values in block.get("groups", {}).items():
            print(f"      by {group_name}:")
            print_mapping(group_values, indent="        ")
    legacy = runs["legacy_claude_columns"]
    print(
        "    legacy_claude_columns: "
        f"{legacy['rows']} row(s), {legacy['split_predictions']} split prediction(s), "
        f"{legacy['distinct_models']} model(s)"
    )

    print("\nCurrent best strategy distribution")
    for row in report["current_best_strategy_distribution"]:
        avg_score = "-" if row["avg_score"] is None else f"{row['avg_score']:.4f}"
        print(
            f"    {row['strategy']}: {row['jobs']} job(s), "
            f"{row['reviewed_jobs']} reviewed, avg score {avg_score}"
        )

    consensus = report["consensus_corpus"]
    print("\nConsensus corpus")
    if consensus["exists"]:
        print(f"    total: {consensus['total']}")
        print_mapping(consensus["by_source"])
    else:
        print("    table missing")

    print("\nTraining readiness warnings")
    warnings = report["training_readiness_warnings"]
    if not warnings:
        print("    None")
    for warning in warnings:
        print(f"    - {warning}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.recent_hours < 1:
        parser.error("--recent-hours must be >= 1")
    if args.strategy_limit < 1:
        parser.error("--strategy-limit must be >= 1")

    report = build_report(args)
    if args.json:
        print(json.dumps(report, indent=2, sort_keys=True, default=json_default))
    else:
        print_report(report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
