---
title: "How a scan becomes a customs record"
category: "Workflow"
order: 30
updated: 2026-04-21
version: v2.15.0
---

# From scanner to ICUMS — the lifecycle

A single cargo container moves through 7 stages in NSCIS before its record lands in ICUMS. Knowing where a container is in that journey tells you what to do with it.

## The stages

```
1. Scanner         →  2. Ingest  →  3. Completeness  →  4. Workbench
   (raw .img)          (DB row)      (data merge)        (analyst)
                                                              │
                                                              ▼
7. ICUMS            ←  6. Submission  ←  5. Audit Review
   (customs DB)        Queue              (auditor)
```

### 1. Scanner
The physical X-ray scanner (FS6000, ASE, Heimann) scans the container. Vendor software writes out a bundle of files:
- A main JPEG (vendor-rendered preview)
- Raw channel `.img` files (high-energy, low-energy, material classification for FS6000/ASE)
- A metadata sidecar (container number, timestamp, scan ID)

These files land in a shared directory the NSCIS ingestion service watches.

### 2. Ingest
The **IngestionService** picks up new scans every 5 seconds, validates the file set (see [data integrity](/help/variant-labels#if-the-variant-says-vendor-jpeg-only)), and writes a row into the `Scans` table with status `Pending`. Raw files are archived to long-term storage.

### 3. Completeness
For a scan to be **actionable**, we need three things to line up:
- **Scanner data** (step 2 — have the images).
- **ICUMS BoE data** (Bill of Entry from the customs system — have the declaration).
- **Vehicle plate match** (the scanner read the plate, ICUMS has a declaration for it).

When all three are present, the container becomes **complete** and eligible for Workbench. The **Completeness Records** page is the ops surface for this stage.

### 4. Workbench
An analyst picks up the container (or the auto-assigner pushes it). They use the viewer to review images, make a Normal/Abnormal decision, and save. Container status moves to `Decision-Made`. See [Workbench](/help/workbench).

### 5. Audit Review
An auditor opens the container (second-tier review). They approve, escalate, or send back. On approve, status moves to `Audited`. See [Audit Review](/help/audit-workflow).

### 6. Submission Queue
Audited containers enter the **ICUMS Submission Queue**. A background service packages the decision + notes + ROIs into an ICUMS payload (envelope + attachments) and POSTs it to ICUMS. Status moves through `Submitting` → `Submitted`. See [ICUMS Submission Queue](/help/icums-submission-queue).

### 7. ICUMS
ICUMS acknowledges receipt. The container record in NSCIS moves to `ICUMSSynced` and the container is **done** — no more action from you or the queue. Records stay searchable via Containers / Completed Records.

## Status vocabulary

In the Containers page and elsewhere you'll see these status values:

| Status | Meaning | Typical duration |
|---|---|---|
| `Pending` | Scan ingested, waiting for ICUMS data | 0–2 hours (depends on customs batch window) |
| `AwaitingDeclaration` | Scanner matched, no BoE yet | 0–24 hours |
| `Ready` | Complete, awaiting analyst | <1 hour in peak, up to days on low-volume nights |
| `InReview` | Analyst has opened it | 1–10 min |
| `DecisionMade` | Analyst done, awaiting audit | <1 hour |
| `Audited` | Auditor approved; ready for submission | <5 min |
| `Submitted` | Sent to ICUMS, awaiting ack | <30 s |
| `ICUMSSynced` | ICUMS confirmed; done | — |
| `Failed` | Submission failed; needs manual intervention | (see [ICUMS Submission Queue](/help/icums-submission-queue)) |

## Why containers stall

Most stall at step 3 (Completeness). Check:

- Is ICUMS reachable? (see [ICUMS Dashboard](/help/icums-overview)).
- Was the container plate read correctly? Wrong plate → wrong BoE.
- Did the scanner emit all its files? A truncated raw file flags `vendor-jpeg-only` ([Variant Labels](/help/variant-labels)).

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager's view of the flow

**Completeness Records** → **Workbench Queue Length** → **Audit Queue Length** → **Submission Queue Length** — four numbers on your dashboard that tell you the health of the pipeline. A growing bubble anywhere means work is queueing up:
- Rising Completeness = ingest or ICUMS sync problem.
- Rising Workbench = not enough analysts, or a training issue.
- Rising Audit = not enough auditors.
- Rising Submission = ICUMS outbound problem (network / ICUMS maintenance).
<!-- /requires -->

---

## What to read next

- [Workbench](/help/workbench) — where analysts pick up work
- [Audit Review](/help/audit-workflow) — second-tier review
- [Completeness](/help/completeness) — what "complete" means and why containers stall
