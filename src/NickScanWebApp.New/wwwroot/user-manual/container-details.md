---
title: "Container Details — everything we know about one container"
category: "Workflow"
order: 25
requires: [Pages.ContainersDetails, Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Container Details page

The Container Details page is the single place where **all the data we have about one container** is visible in one view. Open it from:

- **Containers** (sidebar → Cargo → Containers) → click any row.
- **Workbench / BL Review / Audit Review** → click a container card.
- Global **Search** (top bar) → type a container number.
- Direct URL: `/containers/{containerNumber}`.

## Page layout

Header:
- Container number + status chip + current-stage chip.
- Vehicle plate, BL number, consignee.
- Age since first scanned.

Below header, a **MudTabs** strip with these tabs:

| Tab | What it shows |
|---|---|
| **Overview** | Summary dashboard — counts of scans / BoEs / flags. |
| **Images** | All scans attached to this container; click any for the full viewer. |
| **Declaration** | The ICUMS BoE data — consignee, goods description, HS codes, values. |
| **Decisions** | Full decision history: analyst, auditor, timestamps, notes, tags. |
| **Completeness** | The three-signal matrix (Scanner / ICUMS / Images) with timestamps. |
| **ICUMS** | Submission status + raw payload view (ops/admin only). |
| **Audit Trail** | Every action taken on this container (claims, decisions, edits). |

Not every tab shows for every role — permission-gated.

## Overview tab

Six KPI cards across the top:

- **Scan Count** — how many scans this container has (usually 1; >1 means re-scan).
- **Decisions Made** — total decisions (includes history if sent-back + re-decided).
- **Current Status** — Ready / InReview / DecisionMade / Audited / Submitted / ICUMSSynced / Failed.
- **Completeness** — which of the three signals are present (green check per signal).
- **Time in Stage** — how long it's been at the current stage.
- **Priority** — High / Medium / Low, inherited from the BoE.

Below KPIs: a timeline visualisation showing the full lifecycle from ingest to submission.

## Images tab

Thumbnails grid of every scan. Click any → viewer dialog opens (same as Workbench). You see:

- Vendor main JPEG (always).
- Render-mode-rendered variants (when raw channels exist).
- Any split-pair partner scans if this container came from [Split Review](/help/split-review).

Metadata row below each thumbnail: scanner ID, scan timestamp, variant, image resolution.

## Declaration tab

A table view of the ICUMS BoE attached to this container:

- Consignee + consignor
- HS code(s) + goods description
- Declared weight / declared value
- Declaration number + date
- Customs office
- Any flags (prohibited goods watchlist, random inspection flag, etc.)

If no BoE is attached yet, this tab shows "Declaration not yet received" with the age of waiting.

## Decisions tab

Full chronological log of every decision ever made:

```
[2026-04-20 14:32]  Analyst john.doe: Normal           — "Cargo consistent with declaration"
[2026-04-20 14:48]  Auditor ann.smith: Sent back      — "Please re-check left-side void"
[2026-04-20 15:05]  Analyst john.doe: Abnormal        — "Confirmed — void behind textile pallets"
[2026-04-20 15:12]  Auditor ann.smith: Approved       — "Agreed, escalating to physical inspection"
```

Each row is timestamped, signed, and includes any tag + free-text note.

## Completeness tab

Detailed view of the three completeness signals:

| Signal | Timestamp | Source | Status |
|---|---|---|---|
| Scanner | 2026-04-20 14:15:03 | FS6000-02 (Gate 3) | ✅ |
| ICUMS Declaration | 2026-04-20 14:28:47 | BoE 20260420-0142 | ✅ |
| Images | 2026-04-20 14:15:03 | Vendor JPEG + HE/LE/MAT | ✅ |

With any exception notes (e.g., "Partial channels — MAT missing" or "Late BoE — declaration arrived 6 hours after scan").

<!-- requires: Pages.ImageAnalysisManagement,Pages.AdminAudit -->
## ICUMS tab

The live submission status:
- Current payload state (Draft / Queued / Submitting / Acknowledged / Failed).
- Last-attempt timestamp + error (if any).
- Raw payload JSON + attachments list (expandable).
- Retry button (managers + admins only).

Useful for troubleshooting: if a container shows `Failed`, this tab tells you *why* (network error, ICUMS validation error, timeout) and lets you retry without opening the submission queue.
<!-- /requires -->

## Audit Trail tab

Every action on the container with:

- Action type (claimed / decided / edited / re-scanned / submitted)
- Actor (username)
- Timestamp
- Before/after state (for edits)
- Free-text reason (if provided)

Used for post-release audits by compliance / customs officers. Export available as CSV (if your role has the permission).

## Tips

- **Bookmark the URL.** `/containers/ABCD1234567` is stable — you can share a link to a specific container to a colleague for review.
- **Use Decisions tab to understand why something was sent back.** Saves messaging the analyst.
- **Completeness tab is the first thing to check** when a container "should be ready but isn't."

---

## What to read next

- [Completeness](/help/completeness) — the three-signal model explained
- [Decisions](/help/decisions) — making and reading the decision record
- [Viewer Basics](/help/viewer-basics) — the image viewer, reached from the Images tab
