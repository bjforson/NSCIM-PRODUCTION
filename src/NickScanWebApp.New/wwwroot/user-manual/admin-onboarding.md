---
title: "Admin — onboarding checklist"
category: "Role Guides"
order: 30
requires: [Pages.AdminUsers, Pages.AdminRoles, Pages.AdminSettings]
updated: 2026-04-21
version: v2.15.0
---

# Admin onboarding checklist

Things to learn / verify during your first week as an NSCIS admin, and the daily / weekly / monthly routines after that.

## Day 1 — get oriented

- [ ] Confirm you can log in with your admin account.
- [ ] Glance at **Operations → Services Monitoring** — all five services green?
- [ ] Open **Operations → Performance** — any alarm thresholds tripped?
- [ ] Open **Cargo → ICUMS** — connection online?
- [ ] Open **Administration → Users** — how many users total, how many Admins?
- [ ] Open **Administration → Roles** — which built-in and custom roles exist?
- [ ] Read [System Settings](/help/admin-settings) top-to-bottom.

If any of the above is unexpectedly red, talk to your predecessor / the team before doing anything.

## Day 2 — Read history

- [ ] Read **Administration → Audit Logs** — filter last 30 days, scroll through. Get a feel for what events are normal, which aren't.
- [ ] Read **Administration → Logs** — filter Level=Error, last 24 h. See what errors the system produces routinely vs incidents.
- [ ] Skim **Admin → System Settings** — note which settings are set to non-default values. Those are the ones tuned for this site.

## Day 3 — dry-run the common admin actions

- [ ] Create a test user in a sandbox role. Delete it when done.
- [ ] Create a custom role with two permissions. Delete it when done.
- [ ] Change a System Settings value (non-critical one like notification rules), then change it back.
- [ ] Trigger a manual BoE request (see [BoE Request](/help/icums-boe-request)) for a test declaration.

The point isn't the outcome — it's to experience the UI flow for each action so you know where the buttons are when you're under pressure.

## Day 4 — review retention & compliance

- [ ] [Audit Logs](/help/admin-audit) — 7 year retention — confirm cold storage is configured.
- [ ] Raw scan files — default 7 years — confirm storage tier and free space.
- [ ] Database backups — confirm daily + weekly schedule is running.
- [ ] GPG / encrypted backups off-site.

These aren't NSCIS-UI tasks but they are your responsibility. Get them written down.

## Day 5 — build your monitoring habits

Daily routine (morning, ~5 min):
- All five services green ([Services Monitoring](/help/services-monitoring)).
- ICUMS connected ([ICUMS Overview](/help/icums-overview)).
- Queue depths reasonable (Download <50, Submission <20).
- Failed count 0 for both queues.
- Performance KPIs within targets.

Weekly (~30 min):
- Audit log scan — any unusual role changes, settings changes?
- User review — anyone inactive >90 days?
- Disk space on the image archive volume.
- Performance trending — per-stage latency growing over the week?

Monthly:
- Full "Users by Role" review.
- Security check — any users with Admin who shouldn't?
- Retention compliance — randomly pick a 7-year-old container; confirm record + raw scan are still retrievable.

## Ongoing responsibilities

### Incident response
When a service goes red:
1. [Services Monitoring](/help/services-monitoring) → Details → read recent logs.
2. [System Logs](/help/admin-logs) → filter by service + Level=Error.
3. Decide: restart the service, or deeper investigation.
4. Log the incident in your ticket tracker.
5. Post-incident: update settings / deploy fix if needed.

### User support
The users most often need:
- Password resets — [User Management](/help/admin-users) → Reset Password.
- Access additions — usually need a different role, not new permissions on the existing role.
- "Why can't I see X?" — often a permission they need + sometimes a misunderstanding of which role they have.

### Deployment / upgrade
- Before a deploy: confirm no active audit queue depth; users can experience brief disruption.
- During deploy: watch Services Monitoring; confirm each service comes back green.
- After deploy: run a smoke test — open Workbench, open one container, decide it. Confirm the flow works end-to-end.

## Things you absolutely must not do without a plan

- Don't change **Retention** settings — compliance risk.
- Don't delete users (only deactivate).
- Don't change **ICUMS Base URL** without coordination with customs IT.
- Don't grant Admin to more than 3 people at a site.
- Don't disable audit logging.

## Escalation tree

If something's over your head:
- **Application bug** → developer team / support vendor.
- **ICUMS unreachable** → customs IT contact.
- **Scanner offline** → scanner vendor (Nuctech / Smith / etc.) support.
- **Database issue** → DBA / sysadmin.
- **Hardware / infra** → whoever owns the server hosting.

Keep that list in your wallet.

---

## What to read next

- [User Management](/help/admin-users) — day-to-day user ops
- [Role Management](/help/admin-roles) — how role changes ripple through
- [System Settings](/help/admin-settings) — all the knobs
- [Admin Audit Logs](/help/admin-audit) — the long-retention event log
