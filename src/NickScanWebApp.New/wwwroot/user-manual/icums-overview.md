---
title: "ICUMS Integration — overview"
category: "ICUMS"
order: 10
requires: [Pages.IcumsView, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# ICUMS Integration

**ICUMS** — the Integrated Customs Management System — is the national customs platform NSCIS talks to on both sides of the pipeline:

- **Inbound**: we pull Bill of Entry (BoE) declarations from ICUMS so analysts can match scans to declarations.
- **Outbound**: once a container is audited, we submit the decision (Normal/Abnormal + notes + ROI metadata) back to ICUMS.

Open it: **Cargo → ICUMS** (URL: `/icums` or `/customs/icums`).

## The Dashboard page

Four stat cards across the top:

- **Connection Status** — Online / Offline + API response time. If this goes red, nothing moves in or out.
- **Downloads Today** — BoEs received today (our inbound count).
- **Submissions Today** — decisions sent today (our outbound count).
- **Queue Health** — composite % health score for the two queues.

Below that, two panels:

- **Download Queue** (left) — pending BoE downloads, recent arrivals, failures.
- **Submission Queue** (right) — pending submissions, recent sends, failures.

Plus a connection-history sparkline showing ICUMS availability over the last 24 h.

## When ICUMS is down

Downtime here = pipeline freeze. You'll see:

- Connection Status chip goes red.
- Downloads Today stops incrementing (nothing coming in).
- Submissions Today may or may not keep moving — the queue buffers and retries.
- Completeness Records builds up "Missing ICUMS" counts.

**What to do:**
1. Check the connection history — if ICUMS is known to be down (maintenance window), ride it out.
2. If it's unexpected, try the **Test Connection** button. It forces a probe request and reports back.
3. Check network / VPN connectivity to the ICUMS endpoint.
4. Escalate to ICUMS side if NSCIS is healthy but can't reach them.

Nothing is lost during downtime — queues hold everything and resume on reconnect.

## The five sub-pages

From the top-level ICUMS Dashboard, there are five related pages each gated by its own permission:

| Page | Permission | What it does |
|---|---|---|
| [Download Queue](/help/icums-download-queue) | `pages.icums.downloadqueue` | Inbound BoE queue, manual download trigger |
| [Submission Queue](/help/icums-submission-queue) | `pages.icums.submissionqueue` | Outbound decisions queue, retries |
| [BoE Request](/help/icums-boe-request) | `pages.icums.boerequest` | Manually pull a specific BoE by number |
| **Loose Cargo** | `pages.icums.loosecargo` | Non-containerised imports flow (break-bulk) |
| **Analytics** | `pages.icums.analytics` | Volumes, success rates, per-clearing-agent stats |

Admin-only pages: **Payloads** (raw JSON), **Verify Status** (reconciliation), **Batch Download** (bulk BoE import).

## Connection details

NSCIS talks to ICUMS via:
- **REST API** over HTTPS.
- An authentication token refreshed every 8 hours.
- Polling interval: 60 s for new BoEs, 30 s for submission retries.

If you're running a test environment, the base URL is configurable in **Admin → System Settings → ICUMS Configuration**. Don't change this in production without an ops plan.

## Flow direction reminder

```
           NSCIS               ICUMS
           ─────               ─────
  scan →  Ingest
  BoE ←                    ← Download queue
         Completeness
         Workbench
         Audit
         Submission
  decision →                   → Submission queue
                                 → ack
```

Both directions are strictly asynchronous — no synchronous request ever blocks an operator.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager notes

- **Daily reconciliation**: check Submissions-Today vs Audited-Yesterday-Count. If they diverge by more than 1–2 containers, something didn't submit — investigate via the submission queue.
- **"Failed" containers age** — anything sitting in Failed state for more than 30 min needs manual retry or escalation.
- **ICUMS-side clock skew**: if timestamps on inbound BoEs look wrong, the two systems' clocks may have drifted — contact ops.
<!-- /requires -->

---

## What to read next

- [Download Queue](/help/icums-download-queue) — inbound BoE flow
- [Submission Queue](/help/icums-submission-queue) — outbound decision flow
- [BoE Request](/help/icums-boe-request) — manually request a single BoE
