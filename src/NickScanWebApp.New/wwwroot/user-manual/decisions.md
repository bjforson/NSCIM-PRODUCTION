---
title: "Normal / Abnormal decisions"
category: "Workflow"
order: 10
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Making a decision

Every container that passes through NSCIS gets one primary verdict: **Normal** or **Abnormal**. This is the unit that ICUMS ultimately receives from us.

## What Normal means

The container's scan is **consistent with the declared manifest**. Nothing visible in the image contradicts the BoE (Bill of Entry) declaration:

- Shapes match the expected cargo type.
- Density / material signatures are plausible for what was declared.
- No concealment indicators (voids, unexpected dense objects, shielding patterns).
- No structural anomalies (modified container, hidden compartments).

## What Abnormal means

The scan shows **something that doesn't fit** what was declared. That could be:

| Type | Signal |
|---|---|
| **Density mismatch** | Declared textiles but large metal objects visible. |
| **Concealment** | Voids, objects hidden behind shielding, abrupt density changes. |
| **Undeclared goods** | Items visible in the scan but not on the BoE. |
| **Structural anomaly** | Modified walls, false floors, inconsistent container geometry. |
| **Ambiguous** | Scan is unclear; needs physical inspection to rule out. |

**Abnormal does NOT mean "illegal" or "intercepted."** It means "this needs human attention before clearance." Customs officers and enforcement investigate from there.

## How to record a decision

In the **viewer dialog**:

1. Complete your image review (modes, pixel probe, ROI Inspector as needed).
2. Use the **Normal** / **Abnormal** buttons at the bottom of the side panel.
3. If Abnormal, add a **note** explaining what you saw. This gets attached to the submission and is visible to the auditor.
4. If you marked rectangles over areas of concern, they persist alongside the decision.

Keyboard shortcut: `A` for approve/Normal, `N` for abnormal.

## Decisions propagate to the Bill of Lading

A Bill of Lading usually has multiple containers. The BL's overall verdict follows this rule:

```
BL verdict = MAX(container verdicts)
```

If **any** container in the BL is Abnormal, the whole BL is Abnormal for downstream ICUMS purposes. See [BL Review](/help/bl-review) for the user-facing implications.

## When you're genuinely uncertain

Don't guess. Options:

1. **Mark Abnormal with "needs-physical-inspection" note.** Errs on the side of caution — customs will physically open it.
2. **Escalate to senior analyst / manager.** Use the Send-to-Manager button (if your site has it configured). The container leaves your queue for review by a more senior operator.

Marking Normal on an ambiguous scan is the wrong answer — it removes the container from inspection scrutiny and may be traced back to you in post-release audit.

<!-- requires: Pages.ImageAnalysisAudit,Pages.ImageAnalysisManagement -->
## How audit changes the picture

The analyst's decision is **provisional** until an auditor has reviewed it. The auditor can:

- **Approve** — analyst decision stands; it becomes final.
- **Escalate** — auditor overrides a Normal to Abnormal.
- **Send back** — auditor feels the call isn't right, bounces it back for re-analysis.

Auditors never silently change a Normal to Abnormal — escalations are always logged with the auditor's ID, timestamp and reason. See [Audit Review](/help/audit-workflow) for the auditor-side view.
<!-- /requires -->

## Revisiting past decisions

Once a container's decision is **Approved + Audited**, it's locked from edits. If something turned up later (customs inspector found something physically that you missed), the container has to be raised as a **post-release amendment** — that's an ICUMS workflow outside NSCIS.

## Diagnostic tags

Decisions also carry optional **diagnostic tags** from the dropdown next to the note field:

- `concealment`
- `density-mismatch`
- `structural-anomaly`
- `undeclared-goods`
- `re-scan-required`
- `manifest-unclear`

These feed into the reports dashboard so managers can track which tag types come up most often — useful for training and scanner-calibration feedback.

---

## What to read next

- [BL Review](/help/bl-review) — how BL aggregates container decisions
- [Audit Review workflow](/help/audit-workflow) — the second-tier review
- [ROI Inspector](/help/roi-inspector) — the tool that most often clinches an ambiguous call
