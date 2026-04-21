---
title: "Services Monitoring — health of the five services"
category: "Monitoring"
order: 10
requires: [Pages.ServicesMonitoring, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Services Monitoring

NSCIS runs as **five separate services** that communicate over HTTP. The Services Monitoring page shows the live health of each one.

Open: **Operations → Services Monitoring** (URL: `/operations/services`).

## The five services

| Service | Port | Role |
|---|---|---|
| **Gateway** | 5000 | Public-facing entry; routes requests to the right backend |
| **API** | 5100 | Business logic + database, answers REST calls from the WebApp |
| **IngestionService** | 5200 | Watches scanner directories, imports new scans |
| **ImageProcessing** | 5300 | Decodes raw channels, renders modes, computes ROI stats |
| **ICUMS Sync** | 5400 | Polls ICUMS for BoEs, submits decisions outbound |

Plus a sixth process: the **Scheduler** (in-process scheduled tasks inside the API) and the **Notifier** (SignalR hub inside the WebApp).

## Cards per service

Each service gets a card with:

- Name + port + process ID.
- Uptime.
- Health chip (Healthy / Degraded / Unhealthy / Offline).
- Last-healthcheck timestamp.
- Quick stats: CPU %, memory MB, open connections.
- **Details** button → slide-over with recent log excerpts + detailed probes.

## Health check criteria

A service is **Healthy** when:
- Process is alive.
- Its `/health` endpoint returns 200 within 2 s.
- Any declared dependency (DB, peer service, external API) responds.

**Degraded** when:
- Health endpoint responds slow (>2 s but <10 s).
- A non-critical dependency is intermittently failing.
- Memory usage >85% of configured limit.

**Unhealthy** when:
- Health endpoint returns non-200 or times out.
- A critical dependency is down (e.g., API can't reach DB).

**Offline** when:
- Process isn't running.

## Acting on a red service

Use the **Details** slide-over to see:

- Last 20 error log lines.
- Dependency probe results (DB, peers, external).
- Memory/GC history chart.

Common causes:

| Symptom | Typical cause | Fix |
|---|---|---|
| API offline | Crashed (OOM? unhandled exception?) | Check logs; restart via service control |
| Ingestion degraded | File-system watch path unreachable | Check mount; restart service |
| ImageProcessing slow | Too many concurrent decode requests | Check queue depth; scale horizontally |
| ICUMS Sync unhealthy | ICUMS auth expired / URL changed | Check settings; re-auth |
| Gateway 502 | Backend service crashed | Restart backend |

## Restart + log streaming

With the right permission (**ServiceControl**):

- **Restart** button per service.
- **Stream logs** button → opens a live tail of the service's stdout log. Press Esc to close.

Restarting a service:
- Gateway: 1–2 s of 502 for users; self-heals.
- API: brief blip of WebApp disconnect; auto-reconnects via SignalR.
- IngestionService: no user impact; resumes on next poll cycle.
- ImageProcessing: viewer operations pause briefly; resume on reconnect.
- ICUMS Sync: queues pause during restart (seconds); no data loss.

## History view

The bottom of the page has a **24-hour status strip** per service showing uptime bands — useful for spotting recurring outages or post-deploy instability.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager daily check

A 30-second morning ritual:
1. All five services green.
2. 24h history strips show no red bands.
3. Memory utilisation stable (no leak climbing).
4. ICUMS connection up.

If any of these is off, dig into the specific service's Details pane before users report issues.
<!-- /requires -->

---

## What to read next

- [System Logs](/help/admin-logs) — drill into errors from a specific service
- [Scanners Overview](/help/scanners-overview) — the scanner-side health checks
- [Performance](/help/performance) — throughput + latency metrics
