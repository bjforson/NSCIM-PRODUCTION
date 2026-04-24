# NickERP — Platform Roadmap

> Working document. Check boxes as items ship. Cross-link to related decision
> docs (`SSO.md`, `PLATFORM.md`, `CHANGELOG.md`). Keep tasks concrete —
> include file paths, project names, and commands.

---

## 0. Where we are — April 2026

### Apps in the suite

| Name | Role | Port | Runtime | Data store | Status |
|---|---|---|---|---|---|
| **NickERP.Portal** | Landing page + live stats | 5400 | .NET 10, Blazor Server | reads HR (Postgres) + NSCIM API | 🟢 Live |
| **NickHR WebApp** | People, payroll, attendance | 5310 / 5311 | .NET 10, Blazor Server | Postgres `nickhr` | 🟢 Live |
| **NickHR API** | Mobile/service REST for HR | 5215 | .NET 10 | Postgres `nickhr` | 🟢 Live |
| **NSCIM WebApp** (`NickScanWebApp.New`) | Scan portal UI | 5299 / 5300 | .NET 10, Blazor | calls NSCIM API | 🟢 Live |
| **NSCIM API** | Container/image/ICUMS service | 5205 / 5206 | .NET 10 | Postgres `nickscan_production`, `nickscan_icums`, `nickscan_downloads` | 🟢 Live |
| **NickComms Gateway** | SMTP + SMS + OTP | 5220 | .NET | Postgres `nick_comms` | 🟢 Live |

### Edge / identity today

- Cloudflare Tunnel (`nickhr`, UUID `bef442b0-...`) — single connector on TEST-SERVER
- Public hostnames: `erp`, `hr`, `lan`, `scan`, `api` — all on `nickscan.net`
- Cloudflare Access: one app (`NickScan Services`) covering all five hostnames
- Identity provider: Email OTP (free)
- Policy: include `email_domain: nickscan.com`
- Session: 24h

### Infrastructure that landed recently ✅

- [x] All services on **.NET 10** with `RollForward=LatestMajor`
- [x] Secrets moved to env-var placeholders (`***USE_ENV_VAR_X***`) — API, NickComms, Portal
- [x] Dedicated Postgres app user `nscim_app` replacing `postgres` superuser
- [x] NSCIM migrated from SQL Server to Postgres (same cluster as HR)
- [x] MailKit bumped to 4.16.0 (CVE fix: GHSA-9j88-vvj5-vhgr, GHSA-g7hc-96xr-gvvx)
- [x] Portal installed as Windows Service `NSCIM_Portal`
- [x] NuGet restore working through Starlink
- [x] CF tunnel cut over from NSPORTAL to TEST-SERVER
- [x] NickHR self-service password reset (ForgotPassword/ResetPassword razor pages)
- [x] NickHR login no longer gated on browser geolocation

### What's still not right

Ranked by pain / blast radius.

| # | Gap | Category |
|---|---|---|
| 1 | Two user stores — same person is a different record in NickHR and NSCIM | Identity |
| 2 | App-level logins still present behind Access (2–3 logins per session) | Identity |
| 3 | 34 `[AllowAnonymous]` endpoints on NSCIM API | Security |
| 4 | No rate limit / lockout on `/api/auth/login` | Security |
| 5 | No centralized logging — debugging means grep'ing six places | Operations |
| 6 | Manual `Y:\ → robocopy` deployment; no CI | Operations |
| 7 | No automated backup policy for three Postgres databases | Operations |
| 8 | Single-server SPOF (TEST-SERVER down = everything down) | Resilience |
| 9 | Sub-apps have no shared nav / app switcher | UX |
| 10 | Portal search box is decorative | UX |
| 11 | Notifications bell in portal is decorative; no cross-app notification pipe | UX |
| 12 | Can't cross-join HR + NSCIM data (e.g. top analysts by container count) | Data |
| 13 | No unified audit log (customs compliance concern) | Compliance |
| 14 | Tenancy only half-wired (`NickERP.Platform.Tenancy` unfinished) | Platform |
| 15 | No distributed tracing | Observability |
| 16 | Mobile retired, "responsive web" as substitute — unvalidated for field operators | Mobile |

