---
title: "Role Management"
category: "Administration"
order: 20
requires: [Pages.AdminRoles]
updated: 2026-04-21
version: v2.15.0
---

# Role Management

Roles are **named bundles of permissions** that get assigned to users. The Role Management page is where you edit existing role bundles and create custom ones.

Open: **Administration → Roles** (URL: `/admin/roles`).

## What you see

Grid of role cards. Each card shows:

- Role display name + badge (Built-in / Custom).
- User count chip.
- Permission count summary.
- Actions: Edit / Duplicate / Delete (only for Custom).

Top-right: **Create Custom Role** button.

## Built-in vs Custom

**Built-in roles** (Analyst, Auditor, Manager, Admin, ReadOnly) ship with NSCIS and cannot be deleted. You **can** edit their permission sets, but changes propagate to every user in that role — do so carefully.

**Custom roles** are created by you to cover gaps — e.g., "Junior Analyst" with the analyst surface but no Abnormal-marking power, or "ICUMS Operator" with only ICUMS queue access.

## Creating a custom role

Click **Create Custom Role** → dialog:

1. **Role Name** — internal identifier, kebab-case (`junior-analyst`). Cannot be changed later.
2. **Display Name** — what users and admins see in the UI.
3. **Base Role** — optionally clone from an existing role's permissions as a starting point.
4. **Description** — free-text explanation of the role's purpose. Recommended.

Hit **Create** → the role is created with base permissions (or empty if no base). Edit it to adjust.

## Editing a role's permissions

Click **Edit** on a role card → the permission tree opens:

```
Pages
  ├─ Dashboard
  │   ├─ view          [✓]
  │   └─ analytics     [ ]
  ├─ Containers
  │   ├─ view          [✓]
  │   ├─ details       [✓]
  │   └─ processing    [ ]
  ├─ ImageAnalysis
  │   ├─ view          [✓]
  │   ├─ audit         [ ]
  │   └─ management    [ ]
  └─ ...
Images
  ├─ view              [✓]
  ├─ annotate          [✓]
  └─ edit              [ ]
...
```

Toggle checkboxes. The save button commits all changes in one transaction.

**Permission categories** (from the tree):

- **Pages** — which pages the user can navigate to.
- **Images** — operations on scan images.
- **Containers** — approve, reject, validate, export.
- **Icums** — download, submit, sync.
- **Admin** — user/role/settings management.
- **Reports** — view / export / create templates.

## Effect of role changes

- User's existing session: permission changes take effect on next page navigation (mid-session update).
- User's new sessions: take full effect immediately.
- Claims cache: invalidated on save; users may briefly see a stale menu until next request.

## Deleting a custom role

**Only** allowed if no users are assigned to it. If users are assigned, the Delete button is grayed out. Reassign those users first.

Built-in roles can never be deleted.

## "Users in Role" view

Click the user count chip on a role card → opens a slide-over showing every user assigned to that role. From there you can click a user to jump to their edit dialog.

## Permissions best practice

- **Principle of least privilege**. Start a custom role empty; add only what's needed.
- **Don't nest too deep**. If you need "Analyst but without Raw 16-bit access", create a custom role; don't try to slice inside an existing role by other means.
- **Name roles functionally, not by person**. `split-review-operator`, not `john-s-role`.
- **Document the purpose**. Fill in the Description — it shows up in audit logs and helps future admins understand intent.

## Audit

Every role change is logged with: admin ID, timestamp, before/after permission set. View at **Administration → Audit Logs**.

---

## What to read next

- [User Management](/help/admin-users) — assign roles to users
- [Admin Audit Logs](/help/admin-audit) — role-change history
