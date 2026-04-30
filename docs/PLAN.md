# NickFinance — consolidated work plan (2026-04-28)

> Source documents: `theme-audit.md`, `security-audit.md`, `feature-backlog.md`,
> `ux-punch-list.md` (all under `docs/`). This plan synthesises those four
> reports into an ordered, sized work programme.

---

## Headline numbers

- **P0 hotfixes:** 3–4 hours. Address findings that materially raise blast
  radius today.
- **Phase 1 (auth foundation + theme migration, parallel):** ~5–7 days
- **Phase 2 (streamline + top features, parallel):** ~5–7 days
- **Total:** ~3 weeks of focused work, with visible progress every day.

The order matters. Auth before features (every feature without RBAC has to
be re-authed later). Theme before bulk UI work (porting 30 pages to a token
system means restyling each one — doing it after streamlining means
restyling them twice). Auth and theme run in parallel because they're
disjoint surfaces (server-side vs CSS / Razor).

---

## P0 — fix today, before the broader plan

These are findings where the gap-to-fix is tiny but the gap-to-impact is
large. None blocks the broader plan; all reduce blast radius.

### P0.1 — switch DB connection from `postgres` superuser to `nscim_app` (~30 min)

`scripts/install-nickfinance-service.ps1:38` writes
`Username=postgres` into the `ConnectionStrings__Finance` machine env var,
overriding `appsettings.json:11`'s correct `nscim_app` default. Effect: any
WebApp RCE = full DB takeover, append-only triggers bypassed.

Fix: change the install script's connection string to use `nscim_app`,
verify the role exists with the right grants, restart the service.

### P0.2 — fail-closed CfAccess wiring in Production (~1 hr)

`Services/CfAccessAuth.cs:45-50` returns `false` and silently disables auth
when `TeamDomain` is unset. Wipe the env var → production downgrades to
`dev@nickscan.com` for everyone.

Same file line 82: `ValidateAudience = !string.IsNullOrWhiteSpace(audience)`.
Audience unset = audience not validated; any JWT from the same team domain
authenticates.

Fix: throw a fatal startup exception when `app.Environment.IsProduction()`
AND either `TeamDomain` or `Audience` is missing.

### P0.3 — confirmation dialogs on irreversible actions (~1 hr)

UX audit and security audit both flag this. **Issue** mints a real GRA IRN;
**Disburse** posts a journal; **Approve / Reject / Void** can't be undone
through the UI. Currently first-click fires.

Fix: add a single shared `<ConfirmButton>` component (one Razor file +
JS interop for `confirm()`) and wire it onto Approve, Reject, Issue,
Disburse, Void. ~4 sites.

### P0.4 — `<NotFound>` template + `/Error` route (~30 min)

`Routes.razor` has no NotFound template; typo URLs render Blazor's raw
default outside the layout. `Program.cs:218`'s
`app.UseExceptionHandler("/Error", ...)` points at a route that doesn't
exist — production exceptions hit a 404 cascade.

Fix: add `Components/Pages/NotFound.razor` and `Components/Pages/Error.razor`
and wire both.

### P0.5 — clean up live-DB smoke pollution (~15 min)

`nickhr.tenant_id=1` now contains 3 smoke vouchers (`PC-...00001/2/3`)
tagged `SMOKE TEST`. Real users will see them in their lists.

Fix: change the smoke runner to use `tenant_id = 999_999`; one-time delete
existing `WHERE Purpose LIKE 'SMOKE TEST%'` rows from tenant 1.

---

## Phase 1 — parallel tracks (~5–7 days)

### Track 1A — Auth foundation (4–5 days)

Closes the Critical + High security findings. Order is tight because each
step depends on the previous.