---

## 1. North-star principles

Every change to the platform should move toward these:

1. **One identity.** `angela@nickscan.com` is one person, one canonical record, one set of role assignments scoped per-app.
2. **One login.** Cloudflare Access is the only login the user ever sees. Session lasts 24h across the entire suite.
3. **One nav.** Shared top bar (brand + search + notifications + user menu + app switcher) renders in every sub-app, not just the portal.
4. **One operation.** Onboarding = add user + assign scopes. Deprovisioning = one toggle.
5. **One log.** Every service logs to a central sink. Searchable across all apps.
6. **One pipeline.** Commit → CI → artifacts → deploy. No more `Y: robocopy` tribal knowledge.

---

## 2. Phase plan

Phases can overlap where noted. Each phase has a clear acceptance bar — don't declare done until it's met.

### Phase 1 — Identity unification (2–3 weeks)

**Goal:** one canonical user record, one role system, single-sign-on across sub-apps via the CF Access JWT header.

Blocks the rest of the roadmap (search, notifications, unified audit all depend on a canonical user).

| Task | File / project | Status |
|---|---|---|
| 1.1 Create `NickERP.Platform.Identity` project under `platform/` | `platform/NickERP.Platform.Identity/` | ☐ |
| 1.2 Define canonical domain model: `IdentityUser`, `AppScope`, `UserScope` | `Platform.Identity/Entities/*.cs` | ☐ |
| 1.3 Migration: new schema `identity` in `nickscan_production` Postgres DB | `Platform.Identity/Migrations/` | ☐ |
| 1.4 Backfill from existing stores (dedupe by lowercased email) | `scripts/backfill-identity.sql` | ☐ |
| 1.5 Add `CfAccessJwtMiddleware` — validates `CF-Access-Jwt-Assertion` against CF JWKS (`https://<team>.cloudflareaccess.com/cdn-cgi/access/certs`) | `Platform.Identity/Auth/` | ☐ |
| 1.6 Add `IIdentityResolver` service — maps email → canonical `IdentityUser` | `Platform.Identity/Services/` | ☐ |
| 1.7 Dev-mode bypass: if `ASPNETCORE_ENVIRONMENT=Development`, inject a fake JWT (`dev@nickscan.com`) | same | ☐ |
| 1.8 NickHR: swap `ApplicationUser` auth over to `IIdentityResolver`. Keep `ApplicationUser` as a profile record keyed on canonical user | `NickHR/src/NickHR.Infrastructure/Data/ApplicationUser.cs` | ☐ |
| 1.9 NSCIM API: swap `AuthenticationController`'s JWT issuance to accept CF Access identity instead of running its own PBKDF2 login | `src/NickScanCentralImagingPortal.API/Controllers/AuthenticationController.cs` | ☐ |
| 1.10 NSCIM WebApp: same — trust the CF JWT, drop the login form | `src/NickScanWebApp.New/` | ☐ |
| 1.11 NickHR WebApp: drop `/login` form, keep `/forgot-password` only for emergency (service token) | `NickHR/src/NickHR.WebApp/Components/Pages/Auth/` | ☐ |
| 1.12 Keep legacy login paths live during cutover — both CF JWT *and* app password work. Remove password path one release later. | | ☐ |
| 1.13 Service tokens for machine callers (mobile/future, scanner services) via CF Access Service Tokens | CF dashboard + apps | ☐ |

**Acceptance criteria:**
- New user added → single action (POST to `/api/identity/users`) creates canonical + scopes
- Same user logging into `hr.nickscan.net` and `scan.nickscan.net` gets zero login prompts from sub-apps (Access OTP is the only one)
- Deprovisioning = set `IsActive=false` on `identity.users`; access revokes within seconds across all apps
- Legacy tables (`NickHR.Users`, `NSCIM.Users`) still populated but no longer authoritative

