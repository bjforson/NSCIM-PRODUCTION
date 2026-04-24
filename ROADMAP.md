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
- [x] **CMR→IM implicit upgrade handler** — CMR-typed messages carrying a `RegimeCode` auto-upgrade to IM declarations (`IcumJsonIngestionService.cs:600+`, shipped `bfd4d61`, 2026-04-22; fixes 998 half-state rows)
- [x] **ICUMS ingestion integrity + full-field visibility** (v2.15.2) — warning columns populated, 324 BOEs flagged for upstream DP outreach
- [x] **Viewer arc Phases 1–5 complete** — server-side W/L sliders, client-side 16-bit viewer, pixel-probe hover, ROI inspector side panel
- [x] **Zombie analysis-group sweeper** (v2.15.1) — archives stuck `AnalystCompleted` groups
- [x] **FS6000 partial-channel rendering** (v2.14.0) + ASE tri-panel default through composite renderer
- [x] **Ingest hardening** — reject truncated `.img` at ingest time (v2.14.1)
- [x] **NickComms API key hardcoding fixed** in `NickHR.WebApp` (`89e077f`, 2026-04-21)

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
| 17 | Image load latency — large scans (2–30 MB TIFF/JPEG) read & base64-encoded per request; dashboards cold-start | Performance |
| 18 | Test coverage ~15% — 7 test files for 24 production projects; NickHR has zero tests despite being feature-complete | Quality |
| 19 | 53 TODO markers across 13 files — no unified triage (top clusters: `AccessReviewController` 6, `ComprehensiveDashboardService` 6, Heimann/Nuctech stubs 10, bulk validation ops 3, refresh-token DB storage, real ICUMS API call) | Code debt |
| 20 | `MasterOrchestrator` scaffold ~30% ready, not deployed, last touched 2025-10-12 (6 months stale) | Platform |
| 21 | Background services still 14 separate workers; Phase 2 consolidation designed but not shipped (25–33% memory/connection reduction pending) | Performance |
| 22 | `NickscanERP.sln` does not reference NSCIS or NickHR projects — each module has its own `.sln` (intentional per `PLATFORM.md` but confusing for newcomers) | DX |
| 23 | No finance module (GL, AR, AP, banking, tax) — business runs on spreadsheets + standalone Tally/QBO outside NickERP | Finance |
| 24 | Scan → invoice is manual; DSO loses days per scan because humans key in what ICUMS already knows | Revenue |
| 25 | Statutory tax filings (VAT, WHT, SSNIT, PAYE) assembled by hand monthly — no schedule generation from system of record | Compliance |
| 26 | No cash-position visibility across the 6 sites; daily treasury is a WhatsApp + phone-call exercise | Finance |

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
| 2.13 **Background services consolidation — Phase 2.2**: `IcumPipelineOrchestratorService` (consolidates 5 ICUMS workers into 1) | `src/NickScanCentralImagingPortal.Services/Orchestrators/` | 🔄 designed |
| 2.14 **Background services consolidation — Phase 2.3**: `ContainerCompletenessOrchestratorService` (consolidates 4 workers into 1) | same | ⏳ blocked on 2.13 |
| 2.15 Comment out old service registrations once orchestrators proven in staging (Phase 2.1 `ImageAnalysisOrchestratorService` is coded + marked fallback) | `Services/ServiceConfiguration.cs` | ☐ |
| 2.16 **Code-debt triage** — triage the 53 open TODOs into: ship-now, Phase-X, won't-do. Land top 3 clusters: (a) `JwtService.cs:281` refresh-token DB storage (security), (b) `ICUMSSubmissionService.cs:124` real ICUMS API call (replaces stub), (c) `AccessReviewController.cs` 6 TODOs — review status/date tracking, CSV export, access revocation | various | ☐ |
| 2.17 **Add test project for NickHR** — module is feature-complete but has zero tests. Seed with payroll calculation + self-service password-reset flow. | `NickHR/src/NickHR.Tests/` | ☐ |
| 2.18 **Decide MasterOrchestrator fate** — either revive (test against current topology, deploy as Windows service) or delete the 3-file scaffold. Currently 6 months stale, ~30% ready, not in `publish\`. | `src/NickScanCentralImagingPortal.MasterOrchestrator/` | ☐ |
| 2.19 Add NSCIS + NickHR projects to `NickscanERP.sln` (or document the per-module-sln convention in `PLATFORM.md` so newcomers aren't confused) | `NickscanERP.sln` | ☐ |

**Acceptance criteria:**
- Open Seq, filter by app, see last hour of logs from every service
- Push to `main` → new build lands on TEST-SERVER within 5 minutes
- `scripts/rollback.ps1` reverts any service to the previous published build in under 30 seconds
- `pg_dump` backups for yesterday exist and can be restored to a sandbox DB
- `curl -X POST /api/auth/login` 10× in 60s → 429 Too Many Requests

---

### Phase 2.5 — Performance & pre-rendering (4–6 weeks, runs parallel with Phase 2/3)

**Goal:** analysts stop waiting. Sub-50 ms thumbnails, sub-80 ms previews, dashboards that never cold-start. Full design at [`.claude/plans/glimmering-sparking-valiant.md`](../../../Users/Administrator/.claude/plans/glimmering-sparking-valiant.md) — mirrored below as a checklist. User-confirmed scope: **all four scanners day 1, Redis ON, 50 GB disk LRU**.

| Task | File / project | Status |
|---|---|---|
| 2.5.1 New entities `PrerenderedAsset` + `ImagePrerenderQueueItem` | `src/NickScanCentralImagingPortal.Core/Entities/` | ☐ |
| 2.5.2 EF migration `AddPrerenderingTables` (2 tables, 4+ indexes) + 2 DbSets on `ApplicationDbContext` | `src/NickScanCentralImagingPortal.Infrastructure/Migrations/` | ☐ |
| 2.5.3 Core interfaces: `IPrerenderDispatcher`, `IPrerenderedAssetStore`, `IImageRenderer`, `IDatasetPrewarmer` | `src/NickScanCentralImagingPortal.Core/Interfaces/` | ☐ |
| 2.5.4 `ImageSharpRenderer` (singleton) — 256 px thumb, 1024 px preview; TIFF/JPEG/PNG/BMP | `Services/Prerendering/ImageSharpRenderer.cs` | ☐ |
| 2.5.5 `PrerenderDispatcher` + `PrerenderedAssetStore` — SQL queue + `Channel<long>` pump, Redis (< 512 KB) / disk routing | `Services/Prerendering/` | ☐ |
| 2.5.6 `ImagePreRenderingBackgroundService` — N workers, exponential retry, lease-based dequeue (multi-instance safe) | same | ☐ |
| 2.5.7 Ingestion hooks in **all four scanner services** (FS6000, ASE, Nuctech, HeimannSmith) — fire-and-forget enqueue | `Services.FS6000/IngestionService.cs`, `Services/ASE/AseDatabaseSyncService.cs`, `ScannerServices.Nuctech/…`, `ScannerServices.HeimannSmith/…` | ☐ |
| 2.5.8 `PrerenderController` — GET assets (ETag + `Cache-Control: immutable`), GET status, POST trigger, admin queue/invalidate/warm | `API/Controllers/PrerenderController.cs` | ☐ |
| 2.5.9 Flip Redis `Enabled=true` in config + `appsettings.Prerendering` block | `API/appsettings.json` | ☐ |
| 2.5.10 `PrerenderMetrics` wired to `IPerformanceMetricsService` (queue depth, render latency histogram, cache hits by layer) | `Services/Prerendering/PrerenderMetrics.cs` | ☐ |
| 2.5.11 Backfill console tool — enqueue last 14 days at priority=Low | `src/tools/PrerenderBackfill/` | ☐ |
| 2.5.12 `DatasetPrewarmer` + `DatasetPreWarmingBackgroundService` — dashboard top-pending, today-analyst-summary, image-analysis-ops, worklist-by-user | same | ☐ |
| 2.5.13 `HybridCache` registration (L1 + L2 Redis) for dataset tier only — never image bytes | `API/Program.cs` | ☐ |
| 2.5.14 `PrerenderSignalRNotifier` — push `AssetReady` to group `prerender.{containerNumber}` via `ComprehensiveDashboardHub` | same | ☐ |
| 2.5.15 Frontend: `PrerenderClient`, updated `ContainerViewPreloader`, `<img src>` swap with `data-fallback`, `prerenderFallback.js` | `NickScanWebApp.New/Services/` + container pages | ☐ |
| 2.5.16 `<link rel="preload">` hints in `_Host.cshtml` for first 3 thumbs of loaded container | `NickScanWebApp.New/Pages/_Host.cshtml` | ☐ |
| 2.5.17 Tile pyramids (Phase 3 of internal plan) — `RenderTilePyramidAsync`, DZI manifest, OpenSeadragon viewer | optional / later | ☐ |
| 2.5.18 Extend `HousekeepingWorker` with disk-cache LRU eviction (50 GB cap) + stale-row cleanup | `Services/ImageAnalysis/HousekeepingWorker.cs` | ☐ |
| 2.5.19 Admin ops UI — queue depth, throughput, cache hit rate, manual invalidate/warm | `NickScanWebApp.New/Pages/Admin/PrerenderOps.razor` | ☐ |

**Feature flags (all OFF by default):**

1. `Prerendering:Enabled` — master kill-switch
2. `Prerendering:FeatureFlags:EnableFor{FS6000|ASE|Nuctech|HeimannSmith}` — per-scanner gate
3. `Prerendering:Datasets:Enabled` — dashboard prewarm tier
4. `Prerendering:UsePrerenderedImages` (webapp side) — whether Blazor actually swaps `<img src>`. Lets us populate cache for days before serving from it.

**Rollout cadence:**

- Week 1: flags 1 + 2 ON in dev + staging; backfill last 14 days at priority=Low.
- Week 2: flag 4 ON for 5-user analyst pilot.
- Week 3: flag 4 ON for all analysts.
- Week 4: flag 3 ON (dataset prewarm).

**Acceptance criteria:**

- Ingest 10-scan test folder through each of the four scanners → `PrerenderedAssets` has 20 rows per scanner within 30 s; `StorageLocation` split correctly between Redis and Disk.
- `GET /api/prerender/assets/{scanId}/thumbnail` → 200 with `ETag`; repeat with `If-None-Match` → 304.
- Served latency (warm): thumb ≤ 50 ms p95 from Redis, preview ≤ 80 ms p95 from disk (was 500+ ms baseline).
- Cache hit rate ≥ 85% after 1 hour warm operation.
- Disk LRU triggers at ~40 GB (under 50 GB cap); `prerender_disk_usage_gb` stays bounded.
- No regression in ingestion throughput — ±5% of baseline per scanner.
- Dashboard open: `prewarm_staleness_seconds` < 60 s steady state.

**Library choices (decided):**

- **ImageSharp** — already referenced in `NickScanCentralImagingPortal.Services.ImageProcessing.csproj`; fully managed, no native-deploy friction; TIFF first-class (FS6000 LPR). Rejected SkiaSharp (native libs), Magick.NET (GPL + slow).
- **HybridCache** — .NET `Microsoft.Extensions.Caching.Hybrid` for dataset tier only, stampede-protected. Image bytes stay on disk + raw Redis via existing `ICacheService`.
- **SQL-backed queue** — atomic dequeue via `UPDATE…OUTPUT…WITH (READPAST, UPDLOCK, ROWLOCK)`, lease-based for multi-instance.

**Risks (summary):**

- Disk blow-up → hard 50 GB cap + LRU. Redis memory → 512 KB routing threshold. Worker crash mid-render → lease sweeper reclaims every 5 min. Predictive floods → bounded channel 10 000, DropOldest. `[Cached]` attribute on image endpoints → explicit DO NOT — corrupts streams. Ingestion hook throw → wrapped in try/catch so ingestion never breaks.

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

### Phase 6 — NickFinance suite (quarters, not weeks)

**Goal:** make NickERP the system of record for the business's money, not just its operations. Full plan in [`docs/modules/NICKFINANCE_PLATFORM.md`](docs/modules/NICKFINANCE_PLATFORM.md). Ships one module at a time; each module is independently useful.

**Strategy:** **hybrid** — Tally Prime Gold (or QuickBooks) stays the GL of record + statutory filing engine for year 1; we build operational modules natively on top of NickERP and sync journals nightly. Year 2 optionally cuts Tally over once auditor trust is earned.

**Prerequisites:**
- Phase 1 (Identity) — hard block: approvers, requesters, custodians must be canonical users
- Phase 2 (Seq + CI + backups) — strong preference before money moves
- Phase 4 (event bus + unified audit log) — hard block on 6.2 (scan→invoice) and audit features
- Phase 5 (tenancy) — design-time block only (schemas carry `tenant_id` from day 1)

| Module | Window | Status |
|---|---|---|
| 6.1 Petty Cash — see [`docs/modules/PETTY_CASH.md`](docs/modules/PETTY_CASH.md) — 14 weeks; pathfinder for `NickFinance.Core` + `Ledger` + Money type + Hubtel MoMo extension | Q3 2026 | ☐ |
| 6.2 Accounts Receivable + **scan-to-invoice automation** (ICUMS declaration → GRA e-VAT invoice → MoMo pay link, no humans) — 8–10 weeks | Q4 2026 | ☐ |
| 6.3 Accounts Payable + OCR + **WhatsApp payment-run approval** + auto-WHT — 8–10 weeks | Q1 2027 | ☐ |
| 6.4 Banking & reconciliation (CSV per Ghana bank + Hubtel MoMo webhook auto-match) + **multi-site live cash position** — 6–8 weeks | Q2 2027 | ☐ |
| 6.5 Tax engine (GRA e-VAT via partner, iTaPS schedules, WHT certificates, e-SSNIT) — 4–6 weeks; largely shipped as part of 6.1–6.3 | Q2 2027 | ☐ |
| 6.6 Fixed Assets — 4 weeks; **optional year 1**, Tally adequate at current volume | Q4 2027 | ☐ |
| 6.7 Financial Reporting (P&L, BS, CF, drill-down; Tally remains statutory) — 6–8 weeks | Q3 2027 | ☐ |
| 6.8 Budgeting & Forecasting + rolling 13-week cash forecast — 4–6 weeks | Q3 2027 | ☐ |
| 6.9 GL sync to Tally/QBO (nightly journal export) — 2–3 weeks | Q2 2027 | ☐ |
| 6.10 (optional, year 2) Cut Tally over; NickFinance becomes GL of record — needs ICAG audit sign-off + GRA e-VAT direct certification | 2028 | ☐ |

**De-risk spikes before kickoff** (≤1 week each, run in parallel):
1. Tally XML import round-trip with a hand-crafted journal
2. GRA e-VAT certified-partner discovery (Blue Skies / Persol / Hubtel) — docs + pricing + sandbox invoice
3. `NickFinance.Ledger` kernel proof — posted-event insert + balance invariant + period lock + 1,000 random journals under property tests

**Budget:** ~$62K year 1 + ~$58K year 2 engineering (2 devs + part-time CPA reviewer at Ghana rates) + Tally Gold + Azure Form Recognizer + GRA e-VAT partner + NIA verification. Compare: NetSuite $150–400K y1, Sage X3 $100–250K y1, full in-house no-Tally $250–400K y1.

**Acceptance (for 6.1 + 6.2 together, end of Q4 2026):**
- Scan completed at Tema → e-VAT invoice issued → customer WhatsApp'd a pay link → customer pays MTN MoMo → AR closed — zero human touches between scan and closed invoice
- End-of-month VAT return generates as a CSV iTaPS accepts without edits
- Petty cash GA at all 6 sites; paper vouchers retired
- Tally journals reconcile with NickFinance postings within ±GHS 0.10 daily

**Risks:** See full register in `NICKFINANCE_PLATFORM.md §8`. Top three: GRA e-VAT partner onboarding slip (blocks 6.2), ledger-kernel invariant bug (mitigated by property tests + parallel-run vs Tally for a month), auditor rejection of NickFinance journals (mitigated by hybrid approach — Tally stays authoritative year 1).

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
- [ ] **Delete stale `claude/festive-tharp` GitHub branch** on retired `NICKSCAN-CENTRAL--IMAGE-PORTAL` repo (per `DEFERRED_ACTIONS.md` 2026-04-22)
- [ ] **Mobile smoke test** — walk 14 routes covering the 8 responsive pages on real iOS/Android (portrait + tablet landscape); log visual issues as GitHub issues. 87% of pages have responsive grid markers; the remaining 13% are unvalidated.
- [ ] **Flag-flip schedule** — `PortAssignmentRule` + `FycoExportRule` both OFF in `appsettings.json`; recommended cadence D+7 / D+10 with daily health monitoring D+1..D+7.
- [ ] **Post-deploy ingestion verification** — run the 4 SQL queries from `DEFERRED_ACTIONS.md` to confirm `ingestionlogs` populating + warning columns firing + categories tracked.
- [ ] **ICUMS outreach email** — 324 real IM declarations lack `ManifestDetails.DeliveryPlace` in the source feed. Send the template email to ICUMS contacts to either fill the field or confirm lifecycle timing.

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
| 2026-04-22 | CMR→IM implicit upgrade shipped: CMR-typed messages with non-empty `RegimeCode` auto-upgrade to IM declaration | Closes the 998 half-state CMR class; applies to all import regimes (40/70/90), NOT regime 80 which stays CMR | commit `bfd4d61`, `IcumJsonIngestionService.cs:600+` |
| 2026-04-23 | Pre-rendering service scope: **all four scanners day 1**, Redis ON, 50 GB disk LRU | Broad MVP — avoids a second rollout wave; Redis re-enabled as a natural part of the cache tier; 50 GB matches ~2 weeks of previews at current volume | `.claude/plans/glimmering-sparking-valiant.md` |
| 2026-04-23 | ImageSharp chosen over SkiaSharp / Magick.NET for pre-rendering | Already referenced; fully managed (no native-lib deploy friction on Windows Server 2022); TIFF support is first-class for FS6000 LPR format | same plan |

---

## 5. Parking lot — intentionally not yet

Things explicitly deferred. When one fires, move to a phase.

- **Second tenant / multi-customer rollout** — parked until 1st sale is imminent. Tenancy groundwork exists but isn't exercised.
- **Real mobile app** — retired in favour of responsive web. Revisit if field operators complain.
- ~~**Redis cache** — configured but disabled.~~ **PROMOTED to Phase 2.5** (2026-04-23) — Redis gets turned ON as part of the pre-rendering work; used for thumbnails < 512 KB and for the HybridCache L2 dataset tier.
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
  - `DEFERRED_ACTIONS.md` — lighter-weight TODO list (operational follow-ups from each deploy)
  - `platform/NickERP.Portal/SSO.md` — Option-A-now-Option-D-later rationale
  - `BACKGROUND_SERVICES_OPTIMIZATION_STATUS.md` — Phase 1 done, Phase 2 design (backs Phase 2 tasks 2.13–2.15 here)
  - `IMPLEMENTATION_STATUS_UPDATE.md` — Phase 2.1 `ImageAnalysisOrchestratorService` status
- External plan files (outside this repo, but authoritative for scoped initiatives):
  - `C:\Users\Administrator\.claude\plans\glimmering-sparking-valiant.md` — full design for Phase 2.5 (pre-rendering service). Contains data model, interfaces, phased delivery, verification plan, risks & mitigations. **This ROADMAP is the index; that plan is the detail.**

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
