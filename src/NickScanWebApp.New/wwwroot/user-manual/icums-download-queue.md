---
title: "ICUMS Download Queue — inbound BoE tracking"
category: "ICUMS"
order: 20
requires: [Pages.IcumsDownloadQueue, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# ICUMS Download Queue

Every **Bill of Entry (BoE)** declaration destined for a scanned container has to come into NSCIS before the container can be reviewed. This page is where you watch that inbound flow.

Open: **Cargo → ICUMS → Download Queue** (URL: `/icums/download-queue` or `/customs/icums/download`).

## Queue columns

| Column | Meaning |
|---|---|
| Declaration # | ICUMS-assigned BoE number. |
| Container # | Which container it covers (may be blank if not yet parsed). |
| Consignee | Importing party name. |
| Received | When ICUMS first signalled the BoE was available. |
| Downloaded | When NSCIS actually pulled it. Blank = still queued. |
| Status | Queued / Downloading / Downloaded / Failed. |
| Retries | If Status is Failed, how many attempts. |

## Status lifecycle

```
Queued → Downloading → Downloaded
                    ↘
                     → Failed (retries: 1/3, 2/3, 3/3)
```

- **Queued** — NSCIS knows about the BoE, hasn't fetched it yet. Normal; will pick up in the next poll window (~60s).
- **Downloading** — in flight. Typically 200–800 ms per BoE.
- **Downloaded** — fully ingested. The BoE is now linked (or awaiting match) in Completeness.
- **Failed** — retry counter shows attempts. After 3 failures, the BoE is quarantined; an admin has to intervene.

## Filter bar

- **Status** — focus on queued / failed.
- **Date range** — inbound received between X and Y.
- **Search** — declaration # or container #.

## Common failure causes

| Error snippet | What it means | Fix |
|---|---|---|
| `HTTP 401` | Auth token expired | Usually self-heals on token refresh; if persistent, admin re-logins the service |
| `HTTP 404` | ICUMS says BoE no longer exists (recalled?) | Ops investigates; may have been cancelled |
| `HTTP 500` | ICUMS-side error | Retry; escalate if persistent |
| `Timeout` | Network / ICUMS latency spike | Automatic retry usually resolves |
| `Parse error` | ICUMS returned malformed JSON | Log to ops; investigate the specific BoE |

## Manual retry

For Failed rows (if your role has the permission):

- **Retry** button on the row → schedules an immediate re-attempt.
- **Retry All Failed** button in the header → bulk retry.
- **Dismiss** → move to quarantine; ops reviews separately.

Most retries succeed when the underlying issue was transient (ICUMS was briefly slow).

## Historical view

The **Completed** tab shows successfully-downloaded BoEs for reference. Useful for:

- Confirming a BoE we're about to submit a decision against is indeed the one we pulled.
- Auditing when an analyst says "I was working with the wrong declaration" — proves what was and wasn't available.

## Performance notes

- Poll cadence: ~60s (tunable in settings).
- Download latency: typically 200–800 ms per BoE. 2-5s during ICUMS peak hours.
- Queue can buffer thousands of BoEs without issue — the constraint is ICUMS-side rate limits, not ours.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager monitoring

Rough health thresholds:
- **Queued count** should stay <50 under normal load. Chronically higher = ICUMS slow or your poll interval is too long.
- **Failed count** should be <5 at any given time. If it spikes, look at the error codes — one repeating error means one fixable problem.
- **Age of oldest queued** should never exceed 10 min. Longer = the poll might have hung; restart the ICUMS sync service.
<!-- /requires -->

---

## What to read next

- [ICUMS Overview](/help/icums-overview) — the full picture
- [Submission Queue](/help/icums-submission-queue) — the outbound counterpart
- [BoE Request](/help/icums-boe-request) — fetch a specific BoE manually
