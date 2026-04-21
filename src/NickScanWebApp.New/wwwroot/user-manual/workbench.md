---
title: "Workbench — the analyst's daily surface"
category: "Workflow"
order: 15
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# The Workbench

Workbench is where **analysts pick up and work through their image-review queue**. It's the most-used surface in NSCIS — you live here for most of your shift.

Open it from the sidebar: **Image Analysis → Workbench** (URL: `/operations/image-analysis`).

## Top of the page — the queues

Three stat cards across the top:

- **My Assignments** — groups currently claimed by you. Work through these first.
- **Available Queue** — shared pool (UserClaim mode only). Groups you can pick up.
- **Completed Today** — how many you've signed off since midnight.

Next to them: the **Ready for New Assignments** toggle. Flip it off when you're heading out for lunch or on break — the auto-assigner stops routing new work to you until you flip it back on.

## Assignment modes

The system can distribute work in three ways, set globally by your admin:

| Mode chip | What it means for you |
|---|---|
| **Auto** | The system pushes groups to ready analysts in order. You mostly just work through My Assignments. |
| **UserClaim** | You pick groups from the Available Queue. Useful when workload is uneven or you want choice of what to review next. |
| **Manual** | A manager assigns groups to specific analysts. You just see what you've been given. |

Which mode is active is shown as a chip in the page header.

## Working a group

Most groups are a single **Bill of Lading** (one or more containers under one shipping contract). Click a group card to expand — you'll see each container as a mini card with a thumbnail, container number, consignee, and a status dot.

Click any container card → the viewer opens full-screen. Review, make your Normal/Abnormal call, save, and the container gets a green check. Move to the next.

When every container in a group has a decision, the group leaves your queue automatically and moves to Audit Review.

## Keyboard shortcuts

Most frequent operators never touch the mouse after opening the viewer:

- `A` — Approve / mark Normal
- `N` — Mark Abnormal (opens note field)
- `S` — Send back (if you can't decide — kicks it back to the queue)
- `→` / `←` — Next / previous container in group
- `Esc` — Close viewer and return to the queue
- `+` / `-` — Zoom
- `D` — Toggle draw mode (rectangles)
- `R` — Rotate 90°

See [Viewer Basics](/help/viewer-basics) for the full list.

## Row colour codes

In the group list:

- **Blue left-border** — your assigned group (active work).
- **Orange** — pending (waiting on another stage, e.g., re-scan requested).
- **Green** — decided, in audit.
- **Grey** — not yet in your queue (you don't have permission or it's unassigned).

## Filters

The filter bar at the top lets you narrow the list:

- **Priority** — High / Medium / Low (set at ingest by BoE flags).
- **BL status** — Ready / In Review / Decided.
- **Age** — Oldest first / Newest first.
- **Search** — BL number, container number, consignee name.

Filters persist across page reloads (stored in your browser's localStorage).

## Tab: Live Activity

Second tab in the top-level MudTabs strip. Shows real-time updates for your team via SignalR:

- Who's currently working on which group.
- Recently-completed decisions (yours and teammates').
- System-level events (ICUMS down, scanner offline).

Useful for:
- Avoiding double-work (if someone else is already in a group, you see that).
- Peer awareness during high-volume shifts.

## Tab: My Stats

Your throughput today: containers decided, average time per decision, Normal/Abnormal split. Good for a mid-shift self-check — if you're suddenly spending 5 min on every container when the baseline is 1 min, something's wrong (bad cargo, tired, bad scan quality).

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager view — Team Stats tab

If you're a manager, there's also a **Team Stats** tab showing:

- Per-analyst throughput + decision split.
- Longest-pending groups (stale queue alerts).
- Ready-for-assignment analyst count vs. queue depth (capacity gauge).

Use this for real-time load balancing — flip Auto/UserClaim toggles, reassign work manually, call in extra analysts.
<!-- /requires -->

## When something is wrong

- **Queue looks stuck**: hit **Refresh** in the header. If it's still frozen, the SignalR connection probably dropped — a full page reload fixes it.
- **Can't open a container**: check the container's status in the list. If it's "Sent back" or "Audited", it's not yours to review anymore.
- **Viewer shows only 3 modes**: the scan has limited raw channels — see [Variant Labels](/help/variant-labels). Review with what's available.

---

## What to read next

- [Decisions](/help/decisions) — what Normal/Abnormal actually mean
- [Viewer Basics](/help/viewer-basics) — the image surface itself
- [BL Review](/help/bl-review) — the alternative "group-first" surface for the same work