1. **Persist users** (1 day) — new `identity` schema with `users(internal_id, cf_access_sub, email, display_name, status, created_at)` + `user_phones(user_id, phone_e164)` for the WhatsApp resolver. Migration in the bootstrap CLI. The `CurrentUser` factory now first looks up by `sub`, falls back to creating a row on first login, and returns the persisted `internal_id` (no more SHA-256-of-email derivation).
2. **Roles + assignments** (½ day) — `roles(role_id, name, description)` seeded with `Custodian / Approver / SiteManager / FinanceLead / Auditor / Admin`. `user_roles(user_id, role_id, site_id?, granted_by, granted_at, expires_at?)`. Site-scoped roles (e.g. "Approver for Tema only").
3. **Authorization policies** (1 day) — register policies in DI: `SubmitVoucher`, `ApproveVoucher`, `DisburseVoucher`, `IssueInvoice`, `VoidInvoice`, `PostJournal`, `CloseUserPeriod`, `ManageUsers`, `ViewReports`. Each maps to one or more roles. Apply `[Authorize(Policy = ...)]` on every Razor page that mutates.
4. **Tenant isolation** (½ day) — EF query filters on every DbContext: `modelBuilder.Entity<X>().HasQueryFilter(x => x.TenantId == _tenantAccessor.Current)`. Stop accepting `TenantId` from request bodies; derive from the authenticated user. Postgres RLS as defence-in-depth (deferred to Phase 2 — query filters cover us for tenant 1 today).
5. **Security audit log** (½ day) — `security.audit_log(event_id, user_id, action, target_type, target_id, ip, user_agent, occurred_at, details_json)`. Wire from a tiny `ISecurityAudit.RecordAsync(...)` service called from policy-protected actions.
6. **Headers + rate limiting + circuit re-validation** (½ day) —
   - CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy via a small middleware.
   - ASP.NET Core 8+ rate limiting: 60 req/min per CF Access user for the app; 10/min from any IP for `/api/whatsapp/webhook`.
   - `RevalidatingServerAuthenticationStateProvider` so circuits revalidate the JWT every 30 minutes — JWT expiry / CF Access revocation propagates.
7. **Service account + receipt encryption** (½ day) —
   - Switch the Windows Service from LocalSystem to a dedicated `NickFinance$` virtual service account with file ACLs only on the receipt root + write to `C:\Logs\NickERP\NickFinance.WebApp`.
   - Wrap `LocalDiskReceiptStorage` with at-rest AES-256-GCM (key from machine env var `NICKFINANCE_RECEIPT_DEK`).

Defers to phase 2: Postgres RLS (current EF filters are enough for single-tenant), off-host backup automation (script exists; just needs a destination), full WHT certificate / forensic export tooling.

### Track 1B — Theme migration (3–4 days)

From the theme audit, in the recommended PR boundaries:

1. **Tokens consolidation** (½ day) — rewrite `platform/NickERP.Platform.Web.Shared/Branding/theme-tokens.css` to use the realised `--nep-*` indigo palette (Portal-shipped). Update `BrandConstants.cs` to match (`Primary = "#4F46E5"`). Alias old `--nickerp-*` for back-compat.
2. **NickFinance project ref + token swap** (½ day) — add `<ProjectReference>` to Web.Shared in `NickFinance.WebApp.csproj`. Rewrite `wwwroot/app.css` literals to `var(--nep-*)`. Load Inter font.
3. **Extract 8 shared components** (1.5 days, the only multi-day item) — `NepPage`, `TopNav`, `NepCard`, `NepStat`, `NepPill`, `NepTable`, `NepEmpty`, `NepPageHeader`, `Money`. Each ≤80 lines.
4. **Port 21 NickFinance pages** (1.5 days, mechanical) — `<section class="card">` → `<NepCard>`; raw `(minor / 100m).ToString("N2")` → `<Money Minor=... Currency=...>`; tables to `<NepTable>`. ~10 min/list page, ~30 min/detail page.
5. **MainLayout swap + app switcher** (½ day) — replace handcrafted topnav with `<TopNav>` from the shared lib; add app switcher dropdown (Portal / HR / Finance / Scan).
6. **Validate by lifting Portal Home onto the same components** (½ day) — exercises the lib on its origin app; eliminates drift.

