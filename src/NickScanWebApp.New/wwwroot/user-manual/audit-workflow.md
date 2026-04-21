---
title: "Audit Review — second-tier sign-off"
category: "Workflow"
order: 20
requires: [Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Audit Review workflow

Audit Review is the **second pair of eyes** on every container decision. An analyst's Normal / Abnormal call doesn't become the container's final state until an auditor has signed it off.

Open it from the sidebar: **Image Analysis → Audit Review** (URL: `/operations/audit`).

## What you see

Two queues at the top of the page:

- **My Assignments** — groups currently claimed by you. Keep working through these first.
- **Available Queue** — unclaimed groups that are ready for audit. Only shown in **UserClaim** assignment mode.

Plus a **Ready for New Assignments** switch in the header. Turn it off when you're going on break or off-shift — the auto-assigner stops sending you new work.

## The three assignment modes

The header chip shows which mode is currently active (set by your admin globally):

| Mode | Behaviour |
|---|---|
| **Auto** | The system pushes groups to ready auditors automatically as decisions arrive. |
| **UserClaim** | Groups enter the shared pool. You pick up work from **Available Queue**. |
| **Manual** | A manager assigns groups to specific auditors via their screen. |

Most sites run Auto for throughput and UserClaim during peak loads when you want auditors choosing what to work on. Manual is a backup — useful for training or specialised containers.

## The decision you make

For each group (typically one Bill of Lading):

1. **Open the first container.** Review the analyst's Normal/Abnormal decision, their notes, any marked areas.
2. **Look at the images yourself.** All five viewer capabilities are available — modes, W/L, pixel probe, Raw 16-bit, ROI Inspector. Re-check the analyst's ROIs; draw your own if you see something they missed.
3. **Choose one of three actions** (per container):
   - **Approve** — the analyst's decision stands. Container progresses to submission.
   - **Escalate to Abnormal** — you disagree with a Normal decision; mark it Abnormal. Forces the whole BL to Abnormal (see [BL Review](/help/bl-review)).
   - **Send back** — analyst needs to redo the decision. Optionally include a note. The container returns to the Workbench queue for re-analysis.
4. **Sign off the group.** Once every container has an action, the group moves out of your queue.

## Keyboard shortcuts

Same as Workbench:

- `A` — Approve current container
- `N` — Mark Abnormal
- `S` — Send back for re-review
- `→` / `←` — Next / previous container within the group
- `Esc` — Close the viewer

## Bulk actions

Select multiple containers in a group (checkbox column) then use the bulk action bar at the top: **Bulk Approve**, **Bulk Send Back**. Not available for Escalate — escalations must be individually justified.

## What happens after you sign off

- **All Approved** → group moves to Completeness Records, gets packaged for ICUMS submission.
- **Any Escalated** → BL is flagged Abnormal; goes to the ICUMS Abnormal workflow (different submission path, may require BoE manual intervention).
- **Any Sent Back** → container re-enters Workbench with your note; an analyst picks it up fresh.

<!-- requires: Pages.ImageAnalysisManagement -->
## Audit metrics (Managers)

If you're a manager auditor, you also see a **Team Metrics** card in the header with throughput stats across all auditors, send-back rates, and escalation counts. Use this to spot auditors who need training (high send-back %) or overly lenient review (zero escalations on a high-volume day).
<!-- /requires -->

## Performance

- Loading the audit queue: 300–900 ms.
- Opening a group's first viewer: 1–3 seconds (scan decode + first mode render).
- Switching between containers in the same group: 200–500 ms (decode cache warm).

## If you're stuck

- **No groups in queue + Available Queue empty**: check with your manager — you might be ahead of the analyst tier.
- **"Send back" doesn't show**: the container is already sent-back or already approved; refresh.
- **Images won't load**: check the **variant label** in the viewer ([Variant Labels](/help/variant-labels)) — some scans are vendor-JPEG-only and still need to be reviewed even without the full mode catalogue.

---

## What to read next

- [Analyst & Audit decisions](/help/decisions) — the Normal / Abnormal decision itself
- [BL Review](/help/bl-review) — how BL-level verdicts propagate
- [Viewer Basics](/help/viewer-basics) — if you're new to the image surface
