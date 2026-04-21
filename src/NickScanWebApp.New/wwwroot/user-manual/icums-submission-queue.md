---
title: "ICUMS Submission Queue — outbound decisions"
category: "ICUMS"
order: 30
requires: [Pages.IcumsSubmissionQueue, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# ICUMS Submission Queue

When an audited decision leaves NSCIS, it travels via this queue. Every Normal/Abnormal verdict, every ROI marking, every analyst note — packaged and POSTed to ICUMS.

Open: **Cargo → ICUMS → Submission Queue** (URL: `/icums/submission-queue` or `/customs/icums/submission`).

## What a submission contains

Per container:

- Decision (Normal / Abnormal).
- Analyst + auditor IDs, timestamps.
- Tags (concealment / density-mismatch / etc.).
- Free-text notes.
- **Attached artefacts**: thumbnails of marked ROI regions, rendered main-mode image for the record.

Packaged as an XML envelope + a multipart POST. Typical size: 50 KB – 2 MB depending on attachments.

## Queue columns

| Column | Meaning |
|---|---|
| Container # | The one being submitted. |
| Decision | Normal / Abnormal chip. |
| Queued | When the audit completed and it entered this queue. |
| Submitted | When we successfully POSTed. |
| Acknowledged | When ICUMS confirmed receipt. |
| Status | Queued / Submitting / Acknowledged / Failed. |
| Retries | Attempt count. |

## Status lifecycle

```
Queued → Submitting → Acknowledged  ← happy path
                   ↘
                    → Failed
                       → (auto-retry up to 3×)
                       → Quarantined (after 3 fails)
```

## Failure triage

Click a Failed row to see the last error:

| Error | Root cause | Action |
|---|---|---|
| `HTTP 400: validation` | ICUMS rejected the payload | Check decision notes for special characters; open Payload Viewer (admin) |
| `HTTP 401: auth` | Token expired | Wait; auto-refreshes |
| `HTTP 409: duplicate` | We've already submitted for this container | Mark Resolved — no re-submit needed |
| `HTTP 500` | ICUMS internal | Retry, escalate if persistent |
| `Timeout` | Network slowness | Auto-retry usually fine |

## Manual retry / dismiss

Row actions (permission-gated):

- **Retry** — immediate re-POST.
- **Edit + Retry** — opens a dialog to correct the decision note / tags, then re-POST. Rare use; most corrections happen upstream.
- **Dismiss** — marks Resolved without retrying. Use for duplicate-submission errors or cases where ICUMS side already has the record.

## Bulk operations

Header:

- **Retry All Failed** — bulk re-POST everything in Failed state.
- **Clear Resolved** — hide resolved rows (keeps them searchable in history).

## Historical view

**Submitted** tab shows successful submissions — useful for:

- Confirming a specific container has indeed been submitted (audit trail).
- Exporting daily submission logs for reconciliation.
- Cross-checking ICUMS-side ack timestamps against our submit timestamps (look for clock drift).

## The payload you don't normally see

The **Payload Viewer** (admin-only) shows the raw XML that went out. You'd open it for:

- Diagnosing an HTTP 400 — is there a malformed field?
- Compliance review — "show me exactly what you sent for container X".
- Debugging a new ICUMS validation rule after an ICUMS upgrade.

## Performance

- Per-submission round-trip: 500–2000 ms (ICUMS validates + writes).
- Outbound rate limit: 5 req/s (ICUMS-imposed; we respect it with queue throttling).
- A healthy site processes 200–800 submissions per day.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager checks

- **Queue depth** — should drain to zero within an hour of audit throughput stopping. If it stays deep overnight, retries aren't catching up → ICUMS-side issue.
- **Failed count** — non-zero for more than 30 min usually indicates a systematic issue (ICUMS rule change, auth issue).
- **Acknowledgement delay** — normally <5 s; >30 s consistently suggests ICUMS slowness.
- **Re-submission rate** — if >2% of containers end up retried, something is flaky — time to ticket ICUMS.
<!-- /requires -->

---

## What to read next

- [ICUMS Overview](/help/icums-overview) — both queues in context
- [Download Queue](/help/icums-download-queue) — inbound counterpart
- [How a scan becomes a customs record](/help/normalization-flow) — full pipeline
