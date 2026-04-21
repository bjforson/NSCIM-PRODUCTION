---
title: "User Management"
category: "Administration"
order: 10
requires: [Pages.AdminUsers]
updated: 2026-04-21
version: v2.15.0
---

# User Management

The User Management page is where system users are created, edited, assigned roles, and deactivated.

Open it: **Administration → Users** (URL: `/admin/users/management`).

## What you see

Header: **Create User** and **Refresh** buttons.

Stats cards: Total Users / Active Users / Administrators / Recently Added.

Main table: every user with

| Column | Meaning |
|---|---|
| Name | First + last name. |
| Username | Login identifier. |
| Email | Notification/reset target. |
| Role | Assigned primary role (dropdown). |
| Status | Active / Inactive. |
| Last Login | Most recent successful auth. |
| Actions | Edit / Reset Password / Deactivate. |

## Creating a user

Click **Create User** → dialog opens:

1. **Full Name** — required.
2. **Username** — unique, 3–32 chars, alphanumeric + dots.
3. **Email** — required for password reset emails.
4. **Role** — dropdown sourced from [Role Management](/help/admin-roles). Must be set at create time; can be changed later.
5. **Temporary Password** — auto-generated. User must change on first login.
6. **Send Welcome Email** — checkbox (default on) emails the user with username + temp password.

Click **Create**. The new user appears in the list immediately.

## Editing an existing user

Click the edit (pencil) icon on a row → dialog:

- Change name / email (not username — usernames are immutable once created).
- Change role (takes effect on their next login session).
- Toggle Active / Inactive.

Changes are logged in the audit trail with your user ID.

## Deactivating (vs deleting)

We don't delete users — we deactivate them. Why:

- Historical audit records reference user IDs; deletion would leave orphan references.
- Compliance requirements often require keeping user records for 7+ years.
- A deactivated user can be reactivated without losing history.

To deactivate: row's **Deactivate** button → confirm dialog → user can no longer log in, but all their past decisions and audit entries remain associated with their ID.

## Password reset

**Reset Password** on a row generates a new temporary password and emails it to the user's address on file. The old password is invalidated immediately. User must change on first login.

Don't reset passwords in bulk — it triggers spam-filter flags on some mail servers. If you need to rotate everyone's credentials, run a scripted reset via the backend API instead.

## Role assignment

Each user has exactly **one primary role**. Role controls every permission the user sees. To grant a subset of a higher role's permissions, create a custom role (see [Role Management](/help/admin-roles)) rather than editing built-in ones.

Built-in roles shipped with NSCIS:

- **Analyst** — image review + decision making.
- **Auditor** — second-tier review.
- **Manager** — management-level oversight + reporting.
- **Admin** — system administration.
- **ReadOnly** — read-only access for compliance reviewers.

## Search + filter

Above the table:

- **Search** — full-name or username substring.
- **Role filter** — show only users with a specific role.
- **Status filter** — active / inactive.

Filters persist across page reloads.

## Audit

Every user action (create, edit, role-change, deactivate, password-reset) is logged with your admin ID + timestamp. View at **Administration → Audit Logs**.

## Best-practice housekeeping

- **Quarterly review** — filter Inactive users >90 days and deactivate if they've truly left.
- **Role audit** — run the built-in "Users by Role" report once a month; flag anyone with Admin who doesn't need it.
- **Keep Admins few** — 2–3 is typical for a single site; more than 5 usually indicates scope creep.

---

## What to read next

- [Role Management](/help/admin-roles) — create and edit role permission sets
- [Admin Audit Logs](/help/admin-audit) — see who changed what and when
