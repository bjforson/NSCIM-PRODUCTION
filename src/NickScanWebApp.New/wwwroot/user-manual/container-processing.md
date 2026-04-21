---
title: "Container Processing — live pipeline view"
category: "Workflow"
order: 37
requires: [Pages.ContainerProcessing, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Container Processing pipeline

The Container Processing page shows the **live pipeline state** for every container currently moving through NSCIS, from scan ingest through to ICUMS submission. Think of it as a plant control-room screen.

Open it from the sidebar: **Operations → Container Processing**.

## What it's for

Unlike Workbench (analyst-facing) or Completeness Records (ops-facing), this page is the **cross-stage view** — it tells you where in the pipeline every in-flight container currently sits. It's used by:

- **Managers** doing a mid-shift pipeline health check.
- **Ops** looking for bottlenecks.
- **Auditors** trying to find a container they know was in the system but can't locate in their own queue.

## Page layout

### Pipeline stages (columns)

Left to right:

1. **Ingesting** — scan file just arrived, being validated.
2. **Awaiting BoE** — scan present, waiting for customs declaration.
3. **Ready** — both present, waiting for analyst pickup.
4. **In Review** — analyst has opened it.
5. **Decision Made** — analyst done; awaiting audit.
6. **Auditing** — auditor has opened it.
7. **Audited** — ready for ICUMS submission.
8. **Submitted** — sent to ICUMS, awaiting acknowledgement.
9. **Completed** — ICUMS acknowledged; done.

Each column shows a container count + the five oldest container cards.

### Anomaly lanes (bottom)

Containers that fell off the main pipeline show up here:

- **Failed** — submission rejected by ICUMS (needs manual intervention).
- **Sent Back** — audit sent back to analyst (in the re-review loop).
- **Cross-Record** — needs [split](/help/split-review) or [re-link](/help/cross-record-scans).
- **Stuck Completeness** — waiting >48h for missing signal.

## Drill-down

Click any stage column header → filtered Containers list view for that stage. Click any container card → Container Details page for that specific container.

## Useful filter combinations

- **By age** — "show me anything in Ready stage more than 4 hours old" — catches analyst backlog.
- **By scanner** — "show me anything from FS6000-03" — spots per-scanner issues.
- **By consignee** — track a specific importer's shipments through the pipeline.

## Performance metrics

Top-right shows a mini dashboard:

- **Throughput** — containers-per-hour completed (rolling 1-hour window).
- **Avg Stage Time** — median time in each stage over last 24h.
- **Bottleneck** — which stage currently has the deepest queue.

Useful for sizing the team: if "In Review" always has 50 items and "Auditing" always has 5, you need more analysts. If it's inverted, more auditors.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager actions from this page

- **Reassign** — click a stale container → reassign to a different analyst/auditor.
- **Prioritise** — bump a container to the top of its stage's queue (e.g., urgent customs request).
- **Flag** — add a manager note visible to everyone handling the container downstream.

These actions need `pages.imageanalysis.management` permission.
<!-- /requires -->

## When to use it vs Workbench vs Completeness Records

- **Workbench**: "what's in my personal queue to work on right now?"
- **Completeness Records**: "which containers are blocked before they even become work?"
- **Container Processing**: "where is container #XYZ right now, and is the overall pipeline healthy?"

Use Container Processing for situational awareness; use the other two for actual work.

---

## What to read next

- [How a scan becomes a customs record](/help/normalization-flow) — the pipeline in words
- [Completeness](/help/completeness) — the pre-Workbench gate
- [Container Details](/help/container-details) — drill-down on one container