**Risks:**
- Cutover window: both systems must read from Identity before we can switch writes. Use feature flag `UseCanonicalIdentity` per app.
- Email collision: existing stores have different emails for the same person. Dedup review needed before backfill.

---

### Phase 2 — Operational hygiene (1–2 weeks, can run parallel with Phase 1)

**Goal:** stop losing errors, stop hand-crafting deploys, stop guessing what's backed up.

| Task | Where | Status |
|---|---|---|
| 2.1 **Centralized logging** — Serilog → Seq (self-hosted, free ≤1 user) or ELK | Install Seq at `C:\Program Files\Seq`; all services add `.WriteTo.Seq("http://localhost:5341")` | ☐ |
| 2.2 Each service's Serilog block already exists — just add the sink. Start with `NickHR.API`, `NickERP.Portal`, `NSCIM API` | `Program.cs` in each | ☐ |
| 2.3 **Build CI check** — no literal secrets in committed appsettings. Pattern: any `"Password": "[^*]"` that isn't a placeholder fails the build | `.github/workflows/ci.yml` or `scripts/check-no-secrets.ps1` | ☐ |
| 2.4 **Deploy pipeline** — GitHub Actions: on push to `main`, build all `.csproj`s, archive artifacts, SSH-deploy to TEST-SERVER | `.github/workflows/deploy.yml` | ☐ |
| 2.5 **Rollback automation** — `scripts/deploy.ps1` keeps last 3 `*_backup_YYYYMMDDHHMM` dirs; `scripts/rollback.ps1` promotes previous | `scripts/deploy.ps1`, `scripts/rollback.ps1` | ☐ |
| 2.6 **Postgres backup cron** — `pg_dump` all three DBs nightly to `C:\Shared\Backups\pg\YYYY-MM-DD\`, retain 14 days | Task Scheduler + `scripts/pg-backup.ps1` | ☐ |
| 2.7 Document **RPO 24h / RTO 2h** (or whatever the real tolerance is) | `PLATFORM.md` → "Backup & DR" section | ☐ |
| 2.8 **Audit the 34 `[AllowAnonymous]` endpoints** — per prior audit in transcript, 23 need `[Authorize]`, 7 need IP-allowlist, 4 legit. Ship in batches of 5–10 endpoints behind a feature flag. | `src/NickScanCentralImagingPortal.API/Controllers/` | ☐ |
| 2.9 Add **rate limit** on `/api/auth/login` and `/api/auth/refresh` using ASP.NET Core RateLimiter (5/min/IP, 20/hour/IP) | `src/NickScanCentralImagingPortal.API/Program.cs` | ☐ |
| 2.10 Enable Identity **lockout** in NickHR (`MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15m`) | `NickHR/src/NickHR.Infrastructure/InfrastructureExtensions.cs` | ☐ |
| 2.11 Rotate the "change-me-in-production" NickComms API keys → generate new random strings, update env vars on server, update hashes in `nick_comms.api_keys` | manual + SQL | ☐ |
| 2.12 Rotate the CF API token used during Access setup if it hasn't been already | CF dashboard | ☐ |

**Acceptance criteria:**
- Open Seq, filter by app, see last hour of logs from every service
- Push to `main` → new build lands on TEST-SERVER within 5 minutes
- `scripts/rollback.ps1` reverts any service to the previous published build in under 30 seconds
- `pg_dump` backups for yesterday exist and can be restored to a sandbox DB
- `curl -X POST /api/auth/login` 10× in 60s → 429 Too Many Requests

---

### Phase 3 — Unified experience (2–3 weeks, needs Phase 1 mostly done)

**Goal:** make the suite *feel* like one product.

| Task | Where | Status |
|---|---|---|
| 3.1 **Shared chrome library** — `NickERP.Platform.Web.Shared` already exists; add a `TopNav` Razor component with brand, search, notifications, user menu, app switcher | `platform/NickERP.Platform.Web.Shared/Components/TopNav.razor` | ☐ |
| 3.2 NickHR + NSCIM + Portal all render `<TopNav />` at the top of their layout | `MainLayout.razor` in each | ☐ |
| 3.3 **Federated search API** in Portal — `GET /api/search?q=XYZ` hits HR (employees by name/email) + NSCIM (containers by number) in parallel, returns typed results | `platform/NickERP.Portal/Controllers/SearchController.cs` + `Services/Search*Provider.cs` | ☐ |
| 3.4 Wire portal search box to `/api/search` (the placeholder one in `TopNav`) — debounced, keyboard nav, "no results" state | `TopNav.razor` | ☐ |
| 3.5 **Notifications pipeline** — NickComms already has message history; extend to deliver in-app notifications via a `/api/notifications/inbox` endpoint keyed on canonical user email | `services/NickComms.Gateway/` | ☐ |
| 3.6 Notifications bell in `TopNav` pulls from `/api/notifications/inbox` every 30s (or SignalR live once Phase 4 lands) | `TopNav.razor` | ☐ |
| 3.7 **Deep linking** — portal cards accept `?to=...` params; cards route to specific sub-app screens (`/hr/employees/123` → `https://hr.nickscan.net/employees/123`) | `platform/NickERP.Portal/Components/AppCard.razor` | ☐ |
| 3.8 **Branding pass** — settle on NickERP palette across all apps. Today: NickHR purple/indigo, NSCIM cyan, Portal mixed. Pick primary + accent tokens in shared CSS variables. | `platform/NickERP.Platform.Web.Shared/wwwroot/tokens.css` | ☐ |
| 3.9 **User avatar** shows real initials from canonical user (Phase 1 dependency) | `TopNav.razor` | ☐ |

