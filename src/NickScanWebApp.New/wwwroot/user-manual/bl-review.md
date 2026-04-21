---
title: "BL Review — reviewing containers grouped by Bill of Lading"
category: "Workflow"
order: 40
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Bill of Lading (BL) review

A **Bill of Lading** is the shipping-line document that covers one or more containers travelling together under a single contract of carriage. In NSCIS, containers are grouped by BL so you can review them as a consignment rather than one-at-a-time.

Open it from the sidebar: **Image Analysis → BL Review** (URL: `/image-analysis`).

## Why group by BL

Two reasons:

1. **Customs submits at BL level.** ICUMS accepts and flags declarations per BL. If even one container in the BL looks Abnormal, the **whole BL is considered Abnormal** and gets physical inspection. Reviewing the BL as a unit lets you see the full consignment story before committing.
2. **Context across containers.** Anomalies make more sense when you see the shipment as a whole. A container that looks odd on its own might fit a perfectly normal pattern across the 6 others under the same BL.

## The three completeness signals

A BL only appears in this queue if all three of these are true for every container in it:

- **Scanner data** — raw scan + images are present.
- **ICUMS data** — BoE declaration has been pulled down.
- **Images** — vendor JPEG exists for at least a preview.

If one container is missing data, the BL drops out of the queue until ingest catches up. Check **Completeness Records** if a BL you expect isn't showing up.

## The auto-abnormal rule

```
BL.decision = Abnormal  if ANY container.decision == Abnormal
BL.decision = Normal    only when ALL container.decision == Normal
```

This is enforced by the backend — you can't manually override the BL decision. If you want to "approve the BL" while one container inside is Abnormal, that container has to be re-reviewed (send back) and its decision changed.

## Screen layout

Top: page header with **How it Works** button (opens a quick-guide expander). Below it three info cards summarising the rules.

Then the **BL Review list**:

- One row per BL, expandable.
- Row shows BL number, container count, decision pattern (e.g., "5 Normal, 1 Abnormal"), and age (when the oldest container in it became complete).
- Click to expand → see each container, its thumbnail, and its per-container decision status.

Click any container → the viewer opens, same surface as Workbench.

## Your workflow

1. Pick a BL from the top of the list (usually sorted by age — oldest first).
2. Review each container inside.
3. Make your Normal/Abnormal decision per container (see [Decisions](/help/decisions)).
4. When all containers in the BL have a decision, the BL becomes **ready for audit**.

No explicit "submit BL" button — progression is automatic once every container is decided.

## Filters

The list has filters at the top:

- **Status** — Ready / In Review / Decided / Under Audit / Complete
- **Priority** — High / Medium / Low (set by the ingest stage based on BoE flags)
- **Age** — bracket filter for oldest container age (0–1h / 1–4h / 4–12h / 12h+)
- **Search** — BL number, container number, consignee

## Tips

- **Decide in order.** Don't skip around — it's easy to forget a container if you're hopping. The list grays-out containers you've already decided, so you can see at a glance what's left.
- **Use the BL notes field.** Anything unusual at BL level (e.g., "6 of 8 containers contain identical dense pallets — consistent with declared metal stampings") goes in the BL note. Shows up for the auditor.
- **Don't mix scanners in your head.** Different containers in one BL might have been scanned on different machines (FS6000 at one gate, ASE at another). The variant label in the viewer tells you which — use the mode catalogue appropriate to that variant.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager view

You also see a **Per-Analyst BL Throughput** chart above the list. Useful for spotting bottlenecks — if one analyst has 20 pending BLs and another has 2, work needs rebalancing.
<!-- /requires -->

---

## What to read next

- [Split Review](/help/split-review) — when one scan contains two containers from different records
- [Decisions](/help/decisions) — the Normal/Abnormal choice itself
- [Completeness](/help/completeness) — why a BL might be missing from the queue
