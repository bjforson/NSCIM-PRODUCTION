---
title: "System Logs"
category: "Administration"
order: 50
requires: [Pages.AdminLogs]
updated: 2026-04-21
version: v2.15.0
---

# System Logs viewer

NSCIS services write structured logs for every request, decision, submission, error, and lifecycle event. The Log Viewer is the in-app way to read them without SSHing into a server.

Open: **Administration → Logs** (URL: `/admin/logs`).

## What's shown

A streaming table with:

| Column | Meaning |
|---|---|
| Timestamp | UTC; rendered in your site timezone |
| Level | Debug / Information / Warning / Error / Critical |
| Source | Which service (WebApp, API, IngestionService, ImageProcessing, ICUMS sync, etc.) |
| Category | Logger name, usually the class that wrote the entry |
| Message | Formatted log line |
| Actions | Expand → structured properties + exception (if any) |

Most operators only ever need to filter on Level=Error or Level=Warning to find problems.

## Filters

- **Service** — which of the 6 NSCIM_* services (Gateway, API, Ingestion, ImageProcessing, ICUMS sync, WebApp).
- **Level** — minimum log level to show.
- **Time range** — last 15m / 1h / 4h / 24h / custom.
- **Search** — substring search in the message field.
- **Category** — substring match on the logger name.

Filters combine (AND). Results stream in real-time via SignalR — new log entries appear at the top without refresh.

## Log volumes

Typical per-hour volume on a healthy site:
- Debug: 50,000–200,000 (off by default — only enable for short diagnostic windows).
- Information: 5,000–20,000 (routine events).
- Warning: 20–200 (worth glancing at).
- Error: 0–20 (investigate each).
- Critical: 0 (anything non-zero = incident).

## Common patterns

### Routine Warnings (safe to ignore at low volume)

- `Permission denied for 'xxx' (Context: NavMenu)` — user without access tried a menu item. Expected.
- `ICUMS poll returned no new BoEs` — quiet period. Not a problem.
- `Retry 1/3 for submission of container XXX` — transient submit issue.

### Errors that deserve attention

- `Failed to decode scan {scanId}: {exception}` — look at the scan; might be a corrupt file.
- `ICUMS submission exhausted retries for {container}` — quarantined submission.
- `Database connection pool exhausted` — DB capacity issue.
- `Unhandled exception in {endpoint}` — bug; open a ticket with the stack trace.

### Critical

- Authentication subsystem failures.
- Database outages.
- Scanner service crash loops.

Any critical entry should page ops.

## Exporting

- **Export filtered set as CSV** — up to 100,000 rows per export.
- **Export to share** (e.g., with ticketing) — generates a signed URL that shows the filtered view to whoever has the link. Expires in 24 h.

## Log retention

Default: 90 days. Older logs are rotated out (archived to cold storage if your site has it configured, otherwise deleted).

Audit logs (below) have their own 7-year retention policy — much longer than system logs.

## Tips

- **Correlation IDs**: every user request carries an `X-Correlation-Id` header propagated through all services. Filter by correlation ID to trace a request end-to-end.
- **During an incident**: flip Level filter to Error, time range to "last 15m" — that's usually enough to see the scope.
- **Don't leave Debug on in production**: it 10× the log volume and can overwhelm the storage.

---

## What to read next

- [Admin Audit Logs](/help/admin-audit) — the separate, long-retention business-event log
- [Services Monitoring](/help/services-monitoring) — if a service keeps erroring