**Acceptance criteria:**
- Top bar identical on all three sub-apps; switching apps = one click in the app switcher, no page reload chrome flash
- Typing `ANGELA` in portal search shows both an employee result (HR) and any container linked to her as operator (NSCIM)
- Notification sent via NickComms to angela@nickscan.com shows in the bell badge on every sub-app within 30s
- Portal cards can deep-link to `/hr/employees/123` and `/nscim/container/XYZ` (tunnel forwards path unchanged)

---

### Phase 4 — Data unification (3–4 weeks, starts after Phase 1)

**Goal:** unlock cross-app reporting and compliance-grade audit. Both are very hard today because of two user stores and no common IDs.

| Task | Where | Status |
|---|---|---|
| 4.1 **Cross-DB join strategy** — enable Postgres FDW (`postgres_fdw`) so `nickscan_production` can read `nickhr` tables directly. All three Postgres DBs are on the same cluster already. | SQL on server | ☐ |
| 4.2 **Canonical dimension tables** in `identity` schema: `dim_user`, `dim_location`, `dim_date`. Views expose these joined against each app's fact tables. | `scripts/dw/dim-*.sql` | ☐ |
| 4.3 **Reporting DB** — optional extra Postgres DB `nickscan_reporting` with materialized views for slow reports (top analysts, payroll vs scan hours, ICUMS latency histograms) | `nickscan_reporting` | ☐ |
| 4.4 **Cross-app reporting dashboard** in Portal — "Reports" section with 5–10 canned reports driven by the above views | `platform/NickERP.Portal/Components/Pages/Reports.razor` | ☐ |
| 4.5 **Event bus** — use Postgres `LISTEN/NOTIFY` for in-process events, with an `outbox` table per app (writer adds event row, NOTIFY fires, listeners consume). No Kafka yet. | `platform/NickERP.Platform.Events/` | ☐ |
| 4.6 **First cross-app workflow** — scanner operator clocks in at NickHR → NSCIM auto-activates their operator session | Event consumer in NSCIM API | ☐ |
| 4.7 **Immutable audit log** — append-only `audit.events` table (`event_id`, `ts`, `actor_email`, `action`, `entity_type`, `entity_id`, `payload jsonb`). Every app writes here. No UPDATE/DELETE grants on the table. | `identity.audit_events` | ☐ |
| 4.8 Audit log search UI in Portal — "Who accessed container X at Y time" is a 1-second query | `Reports.razor` | ☐ |

