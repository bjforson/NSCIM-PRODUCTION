---
title: "Manual BoE Request — fetch a specific declaration"
category: "ICUMS"
order: 40
requires: [Pages.IcumsBoeRequest, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Manual BoE Request

Most of the time, BoE declarations arrive in NSCIS automatically through the ICUMS poll loop. Occasionally you need a **specific BoE** right now — this page is how you ask for one.

Open: **Cargo → ICUMS → BoE Request** (URL: `/icums/boe-request`).

## When to use it

- **Urgent consignment** — high-value or time-sensitive cargo, agent has submitted but the polling cycle hasn't picked it up yet.
- **Missing match** — a scan's container number matches no BoE in our system; you want to verify with a specific declaration number the agent gave you.
- **Ops investigation** — confirming a specific BoE exists in ICUMS (and in what state) without trawling the queues.

**Don't use it for** bulk imports — that's the Batch Download page (admin-only).

## The form

One simple input: **Declaration Number**. Type it in exactly as ICUMS generates it (usually something like `20260420-01234`). Hit **Request**.

What happens:

1. NSCIS makes an out-of-cycle API call to ICUMS for that specific declaration.
2. If found, the BoE is downloaded and entered into the Download Queue (status: Downloaded).
3. Completeness Engine immediately tries to link it against an unmatched scan.
4. You see a result panel showing what came back.

## Result panel

Three possible states:

**Found + linked**
```
✓ Declaration 20260420-01234 retrieved
  Container: ABCD1234567
  Consignee: ACME Importers Ltd
  Linked to existing scan from 2026-04-20 14:15
  → Container now complete and in Workbench queue
```

**Found but no matching scan**
```
✓ Declaration retrieved
  Container: XYZ9876543
  Consignee: Widgets Co
  No scan matching this container number exists yet
  → Declaration parked; will auto-link when scan arrives
```

**Not found / error**
```
✗ Declaration 20260420-99999 not found
  ICUMS response: HTTP 404
  Suggestions: check the declaration number; confirm the agent actually submitted it
```

## History

The page keeps a 30-day log of manual requests at the bottom. Useful for:

- Showing the agent that you already tried to pull the declaration and it wasn't there.
- Audit: "who requested this out-of-cycle and why?"

Each log row includes requestor, timestamp, declaration number, and outcome.

## Typical turnaround

- **Found in ICUMS**: 500–1500 ms for the request; another few seconds for completeness linkage.
- **Not found**: immediate (ICUMS returns 404 fast).

## When ICUMS is down

The request fails immediately with a connection error — you don't sit waiting for a timeout. Retry when the ICUMS Dashboard shows Online again.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager notes

- **Heavy use of this page** (>20 requests per day) is usually a sign the standard poll interval is too long or the auto-downloader has a gap. Check the Download Queue + service logs.
- **Manual requests are tracked per-user**. If one user is making dozens, it might indicate a training gap or an actual ops bottleneck — worth a conversation.
<!-- /requires -->

---

## What to read next

- [Download Queue](/help/icums-download-queue) — automatic inbound flow
- [Completeness](/help/completeness) — what happens after a BoE is downloaded
- [ICUMS Overview](/help/icums-overview) — the whole ICUMS picture
