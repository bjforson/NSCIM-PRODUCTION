---
title: "Performance metrics"
category: "Monitoring"
order: 30
requires: [Pages.Performance, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Performance metrics

The Performance page is the **site-wide throughput + latency dashboard**. If you're a manager or ops lead, this is where you confirm the system is running healthily — or find the bottleneck when it isn't.

Open: **Operations → Performance** (URL: `/performance`).

## The four headline KPIs

Across the top:

- **Scans Ingested / hour** — rate of new scans entering the system.
- **Decisions / hour** — analyst + audit throughput (combined).
- **Submissions / hour** — ICUMS outbound rate.
- **Avg Container Time** — wall-clock from ingest to ICUMS-synced for the median container.

Healthy site targets (rough — tune to your volume):
- Scan rate matches scanner output (depends on physical traffic).
- Decision rate = scan rate within ±10% (pipeline keeping up).
- Submission rate = decision rate (ICUMS catching up).
- Avg container time = 2–6 hours.

If any of these diverges, the pipeline is stalling somewhere — the lower panels tell you where.

## Per-stage latency

A row chart showing median + p95 time in each stage:

```
Ingest        ▰▱  — median 12 s,    p95 45 s
Completeness  ▰▰▱ — median 4 min,   p95 3 h
Workbench     ▰▱  — median 2 min,   p95 10 min
Audit         ▰▱  — median 1 min,   p95 5 min
Submission    ▰▱  — median 800 ms,  p95 3 s
```

**If a stage's p95 balloons while median is normal**: you have a tail-latency problem (e.g., one failing BoE stuck retrying).

**If the median itself grows**: the stage is overloaded and needs more capacity.

## Per-analyst / per-auditor throughput

Two tables:

- **Analyst throughput** — decisions per user today. Compare to site average to spot outliers.
- **Audit throughput** — signoffs per auditor.

Colour-coded: green = above avg, yellow = slightly below, red = significantly below.

Use for:
- **Training**: a new analyst at 30% of average might need mentoring.
- **Workload distribution**: if the top analyst is at 200% and bottom is at 20%, reassign.
- **Sickness / off-shift detection**: unexpectedly zero throughput signals someone's not at their desk.

## System-level metrics

Expandable section:

| Metric | Healthy | Alarm threshold |
|---|---|---|
| DB connection pool usage | <60% | >90% |
| ImageProcessing queue depth | <10 | >50 |
| Scan decode cache hit rate | >70% | <40% |
| API p99 latency | <1 s | >5 s |
| ICUMS p99 submission | <3 s | >15 s |

When any alarm threshold trips, a badge appears at the top of the page pointing to the offender.

## Time-range toggles

- Last 1 h / 4 h / 24 h / 7 d.

Default is 24 h. Use 1 h during an incident to see what's happening right now.

## Historical compare

Toggle: **Show previous period overlay** → compare today to yesterday, this week to last week. Useful for catching gradual regressions ("we're slower than we were last Tuesday").

## Exporting

- **Export CSV** — the raw metric table for offline analysis.
- **Snapshot report** — a PDF summary of current state. Good for a daily ops report email.

## Troubleshooting quick wins

### Avg Container Time climbs over weeks
Look at per-stage latency — usually one stage is widening. Probably capacity (more scans than the team can keep up with) or ICUMS slowdown.

### Decisions / hour drops suddenly
Check per-analyst throughput. If most are zero, it's a system issue (WebApp down? SignalR broken?). If one person is zero, they're on break.

### DB pool > 90%
Someone is running an unbounded query. Check **Services Monitoring → API → recent errors**. Typically an analytics query gone wild.

---

## What to read next

- [Services Monitoring](/help/services-monitoring) — the per-service health view
- [System Logs](/help/admin-logs) — drill into errors when metrics alarm
- [How a scan becomes a customs record](/help/normalization-flow) — the pipeline that these metrics measure