**Acceptance criteria:**
- "Top 10 analysts by container count this month, joined against HR active-employees" is a single SQL query against the reporting views
- Audit log has ≥7 days of events covering both apps
- Clock-in event from HR visibly activates the operator's NSCIM UI within 5 seconds

---

### Phase 5 — Platform maturity (ongoing)

**Goal:** ready for a 2nd customer / 2nd site / 3rd app / real production pressure.

| Task | Where | Status |
|---|---|---|
| 5.1 **Finish tenancy** — `NickERP.Platform.Tenancy` already has TenantOwnedEntityInterceptor; ensure every entity has `tenant_id`, every query is tenant-scoped | `platform/NickERP.Platform.Tenancy/` | ☐ |
| 5.2 **Tenant bootstrap** — seeding a new tenant = one SQL script, creates empty HR/NSCIM worlds | `scripts/tenant-bootstrap.sql` | ☐ |
| 5.3 **Distributed tracing** — OpenTelemetry SDK in every service, export to Tempo or Jaeger | `Program.cs` in each | ☐ |
| 5.4 **HA** — second server with replicated Postgres (streaming replication), second CF tunnel connector. We proved the tunnel-replica pattern works earlier. | Second Windows Server | ☐ |
| 5.5 **Health rollup page** in Portal — green/red strip for each service (Cloudflared, NSCIM_*, NickHR_*, NickComms, DBs). Links to Seq for error details. | `platform/NickERP.Portal/Components/Pages/Health.razor` | ☐ |
| 5.6 **Mobile strategy resolution** — either revive `NickHR.Mobile` for field operators OR formally commit to responsive-web-as-mobile and harden it (PWA, offline, push) | spec → build | ☐ |
| 5.7 **API versioning policy** — path-based `/api/v1/...`, deprecation headers, 6-month overlap | all APIs | ☐ |

**Acceptance criteria:**
- Add tenant 2 with zero code changes
- A slow page in portal traces across Portal → NSCIM API → Postgres, visible in Jaeger
- TEST-SERVER can be rebooted and a standby takes traffic within 60 seconds

---

## 3. Quick wins (low-effort, high-payoff — can pick off between phases)

- [ ] **Portal health strip shows real data** — ping each service every 30s, green/red dot next to "All systems operational" (currently hardcoded green)
- [ ] **App switcher dropdown** in NickHR + NSCIM top nav — jump between apps without the full round-trip to portal
- [ ] **Serilog file sink** in services that don't have it — at minimum `C:\Shared\Logs\<service>\YYYY-MM-DD.log` — pre-Seq fallback
- [ ] **Portal search wired to HR + NSCIM minimal queries** — even without Phase 1 identity unification, a read-only search across both DBs is ~1 day of work
- [ ] **"Deferred actions" page in portal** (for admin users) — lists known issues and the live `ROADMAP.md` checkbox state
- [ ] **Rotate the `nickhr-key-change-me-in-production` NickComms key** — generate real 32-byte key, update env + DB hash
- [ ] **Remove NickScanWebApp.New Blazor cert hardcoded password** from `publish/WebApp/appsettings.json` (same pattern — env var placeholder)
- [ ] **Backup policy v0**: `pg_dump` all three DBs to an external drive nightly. Documented. 20 lines of PowerShell.
- [ ] **Delete NSPORTAL's cloudflared service** (if 7 days stable on TEST-SERVER cutover)
- [ ] **Push to `origin/main`** — 4 local commits ahead

---

## 4. Decision log

Living list. Append; don't rewrite.