Decision points before this track starts:
- **Indigo `#4F46E5` (Portal-realised) or blue `#1d4ed8` (declared)?** Recommend indigo; Portal already shipped.
- **Module finance accent — emerald `#059669`?** Indigo for chrome, emerald for finance-specific bits (paid pill, AR aging green band).
- **MudBlazor for icons (Portal/HR pull it; Finance doesn't)?** Recommend yes — consistency at ~600 KB cost; icons-only usage pulls just the icon assembly.
- **Logo asset?** No SVG exists. Either keep Portal's "icon-glyph-in-gradient-box" pattern, or commission a real logo. Recommend the gradient-box pattern for now; commission later as a one-off task.

---

## Phase 2 — parallel tracks (~5–7 days)

### Track 2A — Streamline (3–4 days)

Closes the biggest UX punch-list items.

1. **Real entity pickers** (1 day) — typeahead `<EntityPicker>` component, instances for `<SitePicker>` (sites table or hardcoded for now), `<UserPicker>` (now that there's a users table), `<AccountPicker>` (CoA), `<CustomerPicker>`, `<VendorPicker>`, `<FloatPicker>`. Replace every raw-GUID input.
2. **Pagination + filters + sort** (1 day) — `<NepTable>` gains pagination (page size 25/50/100), column-click sort, top-of-table filter strip with date range + status + site dropdowns. Apply to every list page.
3. **Centralised formatters** (½ day) — `MoneyFormatter`, `DateTimeFormatter` (Africa/Accra), `PhoneFormatter` (E.164 → local). `<Money>`, `<Datetime>`, `<Phone>` components.
4. **Friendly errors** (½ day) — `ExceptionTranslator` service maps exception types → user-friendly messages. `<DataAnnotationsValidator>` on every EditForm. Constraint-violation messages map to "this float already exists for that site" rather than `23505 unique violation on idx_floats_active`.
5. **Global search** (½ day) — top-nav search box that hits `/api/search?q=...`; matches voucher numbers, IRNs, vendor/customer names, journal entry IDs.
6. **Polish** (½ day) — `<title>` per page, breadcrumbs on detail pages, `<NotFound>` styled, `<Error>` styled, loading skeletons, keyboard shortcuts (Ctrl+/ for search, n for new, j/k for list nav), badge on Approvals nav link with pending count.

### Track 2B — Top features by ROI (3–4 days)

From the feature-backlog ranking, picking items where (a) backend already
exists and only UI is missing, OR (b) value/effort ratio is exceptional:

1. **Period-close workflow page** (1 day) — drives `IPeriodService.SoftCloseAsync` / `HardCloseAsync` from a checklist UI: bank rec status, AP/AR open count, depreciation posted, FX revaluation posted, TB balanced.
2. **Manual journal entry UI** (1.5 days) — new `IJournalService` + `<JournalEntryEditor>` for accountants posting adjusting entries. Multi-line, debit/credit, account picker, period picker, approval gate via `PostJournal` policy.
3. **Site-level P&L report** (½ day) — `site_id` already on every ledger line. Add a site dropdown to the existing P&L report; one query method.
4. **Cash-count UI** (½ day) — `ICashCountService` ships; just needs a `<DailyCashCount>` page hitting it.
5. **Approval delegation UI** (½ day) — `Delegation.cs` ships; needs a calendar / "going on leave" picker that creates a delegation row.
6. **Excel export on every list** (½ day) — `<ExportButton>` reading from the same query as the page, streaming an XLSX.

Bigger items deferred to a phase 3 (call it phase 3 = next iteration):
- **PDF rendering (QuestPDF)** — 12-18 days; covers tax invoices, WHT certs, customer statements, year-end WHT books. Substantial enough to be its own phase.
- **Multi-currency FX revaluation** — 15-20 days; new `FxRate` table, BoG rate provider, `IFxRevaluationService`. Hits the kernel's current single-currency invariant — needs careful surgery.
- **GRA VAT V1 form generator** — depends on PDF rendering.
- **ICUMS scan-volume revenue** — depends on a CFO/COO call on the revenue model (per-declaration vs per-truck vs hybrid).

---

## Decision points for sensei

These are blocking calls only sensei can make. I'll proceed without them
where possible, but answers tighten the plan.

1. **P0 now or wait for phase 1?** Recommend P0 today — 3-4 hours, removes the worst-case "WebApp RCE = full DB takeover" exposure.
2. **Smoke-test cleanup** — move smoke runner to `tenant_id = 999_999` and one-time delete the 3 existing rows? Recommend yes.
3. **Multi-tenant** — tackle now during auth work (EF query filters cost ½ day) or defer? Recommend now; cheap insurance.
4. **Theme palette** — indigo `#4F46E5` (recommended) or blue `#1d4ed8`?
5. **MudBlazor for icons** in Finance? (Recommended yes for consistency.)
6. **Logo asset** — keep Portal's gradient-box pattern, or commission a real SVG? Recommend gradient-box for v1.
7. **Period-close cadence** — monthly or quarterly?
8. **WHT certificate distribution** — per-payment PDF, monthly summary, or year-end book?
9. **Phase 3 ordering** — PDF rendering before FX revaluation, or FX first?

---

## How I'll execute

- TodoWrite tracks every phase + sub-task.
- Each P0 fix lands in a single tool turn with verification.
- Phase 1A (auth) and 1B (theme) run as parallel agents in worktrees / zones.
- Phase 2A (streamline) and 2B (features) likewise.
- Each phase ends with: build green, smoke test green, deployed, verified
  via `/metrics` and a real CF Access login flow.
- DEFERRED.md keeps the running list of "this is left."

The four reports (`theme-audit.md`, `security-audit.md`,
`feature-backlog.md`, `ux-punch-list.md`) stay on disk as the source of
truth for the plan.
