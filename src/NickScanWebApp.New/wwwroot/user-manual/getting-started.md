---
title: "Getting Started"
category: "Overview"
order: 10
updated: 2026-04-21
version: v2.15.0
---

# Welcome to NSCIS

NSCIS — the **NickScan Central Imaging System** — is where scanned cargo containers get inspected, analysed, and signed off for customs release. This manual is scoped to your role; topics you don't have access to are hidden automatically.

## Who does what

| Role | Typical day |
|---|---|
| **Analyst** | Reviews cargo images, flags anomalies, submits decisions (Normal / Abnormal) |
| **Auditor** | Reviews analyst decisions, signs off or sends back for re-review |
| **Manager** | Oversees the queue, assigns work, monitors operator readiness |
| **Admin** | Manages users, roles, system settings, scanner services |

If your day-to-day doesn't match any of these, your role may be a custom hybrid — look at the **Help & Guides** sidebar to see what's available to you.

## Where to go first

- Your **Dashboard** (top-left NSCIS logo → Home) summarises the live queue.
- **Workbench** is where analysts pick up assigned work.
- **X-Ray Inspector** is a separate analysis surface for raw 16-bit scan inspection.
- **Containers** is the global index — search for any container by number.

<!-- requires: Pages.ImageAnalysisView,Pages.ImageAnalysisAudit,Pages.ImageAnalysisManagement -->
## If you work with scan images

The key surface is the **Image Analysis Viewer** — a full-screen dialog that opens when you click any scanner image from the Scanners page, from Workbench, or from Container Details.

It has five capabilities layered on top of the basic image:

1. [Render Mode toolbar](/help/mode-toolbar) — 9 vendor-standard image modes (B/W, Inverse, High Pen, Low Pen, Edge, Diff, Composite, Organic-Strip, Metal-Strip)
2. [Level / Window sliders](/help/window-level) — adjust image contrast on the fly
3. [Pixel Probe](/help/pixel-probe) — hover for raw pixel values + material category
4. [Raw 16-bit viewer](/help/raw-16bit) — zero-latency contrast control on the full dynamic range
5. [ROI Inspector panel](/help/roi-inspector) — draw a rectangle, get per-channel stats + material distribution

Start with [Viewer Basics](/help/viewer-basics) if you haven't used the viewer before.
<!-- /requires -->

## Browse by role

If you prefer a task-oriented starting point:

- **New to the system?** → [Analyst — your first hour](/help/analyst-first-hour) — step-by-step walkthrough of your first session.
- **Auditor starting a shift?** → [Auditor — the shift workflow](/help/auditor-workflow) — the loop you'll run hundreds of times.
- **Taking over as Admin?** → [Admin onboarding checklist](/help/admin-onboarding) — day-by-day for your first week.

## Topics by area

- **Workflow** — [Decisions](/help/decisions), [Workbench](/help/workbench), [Audit Review](/help/audit-workflow), [BL Review](/help/bl-review), [Completeness](/help/completeness), [Container Details](/help/container-details), [Normalization flow](/help/normalization-flow), [Split Review](/help/split-review), [Cross-Record Scans](/help/cross-record-scans).
- **Viewer** — [Viewer Basics](/help/viewer-basics), [Mode Toolbar](/help/mode-toolbar), [Window & Level](/help/window-level), [Pixel Probe](/help/pixel-probe), [Raw 16-bit](/help/raw-16bit), [ROI Inspector](/help/roi-inspector), [Variant Labels](/help/variant-labels).
- **ICUMS** — [Overview](/help/icums-overview), [Download Queue](/help/icums-download-queue), [Submission Queue](/help/icums-submission-queue), [BoE Request](/help/icums-boe-request).
- **Administration** — [Users](/help/admin-users), [Roles](/help/admin-roles), [System Settings](/help/admin-settings), [System Logs](/help/admin-logs), [Audit Logs](/help/admin-audit).
- **Monitoring** — [Services](/help/services-monitoring), [Scanners](/help/scanners-overview), [Performance](/help/performance).

## Need help not in this manual?

Ask your team lead or admin. Admins can preview this manual as any role via the **Preview Role** toggle at the top-right of this page, which helps them train new operators and answer "what will I see" questions.

---

> **Tip:** each topic here includes a **Version** chip like `v2.10.3` showing when that capability shipped. If you're on an older build, some features might not appear yet — bump the version (ops) or wait for the next deploy.
