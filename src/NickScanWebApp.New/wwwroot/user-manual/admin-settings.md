---
title: "System Settings"
category: "Administration"
order: 30
requires: [Pages.AdminSettings]
updated: 2026-04-21
version: v2.15.0
---

# System Settings

System-wide configuration. Most settings are one-time at install / deployment; the rest are tunable knobs ops uses during normal operation.

Open: **Administration → System Settings** (URL: `/admin/settings`).

## Tabs

The page is organised into tabs:

| Tab | What lives there |
|---|---|
| **General** | Site name, timezone, logo, default landing page |
| **ICUMS** | Endpoint URL, auth credentials, poll intervals |
| **Scanners** | Per-scanner base URL, credentials, polling toggles |
| **Ingestion** | File-system watch paths, acceptance thresholds |
| **Notifications** | Email SMTP, SMS (if enabled), in-app notification rules |
| **Retention** | How long to keep images / decisions / logs |
| **Feature Flags** | Toggle experimental features (e.g. the unified image pipeline) |

## General

- **Site Name** — appears in the top header + browser title.
- **Timezone** — used for all timestamp display (stored as UTC, rendered in this zone).
- **Default Landing** — which page `/` redirects to after login (Dashboard / Workbench / Audit).
- **Logo** — upload PNG/SVG, max 200KB. Displays in the top-left header.

## ICUMS

| Setting | Typical value | Notes |
|---|---|---|
| Base URL | `https://api.icums.gov.xx/v2` | Provided by customs IT |
| Auth method | OAuth2 client-credentials | Client ID + secret |
| Download poll interval | 60 s | Lower = fresher, higher = less load |
| Submission retry delay | 30 s | How long between retries |
| Submission max retries | 3 | After this, goes to quarantine |
| Request timeout | 10 s | Individual HTTP timeout |
| Batch download size | 50 | BoEs fetched per cycle |

Changing auth credentials triggers a reconnect cycle (~10 s downtime for the ICUMS sync service).

## Scanners

One section per scanner type (FS6000, ASE, Heimann). Per scanner:

- Base URL (the scanner's web endpoint).
- Credentials (auth token / user/pass).
- Enabled toggle.
- Polling interval (seconds).
- File archive path (where raw scans land).
- Plate-OCR confidence threshold (default 0.75 — raise to reduce false plate reads, lower to improve recall).

## Ingestion

- **Watch paths** — directories monitored for new scan files.
- **Minimum file age** — how old a file must be before ingesting (prevents picking up half-written files; default 10 s).
- **Max file age to consider new** — files older than this are ignored (prevents re-ingesting archived scans; default 24 h).
- **Partial-channel acceptance** — whether to ingest scans with missing LE or MAT files (default: ingest, flag as variant `fs6000-v1-no-material`).

## Notifications

- SMTP server, port, username, password.
- From address for system emails.
- Test-email button.
- In-app notification rules: which events create notifications for which roles (e.g., "Failed ICUMS submission → notify Admins").

## Retention

Controls auto-purge:

| Artefact | Default retention |
|---|---|
| Raw scan files (.img) | 7 years |
| Main JPEG | 7 years |
| Decision records | forever |
| System logs | 90 days |
| Audit logs | 7 years |
| Notifications | 30 days |

Don't shorten these without compliance sign-off — customs often requires 5–7 year retention for post-clearance audit.

## Feature Flags

A toggle grid for enabling/disabling experimental or rollout-gated features. Currently-live flags:

- `unified-image-pipeline` — route all decode through the new pipeline (v2.11.0 — default ON).
- `raw-16bit-viewer` — Raw 16-bit toggle in the viewer (v2.12.0 — default ON).
- `roi-inspector` — ROI Inspector side panel (v2.13.0 — default ON).
- `data-integrity-strict` — reject truncated files at ingest (v2.14.1 — default ON).

Flip with care — some are paired with database schema assumptions.

## Saving

Every setting edit prompts a confirm dialog showing the diff. Click **Apply** to save.

Most settings hot-reload (no service restart). Exceptions are explicitly marked — typically the ICUMS / Scanner credentials, which require a service reconnect and will show a banner warning.

## Audit

Every settings change is logged with admin ID, timestamp, before/after values. View at [Admin Audit Logs](/help/admin-audit).

---

## What to read next

- [Admin Audit Logs](/help/admin-audit) — the log of who changed what
- [ICUMS Overview](/help/icums-overview) — the settings that affect ICUMS behaviour
