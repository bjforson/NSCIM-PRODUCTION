---
title: "Audit Logs — business events"
category: "Administration"
order: 60
requires: [Pages.AdminAudit]
updated: 2026-04-21
version: v2.15.0
---

# Audit Logs

Separate from the system logs, the **Audit Logs** capture **business events** — decisions, overrides, role changes, settings changes, container lifecycle transitions. This is the customs-compliance-grade log.

Open: **Administration → Audit Logs** (URL: `/operations/errors` → Audit tab, or `/admin/audit`).

## What's captured

Every event that matters for compliance and retrospective investigation:

| Event type | Example |
|---|---|
| Decision | Analyst marked container X as Abnormal |
| Audit action | Auditor approved / escalated / sent-back |
| Override | Manager reassigned a container to a different analyst |
| Settings change | Admin changed ICUMS polling interval |
| Role change | Admin changed user X's role from Analyst to Auditor |
| User management | User created / deactivated / password reset |
| Linkage | Scan re-linked to a different BoE |
| Split | Split-review approved or rejected |
| ICUMS submission | Container submitted / acknowledged / failed |

Each entry includes:

- Event type
- Timestamp (UTC, rendered local)
- Actor (user ID + role at the time)
- Target (container / user / setting / whatever)
- Before / after values (for edits)
- Free-text reason (when the UI prompts for one)
- Source IP + user agent
- Correlation ID linking to system logs

## What's NOT in audit logs

- Routine reads (viewing a container doesn't log).
- Menu navigation.
- UI preferences (filter saves, theme changes).

Audit logs are append-only — you cannot edit or delete entries through the UI.

## Retention

**7 years** by default (customs-standard). Never shorten without sign-off.

Storage is a dedicated `audit_logs` table with periodic archival to immutable cold storage.

## Filtering

The page's filter bar:

- **Event type** — dropdown of the taxonomy above.
- **Actor** — specific user.
- **Target** — container number, user, or setting key.
- **Date range** — 24h / 7d / 30d / 90d / custom.
- **Text search** — reason / note substring.

## Common queries

### "Who decided container X?"
Filter: Target = X's container number. Shows full decision chain — analyst decision, audit action, any send-back/re-decide cycles.

### "What did user Y do this week?"
Filter: Actor = Y. Shows every auditable action Y took.

### "Who changed the ICUMS URL in March?"
Filter: Event type = settings-change, Text search = "icums", Date range = March. Shows settings history.

### "All overrides last month"
Filter: Event type = override. Review each — overrides should be rare and justified.

## Exporting

- **CSV export** — for offline analysis, up to 100K rows.
- **Compliance report export** — a pre-formatted PDF showing all events for a specific container (used in customs audits). Permission-gated (`reports.view`).

## Inspection workflow

When customs / compliance wants to audit a specific clearance:

1. Find the container via Containers search.
2. Open Container Details → Audit Trail tab (container-scoped view).
3. OR open this Audit Logs page → filter by the container number → export.

Both paths reach the same underlying data; the container-scoped view is more readable for a single case, while this page is better for broad investigations.

## Immutability guarantee

Audit log entries are never updated or deleted by application code. The DB role the app runs as has INSERT-only permission on this table. Any admin with DB credentials could still tamper at the DB level — but our backup trail and hash chain (logged to external log storage) detects this.

## When something looks wrong

If an audit entry doesn't match reality (e.g., a decision you know was made but doesn't appear), check:

1. Was the action truly completed, or did it error mid-transaction? Compare timestamps with system logs.
2. Was the service clock in sync at the time? Occasional clock skew can place events seemingly out of order.
3. Was the user's session valid? If a session expired mid-action, the event may not have landed.

For discrepancies that can't be explained, escalate to ops — may indicate a bug or a breach.

---

## What to read next

- [System Logs](/help/admin-logs) — the technical/error log counterpart
- [User Management](/help/admin-users) — actions on users all land here
- [Role Management](/help/admin-roles) — role changes land here
