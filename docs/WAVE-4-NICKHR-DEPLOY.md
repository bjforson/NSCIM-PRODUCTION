# Wave 4 — NickHR module-access deploy runbook (2026-04-29)

> NickHR adopted `NickERP.Platform.Identity` and gained a "Module access"
> tab on `EmployeeDetail` plus a per-row dialog on `Admin/Users`. NickFinance
> hardened the user-creation path: **HR provisions, NickFinance refuses to
> auto-create**. This runbook walks the operator through the live deploy.

## What's already done by the orchestrator

- **NickFinance side deployed.** Build verified, published, restarted as
  `NT SERVICE\NickFinance_WebApp`. Smoke test passed (voucher
  `PC-76FD5C-2026-00007` disbursed end-to-end). The friendly
  `/access-not-provisioned` page returns HTTP 200.
- **No DB migration needed.** NickHR is a *consumer* of the existing
  `identity.*` schema; the bootstrap CLI in
  `finance/NickFinance.Database.Bootstrap` remains the migration owner.
- **Existing `identity.users` rows preserved.** The hardening is
  lookup-by-`cf_access_sub`-then-`email`. Users that were lazy-created
  earlier in the rollout (smoke users; any operator that logged in
  before today) all stay valid.

## What the operator needs to do

### 1. Deploy NickHR (FIRST — before any human re-logs into NickFinance)

NickHR currently runs as Windows service `NickHR_WebApp` (verify with
`sc.exe qc NickHR_WebApp`). Standard publish + restart:

```powershell
# Run as Administrator
Stop-Service NickHR_WebApp
dotnet publish "C:\Shared\NSCIM_PRODUCTION\NickHR\src\NickHR.WebApp\NickHR.WebApp.csproj" `
    -c Release `
    -o "C:\Shared\NSCIM_PRODUCTION\NickHR\publish\WebApp"
Start-Service NickHR_WebApp
```

Verify via `https://hr.nickscan.net/employees` — the EmployeeDetail page
should show a new **"Module access"** tab.

### 2. Backfill existing CF-Access-onboarded users (one-time)

Anyone who logged into NickFinance before today already has a row in
`identity.users` from the lazy-create path — they're not at risk after
hardening. But they have **no NickFinance role grants yet**. Walk
through `https://hr.nickscan.net/admin/users` and for each existing
user:

1. Click the security icon next to their row (opens `ModuleAccessDialog`)
2. Pick the right roles (most operators are `FinanceLead` for now;
   custodians at sites are `Custodian`; site managers / approvers
   per their actual role)
3. Optionally set their primary phone (E.164 format, e.g. `+233241234567`)
   — this is what the WhatsApp approval notifier will use once Meta
   credentials land

Quick SQL to find existing users that need role grants:
```sql
SELECT u.email, u.display_name, u.last_seen_at
  FROM identity.users u
  LEFT JOIN identity.user_roles ur ON ur.user_id = u.internal_user_id
 WHERE ur.user_role_id IS NULL
 ORDER BY u.last_seen_at DESC NULLS LAST;
```

### 3. Deploy NickFinance (already done by orchestrator on 2026-04-29)

If a future re-deploy is needed, the standard sequence is unchanged:

```powershell
Stop-Service NickFinance_WebApp
dotnet publish "C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\NickFinance.WebApp.csproj" `
    -c Release `
    -o "C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp"
Start-Service NickFinance_WebApp
```

## What changed in user-facing behaviour

### NickHR
- **EmployeeCreate** — when HR onboards a new employee, the same flow now writes a row into `identity.users` (idempotent on lower-cased work email). If the identity write fails, the employee row is still saved; HR sees a yellow banner and re-runs from EmployeeDetail's "Module access" tab.
- **EmployeeDetail → Module access tab** — provisioning status, primary phone editor, 6 NickFinance role checkboxes (Approver and SiteManager have a per-row site dropdown).
- **Admin/Users → security icon** — same panel for non-employee admins (auditors, finance leads, contractors).

### NickFinance
- **First-time CF Access login as an unprovisioned user** — instead of silently auto-creating an `identity.users` row, the user sees a friendly page at `/access-not-provisioned`:
  > Your access hasn't been provisioned yet
  > Hi {email} — you've signed in via Cloudflare Access, but you don't have a NickFinance account yet. Contact your NickHR administrator to provision your access...
- **Already-provisioned users** — no behaviour change. Login → home page as before.
- **Local `dotnet run`** (non-Production) — lazy-create still works for local dev convenience.

## Risks the agent flagged

1. **Site UUIDs are hardcoded** in `ModuleAccessPanel.razor` — six placeholder UUIDs (`11111111-1111-1111-1111-00000000000{1..6}`). If the production sites table uses different UUIDs, the dropdown won't align. Verify before HR starts assigning site-scoped Approver/SiteManager grants. Long-term fix: SiteCatalogue service in NickHR.
2. **Non-employee admins auto-provisioned** when they grant their first role (FK on `user_roles.granted_by_user_id` requires the granter to exist in `identity.users`). Intentional — but worth noting that "you, the HR admin" become an `identity.users` row the first time you touch the Module access UI.
3. **Dev fallback still lazy-creates.** A developer running `dotnet run` against the production DB could still create rows. Production deploys must keep `ASPNETCORE_ENVIRONMENT=Production` (existing convention; verify `[Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Machine')` returns `Production` on TEST-SERVER).
4. **Existing Blazor Server circuits** that survived the deploy hold a cached `CurrentUser`. Restart drops all circuits — they rebuild on reconnect, no extra concern.

## Verification after deploy

- [ ] `Get-Service NickHR_WebApp` shows Running
- [ ] `https://hr.nickscan.net/employees/{any-employee-id}` renders the Module access tab
- [ ] `https://hr.nickscan.net/admin/users` shows the security icon column
- [ ] Provision yourself (the deploying admin) via EmployeeDetail → Module access → Provision now → grant `Admin` role
- [ ] Open `https://finance.nickscan.net/` — you should land on the home page (you're provisioned)
- [ ] (Optional) Pretend to be an unprovisioned user via incognito + a different `@nickscan.com` address — you should see `/access-not-provisioned` instead of the home page
- [ ] Smoke run: `dotnet run --project C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.Database.Bootstrap -- --conn "<conn>" --skip-migrations --smoke-test` should still pass (it doesn't go through the WebApp)

## Reference — what landed in code

- 11 new tests in `NickHR.WebApp.Tests/IdentityProvisioningServiceTests.cs` (provision idempotency, role grant idempotency, distinct-site grants, phone upsert, etc.) — 11/11 pass with `NICKFINANCE_TEST_DB` set
- 3 new tests in `NickFinance.WebApp.Tests/AccessProvisioningHardeningTests.cs` (production-without-row throws, production-with-row returns CurrentUser, non-production lazy-creates) — pass with `NICKFINANCE_TEST_DB` set
- `IIdentityProvisioningService` API: `ProvisionEmployeeAsync(email, displayName, tenantId)`, `GrantRoleAsync(userId, roleName, siteId, grantedBy, expiresAt)`, `RevokeRoleAsync(userId, roleName, siteId)`, `ListRolesAsync(userId)`, `SetPrimaryPhoneAsync(userId, phoneE164)`
