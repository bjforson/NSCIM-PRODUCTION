---
title: "Cross-record scans — when one scan spans multiple records"
category: "Workflow"
order: 60
requires: [Pages.CrossRecordScans, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Cross-record scans — background

A **cross-record scan** is any scan that relates to more than one container record in our system. [Split Review](/help/split-review) is the UI for approving the most common case; this page explains the broader category.

## Why cross-records exist

Our **container-record** model assumes one scan = one container. The real world doesn't always fit:

| Situation | Why it's cross-record |
|---|---|
| Two 20ft containers on a 40ft chassis | One scan, two container numbers, two BoEs |
| Re-scan of a previously-scanned container | Two scans → one container-record (historical scan is superseded) |
| Scanner malfunction mid-pass | One physical container in multiple partial scans |
| Shared consignment with multiple ICUMS declarations | One container, multiple declarations (trans-shipment edge case) |

The system handles these via a **linkage table** (`ScanContainerLinks`) that records which scans belong to which container records. Cross-record scans are the ones where that linkage isn't 1:1.

## The two main surfaces

### Split Review — 2 containers per scan

Most common. One scan image → two independent container records. The [Split Review](/help/split-review) queue handles this.

### Completeness Records → Cross-Record tab

Groups of **related records** the system has auto-flagged because they share a scan or share a BoE. Ops uses this to:

- Resolve "which scan is the real one" for duplicate / re-scanned containers.
- Audit where re-scans happened and why.
- Manually link scans to BoEs when auto-linkage didn't fire.

URL: `/operations/cross-record-scans` (also reachable via Completeness → Cross-Record tab).

## How automatic linkage works

On ingest, the **Completeness Engine** tries to link each scan to exactly one container record based on:

1. **Plate OCR + vehicle registry match** — reads the container number from the scanner's plate crop, looks it up in the vehicle registry.
2. **Declaration match** — looks for ICUMS BoEs awaiting a scan with that container number.
3. **Temporal window** — only considers BoEs submitted within the last 72 h.

If the engine finds exactly one match, the scan is linked and becomes part of that record's completeness bundle. If it finds zero, one, or multiple matches, the scan is parked for ops review:

| Match count | Outcome |
|---|---|
| **1** | Auto-linked. Scan flows to Workbench as normal. |
| **0** | Parked under "Unmatched Scans" — ops may need to intervene when BoE arrives late. |
| **2+** | Parked under "Cross-Record Scans" — ops picks the correct BoE. |

## Resolving a cross-record scan manually

From Cross-Record Scans tab:

1. Open the flagged scan.
2. Look at the candidate BoEs the engine considered (shown in a side panel with their declaration timestamps + consignee).
3. Pick the correct match (or none if you need to escalate).
4. Confirm → linkage written. The scan joins that record's flow.

Incorrectly linking here will mess up a customs submission, so it's a senior-analyst / ops task. The `pages.crossrecordscans.view` permission gates the page.

## When to re-split vs when to re-link

- **Re-split** (use [Split Review](/help/split-review)) when the scan physically contains two containers and the auto-split was wrong.
- **Re-link** (use Cross-Record Scans) when the scan is one container but attached to the wrong BoE.

These are different fixes for different problems — don't conflate them.

## Audit trail

Every linkage and split decision is logged with:

- Operator ID
- Timestamp
- Before/after state
- Free-text reason (if the operator provided one)

Accessible via **Admin → Audit Logs** (permission-gated). Used in post-clearance customs audits to explain why a specific scan was attached to a specific BoE.

---

## What to read next

- [Split Review](/help/split-review) — the main cross-record workflow
- [Completeness](/help/completeness) — how the engine decides scans are ready for review
- [Admin Audit Logs](/help/admin-audit) — how to find historical linkage decisions
