---
title: "Analyst — your first hour"
category: "Role Guides"
order: 10
requires: [Pages.ImageAnalysisView]
updated: 2026-04-21
version: v2.15.0
---

# Your first hour as an analyst

A step-by-step walkthrough of your first session. If you've used a DICOM / radiology viewer before, most of this will feel familiar.

## Before you start

- Confirm with your manager: are you on **Auto**, **UserClaim**, or **Manual** assignment mode? This changes how work gets to you.
- Your manager will give you some warm-up containers to look at — usually the last 24 h of already-decided cases so you can see how a senior analyst reasoned about them.

## Step 1 — Open Workbench (2 min)

- Sidebar → **Image Analysis → Workbench**.
- Your queue probably has 0 items on day one. That's fine — your manager will push a practice group.
- While you wait, glance at the three tabs at the top: **Work Queue**, **Live Activity**, **My Stats**. You'll only use Work Queue today.

## Step 2 — Understand the queue card (3 min)

When your first group appears, it's usually a Bill of Lading (BL) with 1–8 containers:

- **BL header row** — BL number, consignee, container count, age.
- Each container in the group has its own sub-row with a small thumbnail.
- Click the container sub-row to open the viewer.

See [Workbench](/help/workbench) for the full layout.

## Step 3 — Explore the viewer (20 min)

Click your first container. The viewer opens full-screen.

What to try, in order:

### (a) Orient yourself
- The main image fills the screen.
- Top toolbar has zoom, rotate, filters.
- Below toolbar: the **RENDER MODE** row. Notice the variant chip at the right — e.g. `fs6000-v1`.
- Right side: the side panel with container info, ROI inspector, decision buttons.

### (b) Try every render mode
Click each mode chip left-to-right: **Composite, B/W, Inverse, High Pen, Low Pen, Organic, Metal, Edge, Diff**.

See [Render Mode toolbar](/help/mode-toolbar). Notice what each one emphasises. Composite is your "home base" — you'll start there for every container.

### (c) Drag the Window + Level sliders
On a mode that supports it (B/W is a good choice). Watch how the image tones shift. See [Window & Level](/help/window-level). Hit the reset button (↻) to go back to defaults.

### (d) Turn on Pixel Probe
Click the eyedropper in the top toolbar. Hover the cargo — see the HE/LE/Material values. See [Pixel Probe](/help/pixel-probe).

### (e) Draw a rectangle
Press `D` (or click **Mark Area**). Drag a rectangle over something interesting. Release.

The **ROI Inspector** panel on the right populates — material distribution, histograms, channel stats. See [ROI Inspector](/help/roi-inspector). This is one of the most powerful tools — spend 5 minutes understanding the histograms.

### (f) Turn on Raw 16-bit
Click the **Raw 16-bit** chip at the far right of the RENDER MODE row. Now drag the WINDOW slider all the way down — you'll pull detail out of the dark regions. See [Raw 16-bit viewer](/help/raw-16bit).

## Step 4 — Your first decision (10 min)

Now make a call:

1. Look at the container as a whole. Does anything jump out?
2. Check the **Declaration** panel (side panel or Container Details). What's it *supposed* to be?
3. Compare what you see to what's declared. Any mismatch?
4. If something looks odd, mark it with a rectangle and read the ROI stats.

When you're ready:
- **Normal** — if the scan is consistent with the declaration.
- **Abnormal** — if anything looks off. Add a note explaining what.

See [Decisions](/help/decisions) for more on the call.

Keyboard shortcut: `A` = Normal, `N` = Abnormal.

## Step 5 — Rinse and repeat (rest of hour)

Move through the rest of the containers in the group:

- `→` (right arrow) jumps to the next container in the group.
- Decide each one.
- When you finish the last container in the group, it leaves your queue automatically.

## Don't try to be fast on day one

Experienced analysts decide in 30–90 seconds per container. You might take 5–10 minutes per container today. **That's fine.** Speed comes from pattern familiarity (what textile cargo looks like vs metal, what a concealment void looks like). Spend the time looking.

## If something stumps you

- Mark the container **Send Back for Re-Review** with a note explaining what confused you.
- Message your manager — they'll come help, and if the case is ambiguous it may warrant escalation anyway.
- **Never guess Normal on an ambiguous scan.** When in doubt, mark Abnormal — physical inspection will resolve it.

## End-of-hour checklist

- [ ] You've opened the viewer and tried every mode chip.
- [ ] You've drawn at least one rectangle and read the ROI stats.
- [ ] You've made at least 3 decisions (Normal or Abnormal).
- [ ] You know the keyboard shortcuts `A`, `N`, `S`, `→`.
- [ ] You know where the Raw 16-bit toggle is.
- [ ] You know where to ask for help.

If you can tick all six, you're ready for real work. Welcome to the team.

---

## What to read next

- [Viewer Basics](/help/viewer-basics) — full viewer reference
- [Decisions](/help/decisions) — the decision itself, in depth
- [BL Review](/help/bl-review) — alternative workflow some sites use