| Date | Decision | Rationale | Doc |
|---|---|---|---|
| 2026-04-21 | Cloudflare Access uses **Email OTP** (not external IdP) for v1 | Free, zero setup, covers <50 users | `SSO.md` |
| 2026-04-21 | Access policy = `email_domain: nickscan.com` (no geo restriction) | Remote ops access; avoid lockouts on Starlink / travel | Access dashboard |
| 2026-04-22 | Portal goes live at `erp.nickscan.net` as the suite entry point | One canonical entry, room to grow | `platform/NickERP.Portal/` |
| 2026-04-22 | SSO strategy: **Option A now, migrate to Option D** (CF-Access-Jwt-Assertion) | Ship fast, defer the heavy lift until 3rd app joins or users complain | `SSO.md` |
| 2026-04-23 | Stats sourced direct-to-Postgres for HR, via HTTP API for NSCIM | HR Postgres is local + cheap; NSCIM API already exists and is trusted | `platform/NickERP.Portal/Services/StatsService.cs` |
| 2026-04-24 | All services upgraded to **.NET 10** with `RollForward=LatestMajor` | Stay current; survive future SDK installs | all `*.csproj` |
| 2026-04-24 | NSCIM migrated from SQL Server to **Postgres** | Same cluster as HR — unblocks FDW-based cross-DB joins in Phase 4 | `publish/API/appsettings.json` |
| 2026-04-24 | All literal secrets replaced with `***USE_ENV_VAR_X***` placeholders | Git-safe; rotation = env-var change only | all `appsettings.json` |

---

## 5. Parking lot — intentionally not yet

Things explicitly deferred. When one fires, move to a phase.

- **Second tenant / multi-customer rollout** — parked until 1st sale is imminent. Tenancy groundwork exists but isn't exercised.
- **Real mobile app** — retired in favour of responsive web. Revisit if field operators complain.
- **Redis cache** — configured but disabled. Enable when in-memory cache starts missing under load (not today's problem).
- **Fleet / Customs modules** — placeholders on the portal. Design-ready when business prioritises.
- **External IdP federation** (Google Workspace / M365 / SAML) — move to Phase 1.x if above 50 users or if a customer demands it.
- **AI workflow / Claude integration** — scaffolding landed in API `AiWorkflow` config. Work happens in its own track.

---

## 6. How we're tracking

- **This file is the source of truth** for platform-level plans. Edit freely.
- Phase-sized work → one git branch per phase, long-lived, merged behind a feature flag where possible.
- Task-sized work → one PR per task, reference the phase+task number in the title (e.g. `feat(identity): 1.2 canonical IdentityUser entity`).
- Each completed task → check the box here, add a line to `CHANGELOG.md`.
- **Don't check a box until the acceptance criteria for its phase are met**.
- Related files in this repo that are part of the same conversation:
  - `CHANGELOG.md` — what actually shipped
  - `PLATFORM.md` — architectural contract ("shared clients enforce IsUnset()" etc.)
  - `DEFERRED_ACTIONS.md` — lighter-weight TODO list
  - `platform/NickERP.Portal/SSO.md` — Option-A-now-Option-D-later rationale

---

## 7. First concrete next move

If we resume and pick one thing, start with **Phase 1 task 1.1**: scaffold `platform/NickERP.Platform.Identity` as an empty class library targeting .NET 10, add it to `NickscanERP.sln` under the `platform` solution folder, commit. Everything else branches off that seed.

Rough commands:

```bash
cd /c/Shared/NSCIM_PRODUCTION
dotnet new classlib -n NickERP.Platform.Identity -o platform/NickERP.Platform.Identity -f net10.0
dotnet sln NickscanERP.sln add platform/NickERP.Platform.Identity/NickERP.Platform.Identity.csproj --solution-folder platform
git add platform/NickERP.Platform.Identity NickscanERP.sln
git commit -m "chore(platform): seed NickERP.Platform.Identity project (ROADMAP 1.1)"
```

Then 1.2 (entities), 1.3 (migration), 1.4 (backfill script)…
