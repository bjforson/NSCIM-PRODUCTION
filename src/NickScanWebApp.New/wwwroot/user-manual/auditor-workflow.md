---
title: "Auditor — the shift workflow"
category: "Role Guides"
order: 20
requires: [Pages.ImageAnalysisAudit]
updated: 2026-04-21
version: v2.15.0
---

# Auditor — a typical shift

A walkthrough of how an experienced auditor moves through their shift. Shorter than the analyst guide because audit is mostly about **disagreeing well** with someone else's work — the surface is similar.

## Before your shift

- Confirm **Ready for New Assignments** is on.
- Glance at the **Team Stats** tab (if you have it) — see if any analysts look like they had a rough shift overnight. Their work might need extra scrutiny.

## The loop

Every container you audit is one iteration of this:

```
1. Open group from queue
2. For each container:
   a. Read the analyst's decision + note
   b. Review the images yourself
   c. Approve / Escalate / Send-back
3. Sign off the group
```

## Step 1 — Open a group

- Sidebar → **Image Analysis → Audit Review**.
- Claim the oldest group (usually the top of your queue) — age matters more than content here. Older containers have had more time to sit; getting them through clears the pipeline.

## Step 2 — Review the decision BEFORE the images

Click the first container. On the side panel, read what the analyst said:

- Decision: Normal / Abnormal.
- Tags they used.
- Free-text note.
- Any rectangles they drew.

**Form an expectation** before looking at the image: "They said Abnormal because of a void in the rear. I'll look for that first."

Why: lets you confirm or counter their reasoning efficiently, without getting pulled into a tangent.

## Step 3 — Review the images

Your process mirrors the analyst's, but **fast** — you're validating, not discovering:

1. Composite mode, scan top to bottom.
2. Check the specific area the analyst called out.
3. Draw your own ROI rectangles if needed.
4. If anything is uncertain, switch to High-Pen / Raw 16-bit.

Experienced auditors spend **30–60 seconds** per Normal decision. Abnormals get more time — typically 2–5 minutes to verify the anomaly.

## Step 4 — Decide

Three choices:

### Approve
The analyst's call is right. One click. Container moves to submission queue.

Keyboard shortcut: `A`

### Escalate
They called Normal but you see something they missed. Click **Escalate**, mark your own rectangles, add a note explaining what you see.

This escalation goes into the BL's record — the whole BL will be marked Abnormal (see [BL Review](/help/bl-review)). Use sparingly; escalations are a sign the analyst wasn't careful and trigger training review.

Keyboard shortcut: `N`

### Send-back
Something's wrong with their analysis — wrong area marked, ambiguous call they should re-review. Click **Send Back**, leave a note explaining why, container goes back to Workbench.

Send-back isn't punishment — it's coaching. A short, specific note ("please check the right-side gap between pallets — can't tell from your mark if you meant to flag that") helps the analyst learn.

Keyboard shortcut: `S`

## Step 5 — Next container / close the group

`→` arrow moves to the next container in the group. When all containers are decided, the group auto-closes and leaves your queue.

## Bulk actions

For groups where every container is clearly Normal:

- Select all rows → **Bulk Approve**. Saves keystrokes.

**Don't bulk-approve without reading.** The temptation is real when you've got 50 containers from a known-reliable analyst and it all looks fine — but bulk-approve without inspection is exactly the failure mode customs audits catch. Slow down on at least a spot-check sample.

## Mid-shift checks

- Every ~2 hours, glance at the queue depth. If it's growing faster than you're working, flag your manager — more auditors needed.
- If you're escalating unusually often (>10% of decisions), message the analyst's manager — likely a training issue.
- If you're sending back often (>5%), same.

## End-of-shift

- Flip **Ready for New Assignments** off.
- Clear your current group if possible (don't leave half-decided groups for the next auditor).
- Log off.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager responsibilities on top of the above

- **Queue depth monitoring** throughout the shift.
- **Reassignment** of stuck groups (someone's out sick, their claimed groups need re-routing).
- **Escalation review** — look at every escalation with the analyst involved; build training patterns.
- **Metric sign-off** — end-of-shift sanity check that throughput numbers look reasonable.
<!-- /requires -->

---

## What to read next

- [Audit Review](/help/audit-workflow) — full feature reference
- [Decisions](/help/decisions) — the decision model in depth
- [BL Review](/help/bl-review) — how escalations propagate
