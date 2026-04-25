# NickERP — Platform Roadmap

> Working document. Check boxes as items ship. Cross-link to related decision
> docs (`SSO.md`, `PLATFORM.md`, `CHANGELOG.md`). Keep tasks concrete —
> include file paths, project names, and commands.

> **Restructured 2026-04-25** from a phased v1-retrofit shape to a three-track
> platform-first shape. Track A builds `NickERP.Platform.*` standalone. Track B
> builds v2 modules on top. Track C keeps v1 running and opportunistically tidies
> it. See [§2 — Strategy](#2-strategy--platform-first-three-tracks) for the why.

---

## 0. Where we are — April 2026

### Apps in the suite

| Name | Role | Port | Runtime | Data store | Status |
|---|---|---|---|---|---|
| **NickERP.Portal** | Landing page + live stats | 5400 | .NET 10, Blazor Server | reads HR (Postgres) + NSCIM API | 🟢 Live (v1) |
| **NickHR WebApp** | People, payroll, attendance | 5310 / 5311 | .NET 10, Blazor Server | Postgres `nickhr` | 🟢 Live |
| **NickHR API** | Mobile/service REST for HR | 5215 | .NET 10 | Postgres `nickhr` | 🟢 Live |
| **NSCIM WebApp** (`NickScanWebApp.New`) | Scan portal UI | 5299 / 5300 | .NET 10, Blazor | calls NSCIM API | 🟢 Live (v1) |
| **NSCIM API** | Container/image/ICUMS service | 5205 / 5206 | .NET 10 | Postgres `nickscan_production`, `nickscan_icums`, `nickscan_downloads` | 🟢 Live (v1) |
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
- [x] **v2 architecture committed** (2026-04-24) — vendor-neutral, location-federated, plugin-driven; design at `C:\Shared\ERP V2\docs\ARCHITECTURE.md`
- [x] **v2 repo seeded** (2026-04-25) — `github.com/bjforson/ERP-V2` (private), commits `f50bf94` + `68493a6`; folder `C:\Shared\ERP V2\`

### What's still not right

Ranked by pain / blast radius. Each gap is addressed by Track A, B, or C below.

| # | Gap | Category | Addressed by |
|---|---|---|---|
| 1 | Two user stores — same person is a different record in NickHR and NSCIM | Identity | A.2 + C.2 |
| 2 | App-level logins still present behind Access (2–3 logins per session) | Identity | A.2 + C.2 |
| 3 | 34 `[AllowAnonymous]` endpoints on NSCIM API | Security | C.1 (v1 hygiene) |
| 4 | No rate limit / lockout on `/api/auth/login` | Security | C.1 (v1 hygiene) |
| 5 | No centralized logging — debugging means grep'ing six places | Operations | A.1 + C.1 |
| 6 | Manual `Y:\ → robocopy` deployment; no CI | Operations | A.0 (CI) + C.1 |
| 7 | No automated backup policy for three Postgres databases | Operations | C.1 (v1 ops) |
| 8 | Single-server SPOF (TEST-SERVER down = everything down) | Resilience | C.1 (HA) |
| 9 | Sub-apps have no shared nav / app switcher | UX | A.6 |
| 10 | Portal search box is decorative | UX | B.2 (Portal v2) |
| 11 | Notifications bell in portal is decorative; no cross-app notification pipe | UX | A.5 + B.2 |
| 12 | Can't cross-join HR + NSCIM data (top analysts by container count, etc.) | Data | A.5 + B.2 |
| 13 | No unified audit log (customs compliance concern) | Compliance | A.5 |
| 14 | Tenancy only half-wired (`NickERP.Platform.Tenancy` unfinished) | Platform | A.3 |
| 15 | No distributed tracing | Observability | A.1 |
| 16 | Mobile retired, "responsive web" as substitute — unvalidated for field operators | Mobile | parking lot until field operators complain |
| 17 | Image load latency — large scans read & base64-encoded per request; dashboards cold-start | Performance | C.1.27 (v1 pre-rendering, scheduled) + B.1 (v2 image pipeline, baked in from B.1.1) |
| 18 | Test coverage ~15%; NickHR has zero tests | Quality | C.1 (NickHR tests) |
| 19 | 53 TODOs across 13 files (top: AccessReview 6, ComprehensiveDashboard 6, scanner stubs 10, bulk ops 3, refresh-token DB, real ICUMS API call) | Code debt | C.1 triage |
| 20 | `MasterOrchestrator` ~30% ready, 6 months stale | Platform | C.1 (revive-or-delete) |
| 21 | 14 background workers; Phase 2 consolidation designed not shipped (25–33% memory/connection reduction pending) | Performance | C.1 (orchestrator consolidation) |
| 22 | `NickscanERP.sln` doesn't reference NSCIS or NickHR projects | DX | C.1 |
| 23 | No finance module (GL, AR, AP, banking, tax) | Finance | B.3 (NickFinance suite) |
| 24 | Scan → invoice manual; DSO loses days | Revenue | B.3 (6.2 scan-to-invoice automation) |
| 25 | Statutory tax filings (VAT, WHT, SSNIT, PAYE) hand-assembled monthly | Compliance | B.3 (6.5 tax engine) |
| 26 | No cash-position visibility across 6 sites | Finance | B.3 (6.4 banking + multi-site cash) |

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

## 2. Strategy — platform-first, three tracks

### Why this shape

The earlier roadmap interleaved platform work with v1 retrofit (Phase 1 = "build Identity AND swap NickHR auth over to it"). That couples two risky activities — designing platform contracts and rewriring production. A bug in either taints the other.

After 2026-04-25 review, restructured to:

- **Track A — Platform (~3–4 months).** Build `NickERP.Platform.*` as a standalone, module-less library suite. No retrofit to v1. Each layer validated by a throwaway demo app. Time-boxed.
- **Track B — v2 modules (months 5+).** Inspection v2, Portal v2, NickFinance — built clean on the finished platform. No stub-then-retrofit hacks.
- **Track C — v1 lifecycle (continuous, opportunistic).** v1 keeps shipping under its own cadence. Retrofits happen only where (a) v1 pain genuinely justifies, or (b) v1 must consume a new platform piece for SSO/audit coherence before its module's v2 cuts over.

### What gets rebuilt vs. adapted vs. left alone

| Module | Treatment | Why |
|---|---|---|
| **NickERP.Platform.\*** | **Built ground-up** in Track A | Doesn't exist; foundation for everything else |
| **NSCIM (Inspection)** | **Rebuilt ground-up** as v2 in Track B.1 | Vendor-entangled, flat-world, plugin-impossible; structurally broken |
| **NickERP.Portal** | **Rebuilt ground-up** as v2 in Track B.2 | Small (≤2-week effort); cleanest place to land new chrome + canonical Identity. v1 Portal sunsets. |
| **NickFinance suite** | **Built ground-up** in Track B.3 | Doesn't exist; gaps #23–#26 |
| **NickHR** | **Adapted, not rebuilt** | Feature-complete, zero TODOs, ships real payroll. Internals untouched. Adapter shims (Track C.2) consume new Identity / Audit / Comms. |
| **NickComms Gateway** | **Adapted, not rebuilt** | Solid, file-based outbox by design. Same treatment — adapter shims for the new audit log + canonical user identity. |

Rebuilding HR or Comms identically on the new platform delivers zero customer-visible value and burns 4–6 months. Adaptation gets the same SSO/audit coherence at ~1 week of shim code per module.

### Timeline shape

```
Month:    1  2  3  4  5  6  7  8  9  10 11 12 13 14 15 16 17 18+
                                                                 
Track A:  [============================]                         
            obs─identity─tenancy─plugins─audit─chrome              
                                                                 
Track B:                              [B.1 Inspection v2 ==============]
                                            [B.2 Portal v2 ==]    
                                                  [B.3 NickFinance ============>]
                                                                 
Track C:  [opportunistic v1 hygiene + adapter shims throughout ============>]
                                                  [C.3 v1 inspection cutover ==]
                                                                 
v1 NSCIM: [keeps shipping >>>>>>>>>>>>>>>>>>>>>>>][retiring][gone]
v1 HR:    [keeps shipping forever, adapted to new platform >>>>>>>>>>>>>>>>>>>>]
v1 Comms: [keeps shipping forever, adapted to new platform >>>>>>>>>>>>>>>>>>>>]
```

### Cross-track rules of engagement

- **No v1 code changes during Track A.** That keeps Track A bounded and prevents platform contracts from being shaped under retrofit pressure. Exceptions only for genuine production fires.
- **Track B can't start a module until its prerequisite Track A layers are accepted.** B.1 Inspection v2 needs A.1–A.5. B.2 Portal v2 needs A.2 + A.6. B.3 NickFinance 6.1 needs A.2 + A.3 + A.5.
- **Track C is opportunistic.** No required schedule. Items move to "do now" only when v1 pain or Track-B integration demands it.
- **Track A demo apps are throwaway.** They exist to exercise platform contracts during development. Once Track B starts, they get deleted; real modules become the validation.

### Risks of platform-first (and mitigations)

| Risk | Mitigation |
|---|---|
| Platform built without modules → over-engineered or wrong shape | Per-layer demo app must exercise every contract. Time-box layers hard (A.2 Identity = 4 weeks max; ship what's done if it slips). |
| Visible improvements to v1 users delayed | Track C runs in parallel; opportunistic v1 fixes (Seq logging on v1, key rotation, NSCIM security audit) ship whenever cheap. |
| Engineer burnout doing 3–4 months of "invisible" platform work | Demo app per layer creates visible artifacts. Each layer has its own ship moment. Track C variety (different kind of work) provides relief. |
| Platform contract revision after first real consumer (B.1) finds a gap | Expected. A.1–A.6 acceptance is "demo app green," not "perfect API." First B.1 month will surface revisions; budget 1–2 weeks of platform fixup during B.1. |
| Confusion about which module gets rebuilt vs. adapted | This roadmap is the single answer. Decision log records each call. |

---

## 3. Track A — Platform (~3–4 months, ~11–15 weeks)

**Goal:** ship `NickERP.Platform.*` as a coherent, documented, demo-validated library suite that v2 modules consume without further negotiation. No production code modified during this track.

### A.0 Approach

- **Repo location:** `C:\Shared\NSCIM_PRODUCTION\platform\` continues to host platform code (parts already exist, e.g. `Platform.Tenancy`, `Platform.Web.Shared`). New layers added alongside.
- **CI from day 1:** `.github/workflows/platform-ci.yml` — restore, build, test, schema-drift check on every push. Becomes the template for v2 module CI later.
- **Demo apps:** one tiny app per layer at `platform/demos/<layer>/`. Throwaway. Exercises every contract before declaring layer "done."
- **Documentation:** every layer ships with `<layer>.md` covering contracts, examples, and gotchas. Doc completeness is acceptance criterion, not afterthought.
- **No v1 code touched.** Period.

### A.1 Observability foundation (~1 week)

| Task | File / project | Status |
|---|---|---|
| A.1.1 Install Seq self-hosted at `C:\Program Files\Seq` | server | ☐ |
| A.1.2 Define `NickERP.Platform.Logging` Serilog conventions package — sinks (Seq, file fallback), enrichers (correlation, tenant, user) | `platform/NickERP.Platform.Logging/` | ☐ |
| A.1.3 OpenTelemetry SDK package — `NickERP.Platform.Telemetry` with tracing + metrics conventions | `platform/NickERP.Platform.Telemetry/` | ☐ |
| A.1.4 Demo: `platform/demos/observability/` — minimal API that emits structured logs + traces; verify Seq/Tempo show them | demo | ☐ |
| A.1.5 Document log levels, span naming conventions, metric naming conventions | `PLATFORM.md` → "Observability" section | ☐ |

**Acceptance:** Seq shows 24h of demo-app logs filtered by app + correlation; OTel trace from demo HTTP call → DB query visible end-to-end.

### A.2 NickERP.Platform.Identity (~3–4 weeks)

| Task | File / project | Status |
|---|---|---|
| A.2.1 Create `NickERP.Platform.Identity` project under `platform/` | `platform/NickERP.Platform.Identity/` | ☐ |
| A.2.2 Define canonical domain model: `IdentityUser`, `AppScope`, `UserScope`, `ServiceTokenIdentity` | `Platform.Identity/Entities/*.cs` | ☐ |
| A.2.3 Migration: new schema `identity` in `nickerp_platform` Postgres DB (separate DB so platform isn't coupled to v1 NSCIM DB) | `Platform.Identity/Migrations/` | ☐ |
| A.2.4 `CfAccessJwtMiddleware` — validates `CF-Access-Jwt-Assertion` against CF JWKS (`https://<team>.cloudflareaccess.com/cdn-cgi/access/certs`) | `Platform.Identity/Auth/` | ☐ |
| A.2.5 `IIdentityResolver` service — maps email → canonical `IdentityUser`; handles new-user-on-first-login | `Platform.Identity/Services/` | ☐ |
| A.2.6 Dev-mode bypass: if `ASPNETCORE_ENVIRONMENT=Development`, accept fake JWT header (`X-Dev-User: dev@nickscan.com`) | same | ☐ |
| A.2.7 Service tokens — `IServiceTokenAuthenticator` for machine callers (CF Access Service Tokens) | same | ☐ |
| A.2.8 Identity admin REST API: `POST /api/identity/users`, `PATCH /api/identity/users/{id}/scopes`, `DELETE /api/identity/users/{id}` (soft) | `Platform.Identity/Api/` | ☐ |
| A.2.9 Demo: `platform/demos/identity/` — Blazor app behind CF Access; shows canonical user + scopes; admin-creates-user round-trip works | demo | ☐ |
| A.2.10 Document the Identity contract — JWT shape, scope model, dev-bypass, migration patterns from "legacy users tables" (consumed by Track C.2) | `platform/NickERP.Platform.Identity/IDENTITY.md` | ☐ |

**Acceptance:** demo app proves CF JWT → canonical user → scoped DB query in under 10 ms p95. Dev bypass works without internet. Service token round-trip works. Doc passes "could a new engineer wire a new app without asking" test.

**Defers to Track C.2:** wiring v1 NickHR / NSCIM auth to consume `IIdentityResolver` is C.2 work, not A.2. A.2 ships when the platform contract is ready; C.2 runs whenever it makes sense to retrofit a v1 app.

### A.3 NickERP.Platform.Tenancy (~2 weeks)

| Task | File / project | Status |
|---|---|---|
| A.3.1 Finish existing `NickERP.Platform.Tenancy` (already has `TenantOwnedEntityInterceptor`) — extend with session-variable middleware that sets `app.current_tenant_id` per request | `platform/NickERP.Platform.Tenancy/` | ☐ |
| A.3.2 Postgres RLS policy templates — generators for "tenant-isolated" and "tenant + scope-isolated" tables | `Platform.Tenancy/Rls/` | ☐ |
| A.3.3 `Tenant` entity + `TenantBootstrap` script (seeds a new tenant world) | `Platform.Tenancy/Entities/` + `scripts/tenant-bootstrap.sql` | ☐ |
| A.3.4 Tenant admin REST API: create/suspend/list tenants | `Platform.Tenancy/Api/` | ☐ |
| A.3.5 Demo: `platform/demos/tenancy/` — two-tenant data; query as tenant A returns zero of tenant B's rows even with malicious WHERE | demo | ☐ |
| A.3.6 Document the Tenancy contract | `platform/NickERP.Platform.Tenancy/TENANCY.md` | ☐ |

**Acceptance:** RLS verified by integration test (cross-tenant query returns zero); tenant bootstrap creates an empty world in <30 seconds.

### A.4 NickERP.Platform.Plugins (~1–2 weeks)

| Task | File / project | Status |
|---|---|---|
| A.4.1 Define `IPlugin` marker + `PluginManifest` JSON-Schema format | `platform/NickERP.Platform.Plugins/` | ☐ |
| A.4.2 `PluginLoader` — scans `plugins/` folder, loads assemblies, finds `[Plugin]` types via reflection, registers in DI keyed by `TypeCode` | same | ☐ |
| A.4.3 `IPluginConfigValidator` — validates plugin-instance config against the manifest's JSON Schema before startup | same | ☐ |
| A.4.4 Reference mock plugin to prove the loader works end-to-end | `platform/demos/plugins/MockEcho/` | ☐ |
| A.4.5 Plugin authoring guide | `platform/NickERP.Platform.Plugins/PLUGIN-AUTHORING.md` | ☐ |

**Acceptance:** drop a new plugin DLL + manifest into `plugins/`; restart; new entry appears in admin UI; instance can be configured and used. Removing the DLL gracefully unloads on next restart.

### A.5 NickERP.Platform.Audit & Events (~2–3 weeks)

| Task | File / project | Status |
|---|---|---|
| A.5.1 `DomainEvent` record + `audit.events` append-only table — `event_id`, `tenant_id`, `actor_user_id`, `correlation_id`, `event_type`, `entity_type`, `entity_id`, `payload jsonb`, `occurred_at`, `idempotency_key`, `prev_event_hash` | `platform/NickERP.Platform.Audit/` | ☐ |
| A.5.2 `REVOKE UPDATE, DELETE` on `audit.events` for app role — append-only at the DB level | migration | ☐ |
| A.5.3 Outbox pattern — write event in same transaction as state change; projector `BackgroundService` consumes | `Platform.Audit/Outbox/` | ☐ |
| A.5.4 Postgres `LISTEN/NOTIFY` event bus for in-process consumers (no Kafka) | `platform/NickERP.Platform.Events/` | ☐ |
| A.5.5 Idempotency-key helpers (deterministic key from input bytes + scope) | same | ☐ |
| A.5.6 Notifications inbox table (`notifications.inbox`) — keyed on canonical user, drained by Comms gateway when delivery channel chosen | `Platform.Audit/Notifications/` | ☐ |
| A.5.7 Demo: `platform/demos/audit/` — three apps emit events, projector consumes, notifications inbox shows up across them | demo | ☐ |
| A.5.8 Documentation | `platform/NickERP.Platform.Audit/AUDIT.md` | ☐ |

**Acceptance:** demo proves event from app A reaches subscriber in app B in <1 s; `audit.events` cannot be UPDATEd or DELETEd by app role (test fails on attempt); `prev_event_hash` chain verifiable.

### A.6 NickERP.Platform.Web.Shared (~2–3 weeks)

| Task | File / project | Status |
|---|---|---|
| A.6.1 Branding tokens — primary/accent palette, typography, spacing — CSS custom properties in shared library | `platform/NickERP.Platform.Web.Shared/wwwroot/tokens.css` | ☐ |
| A.6.2 `TopNav` Razor component — brand, search, notifications, user menu, app switcher | `Platform.Web.Shared/Components/TopNav.razor` | ☐ |
| A.6.3 Search hook contracts — `ISearchProvider`, federated-search aggregator | `Platform.Web.Shared/Search/` | ☐ |
| A.6.4 Notification bell — pulls from notifications inbox API; SignalR upgrade path | `Platform.Web.Shared/Components/NotificationBell.razor` | ☐ |
| A.6.5 User-menu component — initials avatar, name, scopes, sign-out (CF Access logout) | `Platform.Web.Shared/Components/UserMenu.razor` | ☐ |
| A.6.6 Deep-link helpers — `IDeepLinkResolver` for cross-app routing | `Platform.Web.Shared/DeepLink/` | ☐ |
| A.6.7 Demo: `platform/demos/chrome/` — three "fake apps" all rendering the same TopNav with consistent branding, app switcher hops cleanly | demo | ☐ |
| A.6.8 Documentation | `platform/NickERP.Platform.Web.Shared/WEB-SHARED.md` | ☐ |

**Acceptance:** TopNav identical pixel-for-pixel across the three demo apps; switching apps = one click, no chrome flash; notification bell reflects audit/notification events from A.5 within 30 s.

### A.7 Track A acceptance — platform v1.0 ships when…

- All six layers (A.1–A.6) have green demo apps + complete docs.
- A new engineer reading only `PLATFORM.md` + per-layer `*.md` files can wire a new app to the platform without asking questions.
- CI on every layer green; first migration on a clean Postgres takes < 60 s.
- A "fresh tenant" can be bootstrapped end-to-end (tenant + first user + first scope) via API in < 30 s.
- Cross-tenant RLS verified by integration test.

---

## 4. Track B — v2 modules (months 5+)

**Gating rule:** a Track B module cannot start until its prerequisite Track A layers are accepted (§A.7).

### B.1 NickERP.Inspection v2 (federated rebuild) — first Track B module

**Goal:** vendor-neutral, location-federated, plugin-driven rebuild of the NSCIM scan / analysis / authority-submission pipeline. Runs in parallel with v1 NSCIM until Location-by-Location cutover (coordinated with Track C.3). Addresses gaps #1, #14, #17, #19, #20, #21, #22.

**Repo:** standalone at `C:\Shared\ERP V2\` (sibling repo, not subfolder of NSCIM_PRODUCTION). Initialised 2026-04-25 with commits `f50bf94` (docs seed) + `68493a6` (phase renumber). Remote: `github.com/bjforson/ERP-V2` (private).

**Design of record:** `C:\Shared\ERP V2\docs\ARCHITECTURE.md`. This ROADMAP entry is the index; that doc is the detail.

**Guiding decisions** (2026-04-24, locked):

- D1 Online-first; edge-for-backup designed in but built in B.1.6.
- D2 Blazor Server primary web (WASM / native deferred for edge).
- D3 Central Postgres, RLS-enforced tenant + location isolation.
- D4 Multi-tenant from day 1.
- D5 Months-timeline, phase-gated, no hard deadline.
- D6 In-house plugins only.
- D7 Consumes shared `NickERP.Platform.*` (Identity, Tenancy, Plugins, Audit, Web.Shared) — **no stubs** (this is the platform-first dividend).
- D8 External system bindings can be per-Location or shared — chosen at onboarding per instance.

**Nomenclature change** (critical — see `ARCHITECTURE.md §3`):

Vendor words (FS6000, ICUMS, BOE, CMR, regime) leave core domain entirely. Core speaks `InspectionCase`, `ScannerDeviceType`, `ExternalSystemInstance`, `AuthorityDocument`, `Finding`, `Verdict`. Vendor-specific and country-specific concepts live in plugin adapters (`Scanners.FS6000`, `ExternalSystems.IcumsGh`, `Authorities.CustomsGh`).

**Sub-phases:**

| Sub-phase | Target | Scope | Status |
|---|---|---|---|
| B.1.-1 **Repo seed** | done 2026-04-25 | Folder, docs, git init | ✅ |
| B.1.0 Skeleton | 2 weeks | Projects, RLS bootstrap (using A.3), event log wiring (using A.5), plugin loader (using A.4), empty admin CRUD UI (using A.6) — **smaller than originally scoped because Track A delivered most plumbing** | ☐ |
| B.1.1 Single-location single-scanner happy path | 5–7 weeks | Tema, FS6000 plugin, ICUMS-GH external-system plugin, `Authorities.CustomsGh` rules provider, full case lifecycle, analyst review UI (viewer ported from v1), **image pre-rendering baked in from line 1** — vendor-neutral pipeline modelled on v1 design (`glimmering-sparking-valiant.md`), thumbnails 256 px + previews 1024 px served via ETag/`Cache-Control`, Redis + disk tiers, queue-driven workers. **No base64 image marshalling, ever.** | ☐ |
| B.1.2 Multi-scanner same location | 2–3 weeks | ASE plugin, multiple Stations at Tema, second adapter exercises the pre-rendering pipeline shipped in B.1.1 | ☐ |
| B.1.3 Multi-location | 3–4 weeks | Kotoka onboarding, per-Location + shared `ExternalSystemBinding` (D8) | ☐ |
| B.1.4 External system breadth | 2–3 weeks | 2nd authority type, polymorphic documents | ☐ |
| B.1.5 Migration tooling + parallel run | 3–4 weeks + parallel-run duration | Dual-report scanners, cutover register, deep-link forwarder; coordinates with Track C.3 | ☐ |
| B.1.6 Edge node | 4–6 weeks (post-cutover) | Offline-capable per-Location node, sync protocol | ☐ |

**Cross-dependencies:**
- Needs Track A.1, A.2, A.3, A.4, A.5 accepted before B.1.0 starts. A.6 lands during B.1.0–B.1.1 (not blocking).
- Coordinates with **Track C.3** on per-Location cutover sequence.

**Rules of engagement:**
- v1 (`src/NickScanCentralImagingPortal.*`) is **not modified** by Track B work. v1 fixes, when needed, happen via Track C.
- v2 **does not reference** v1 code. Ports are point-in-time copies with rename.
- Every task in B.1.X → check box in `ARCHITECTURE.md §8` AND here.

### B.2 NickERP.Portal v2 (~2–4 weeks)

**Goal:** rebuild the suite landing page on the new platform. Drops v1 Portal; the public hostname `erp.nickscan.net` cuts over.

**Prerequisites:** A.2 (Identity), A.5 (Audit/Events for notifications inbox), A.6 (Web.Shared TopNav).

| Task | File | Status |
|---|---|---|
| B.2.1 New project `NickERP.Portal.v2` consuming `Platform.Web.Shared` | `platform/NickERP.Portal.v2/` | ☐ |
| B.2.2 Live-stats panel — pulls HR (Postgres direct) + Inspection v2 (HTTP); no NSCIM v1 dependency by the time this ships | same | ☐ |
| B.2.3 Federated search — `ISearchProvider` implementations for HR (employees), Inspection v2 (cases), eventually Finance (invoices, vouchers) | same | ☐ |
| B.2.4 Notification bell wired to A.5 inbox | same | ☐ |
| B.2.5 Deep-link cards — route to specific sub-app screens | same | ☐ |
| B.2.6 Cross-app reporting dashboard — canned reports driven by FDW views (Track C.1.6) | `Pages/Reports.razor` | ☐ |
| B.2.7 Audit-log search UI — "Who accessed case X at time Y" against `audit.events` | `Pages/Audit.razor` | ☐ |
| B.2.8 Health rollup — green/red strip for each service, links to Seq | `Pages/Health.razor` | ☐ |
| B.2.9 Cutover: switch `erp.nickscan.net` from v1 Portal to v2 Portal; retire v1 Portal Windows Service | ops | ☐ |

**Acceptance:** typing `ANGELA` returns employee + cases-as-operator; notification sent via Comms appears in bell within 30 s; deep-link to `/inspection/case/abc123` lands directly.

### B.3 NickFinance suite (quarters)

**Goal:** make NickERP the system of record for the business's money, not just its operations. Full plan in [`docs/modules/NICKFINANCE_PLATFORM.md`](docs/modules/NICKFINANCE_PLATFORM.md). Ships one module at a time; each independently useful.

**Strategy:** **hybrid** — Tally Prime Gold (or QuickBooks) stays the GL of record + statutory filing engine for year 1; we build operational modules natively on the platform and sync journals nightly. Year 2 optionally cuts Tally over once auditor trust is earned.

**Prerequisites:** Track A complete. B.1.0–B.1.1 deliver an Inspection v2 events stream that B.3.2 (scan-to-invoice) consumes — so B.3 starts in earnest after B.1.1 ships.

| Module | Window | Status |
|---|---|---|
| B.3.1 Petty Cash — see [`docs/modules/PETTY_CASH.md`](docs/modules/PETTY_CASH.md) — 14 weeks; pathfinder for `NickFinance.Core` + `Ledger` + `Money` type + Hubtel MoMo extension | ~Q3 2026 (relative to Track A completion) | ☐ |
| B.3.2 Accounts Receivable + **scan-to-invoice automation** (Inspection v2 verdict → GRA e-VAT invoice → MoMo pay link, no humans) — 8–10 weeks | +1 quarter | ☐ |
| B.3.3 Accounts Payable + OCR + **WhatsApp payment-run approval** + auto-WHT — 8–10 weeks | +1 quarter | ☐ |
| B.3.4 Banking & reconciliation + **multi-site live cash position** — 6–8 weeks | +1 quarter | ☐ |
| B.3.5 Tax engine (GRA e-VAT via partner, iTaPS schedules, WHT certificates, e-SSNIT) — largely shipped within B.3.1–B.3.3 | parallel | ☐ |
| B.3.6 Fixed Assets — 4 weeks; **optional year 1**, Tally adequate at current volume | year 2 | ☐ |
| B.3.7 Financial Reporting (P&L, BS, CF, drill-down; Tally remains statutory) — 6–8 weeks | year 2 | ☐ |
| B.3.8 Budgeting & Forecasting + rolling 13-week cash forecast — 4–6 weeks | year 2 | ☐ |
| B.3.9 GL sync to Tally/QBO (nightly journal export) — 2–3 weeks | early year 2 | ☐ |
| B.3.10 (optional, year 2) Cut Tally over; NickFinance becomes GL of record — needs ICAG audit sign-off + GRA e-VAT direct certification | year 2+ | ☐ |

**De-risk spikes before B.3.1 kickoff** (≤1 week each, run in parallel):
1. Tally XML import round-trip with a hand-crafted journal
2. GRA e-VAT certified-partner discovery (Blue Skies / Persol / Hubtel) — docs + pricing + sandbox invoice
3. `NickFinance.Ledger` kernel proof — posted-event insert + balance invariant + period lock + 1,000 random journals under property tests

**Budget:** ~$62K year 1 + ~$58K year 2 engineering + Tally Gold + Azure Form Recognizer + GRA e-VAT partner + NIA verification. (Compare: NetSuite $150–400K y1, Sage X3 $100–250K y1, full in-house no-Tally $250–400K y1.)

**Acceptance (for B.3.1 + B.3.2 together):** scan completed at Tema → e-VAT invoice issued → customer WhatsApp'd a pay link → customer pays MTN MoMo → AR closed — zero human touches between scan and closed invoice.

**Risks:** see full register in `NICKFINANCE_PLATFORM.md §8`.

### B.4 What does NOT get rebuilt

**NickHR** and **NickComms Gateway** stay on v1 indefinitely. They are feature-complete, debt-free, and ship real value daily. Rewriting them identically on the new platform would burn months for zero customer-visible benefit.

They consume new platform pieces via thin adapter shims (Track C.2):
- HR auth swaps to `IIdentityResolver` (via shim)
- HR + Comms emit events to the new `audit.events` table
- HR + Comms appear in the new federated search and notification inbox

If, after Track A + B.1 + B.2 ship, there's specific evidence that HR or Comms internals are blocking us, we revisit. Until then, leave them alone.

---

## 5. Track C — v1 lifecycle (continuous, opportunistic)

**Goal:** v1 keeps running and shipping value. Retrofits, hygiene fixes, and adapter shims happen when (a) v1 pain genuinely justifies cost, or (b) Track B integration demands a v1 module consume new platform pieces.

**Track C is the "v1 keeps your business running" track.** No rigid schedule. Pull items from this list when capacity allows or pain forces.

### C.1 v1 hygiene — security, ops, code debt

| Task | Where | Trigger |
|---|---|---|
| C.1.1 Audit the 34 `[AllowAnonymous]` endpoints — 23 need `[Authorize]`, 7 need IP-allowlist, 4 legit. Ship in batches of 5–10 behind a feature flag. | `src/NickScanCentralImagingPortal.API/Controllers/` | security review pressure |
| C.1.2 Rate limit on `/api/auth/login` and `/api/auth/refresh` (5/min/IP, 20/hour/IP) | `src/NickScanCentralImagingPortal.API/Program.cs` | security review |
| C.1.3 Enable Identity lockout in NickHR (`MaxFailedAccessAttempts=5`, `DefaultLockoutTimeSpan=15m`) | `NickHR/src/NickHR.Infrastructure/InfrastructureExtensions.cs` | security review |
| C.1.4 Rotate `nickhr-key-change-me-in-production` NickComms keys → real 32-byte; update env + DB hash | manual + SQL | always-on; ship now |
| C.1.5 Rotate the CF API token used during Access setup | CF dashboard | always-on; ship now |
| C.1.6 Postgres FDW (`postgres_fdw`) so `nickscan_production` reads `nickhr` tables for cross-app joins | SQL on server | needed by B.2.6 reporting |
| C.1.7 Canonical dimension tables in `identity` schema (`dim_user`, `dim_location`, `dim_date`); views joined against each app's facts | `scripts/dw/dim-*.sql` | needed by B.2.6 |
| C.1.8 Reporting DB `nickscan_reporting` with materialized views | DB | needed by B.2.6 |
| C.1.9 Postgres backup cron — `pg_dump` all DBs nightly to `C:\Shared\Backups\pg\YYYY-MM-DD\`, retain 14 days | Task Scheduler + `scripts/pg-backup.ps1` | always-on; ship now |
| C.1.10 Document RPO 24h / RTO 2h | `PLATFORM.md` → "Backup & DR" section | with C.1.9 |
| C.1.11 NickHR adapter shim → consume A.2 Identity | `NickHR/src/NickHR.Infrastructure/Auth/` | C.2 |
| C.1.12 NSCIM v1 adapter shim → consume A.2 Identity (NSCIM kept on auth-shim until B.1.5 cutover) | `src/NickScanCentralImagingPortal.API/Auth/` | C.2 / B.1.5 |
| C.1.13 NickComms adapter shim → emit events to A.5 audit log | `services/NickComms.Gateway/` | C.2 |
| C.1.14 Background services consolidation Phase 2.2 — `IcumPipelineOrchestratorService` (5 ICUMS workers → 1) | `Services/Orchestrators/` | v1 perf pain pre-cutover |
| C.1.15 Background services consolidation Phase 2.3 — `ContainerCompletenessOrchestratorService` (4 workers → 1) | same | after C.1.14 |
| C.1.16 Comment out old service registrations once orchestrators proven in staging | `Services/ServiceConfiguration.cs` | after C.1.15 |
| C.1.17 Code-debt triage — top 3 clusters from gap #19: refresh-token DB storage, real ICUMS API call, AccessReview suite | various | code-review pressure |
| C.1.18 Add NickHR test project — payroll calculation + self-service password-reset flow | `NickHR/src/NickHR.Tests/` | always-on; pickup as time allows |
| C.1.19 Decide `MasterOrchestrator` fate — revive or delete | `src/NickScanCentralImagingPortal.MasterOrchestrator/` | always-on |
| C.1.20 Solution-file cleanup — add NSCIS + NickHR to `NickscanERP.sln` or document the per-module-sln convention in `PLATFORM.md` | `NickscanERP.sln` | always-on |
| C.1.21 Mobile smoke test — 14 routes covering 8 responsive pages on real iOS/Android | QA pass | once before B.1.5 cutover |
| C.1.22 Flag-flip schedule — `PortAssignmentRule` + `FycoExportRule` (D+7 / D+10 cadence) | Ops | always-on |
| C.1.23 Post-deploy ingestion verification — 4 SQL queries from `DEFERRED_ACTIONS.md` | Ops | with each deploy |
| C.1.24 ICUMS outreach email — 324 IM declarations missing `ManifestDetails.DeliveryPlace` | upstream | always-on; ship now |
| C.1.25 Push to `origin/main` — 4 local commits ahead | always-on | ship now |
| C.1.26 Delete stale `claude/festive-tharp` GitHub branch on retired `NICKSCAN-CENTRAL--IMAGE-PORTAL` repo | git | ship now |
| C.1.27 **v1 Pre-rendering service** per [`.../glimmering-sparking-valiant.md`](../../../Users/Administrator/.claude/plans/glimmering-sparking-valiant.md) — full design exists (all four scanners day 1, Redis ON, 50 GB disk LRU, ImageSharp). **Scheduled, not optional** — v1 serves un-cutover Locations through the entire parallel-run window; at 2000 images/day per Location, base64-per-request is unworkable. Ship before whichever comes first: (a) any Location's throughput projects past **1500 images/day**, or (b) parallel-run window opens (B.1.5). | `src/NickScanCentralImagingPortal.*` | scale-triggered |

### C.2 Adapter shims — v1 consumes new platform pieces

The shim approach: v1 modules stay on their own internals; thin shims translate v1 patterns to the new platform contracts.

| Shim | What it does | Trigger |
|---|---|---|
| C.2.1 NickHR ↔ Identity | NickHR's `ApplicationUser` becomes a profile record keyed on canonical `IdentityUser`. NickHR auth path validates CF JWT via shim, resolves to canonical user, looks up profile. Login form removed; `/forgot-password` retained as emergency. | After A.2 accepted; can land any time |
| C.2.2 NickHR ↔ Audit | NickHR emits selected events (clock-in, payroll-run, leave-approved) to `audit.events` via shim. | After A.5 accepted |
| C.2.3 NickComms ↔ Audit | NickComms emits delivery events (sent, delivered, bounced) to `audit.events` and to notification inbox. | After A.5 accepted |
| C.2.4 NickComms ↔ Identity | NickComms recipient lookup uses canonical user → preferred channel. | After A.2 accepted |
| C.2.5 NSCIM v1 ↔ Identity | NSCIM v1 swaps `AuthenticationController` PBKDF2 login for CF JWT validation via shim. WebApp drops login form. | Strongly recommended pre-B.1.5 cutover so v1/v2 share auth during parallel run |
| C.2.6 NSCIM v1 ↔ Audit | v1 emits selected events to `audit.events` so B.2 audit-log UI shows v1 + v2 history continuously. | Optional pre-cutover |

### C.3 v1 inspection cutover (coordinated with B.1.5)

| Task | Status |
|---|---|
| C.3.1 Per-Location cutover schedule — Tema first (most scans), then Kotoka, etc. | ☐ |
| C.3.2 Dual-report scanners during parallel run — both v1 and v2 receive each scan; idempotency keys dedupe | ☐ |
| C.3.3 Cutover register — which cases already submitted to ICUMS by v1 (so v2 doesn't double-submit) | ☐ |
| C.3.4 In-flight case handoff — cases AwaitingReview/UnderReview at moment T finish in v1; new cases in v2 | ☐ |
| C.3.5 Deep-link forwarder — v1 URLs (`/container/{n}/details`) redirect to v2 (`/cases/by-subject/{n}`) for 90 days | ☐ |
| C.3.6 v1 NSCIM read-only mode per-Location after cutover; full retire after 90 days of v2 stability | ☐ |

### C.4 v1 retirement criteria

v1 NSCIM retires when:
- All Locations cut over to v2.
- 90 days of v2 stability with no rollback.
- Historical case access retained via v2 read-only history view (back-loaded from v1).
- Deep-link forwarder retired.

NickHR and NickComms **do not retire** — they stay v1 indefinitely, consumed via adapter shims.

---

## 6. Quick wins (low-effort, high-payoff — pick off between deeper work)

Grouped by track. Items move into the track-proper when they grow beyond a quick win.

### Quick wins for Track A
- [ ] **Serilog file sink** in services that don't have it — at minimum `C:\Shared\Logs\<service>\YYYY-MM-DD.log` — useful even before Seq lands
- [ ] **App switcher dropdown** in NickHR + NSCIM top nav (v1) — pre-A.6 stopgap; decoupled from full TopNav rollout
- [ ] **Backup policy v0** — `pg_dump` all three DBs to an external drive nightly. 20 lines of PowerShell. Pre-C.1.9.

### Quick wins for Track B
- [ ] **"Deferred actions" page in portal v2** (admin users) — lists known issues and live `ROADMAP.md` checkbox state

### Quick wins for Track C (v1)
- [ ] **Portal health strip shows real data** (v1 Portal) — ping each service every 30s, green/red dot
- [ ] **Portal search wired to HR + NSCIM minimal queries** — even without full Identity, a read-only search across both DBs is ~1 day
- [ ] **Rotate the `nickhr-key-change-me-in-production` NickComms key** — 32-byte random; env + DB hash
- [ ] **Remove NickScanWebApp.New Blazor cert hardcoded password** from `publish/WebApp/appsettings.json`
- [ ] **Delete NSPORTAL's cloudflared service** (if 7 days stable on TEST-SERVER cutover)
- [ ] **Push to `origin/main`** — 4 local commits ahead

### Quick wins shipped recently
- [x] ~~**Push v2 repo to GitHub**~~ — done 2026-04-25: `github.com/bjforson/ERP-V2` (private)
- [x] ~~**Decide v2 folder name**~~ — done 2026-04-25: keeping `C:\Shared\ERP V2\` with the space

---

## 7. Decision log

Living list. Append; don't rewrite.

| Date | Decision | Rationale | Doc |
|---|---|---|---|
| 2026-04-21 | Cloudflare Access uses **Email OTP** (not external IdP) for v1 | Free, zero setup, covers <50 users | `SSO.md` |
| 2026-04-21 | Access policy = `email_domain: nickscan.com` (no geo restriction) | Remote ops access; avoid lockouts on Starlink / travel | Access dashboard |
| 2026-04-22 | Portal goes live at `erp.nickscan.net` as the suite entry point | One canonical entry, room to grow | `platform/NickERP.Portal/` |
| 2026-04-22 | SSO strategy: **Option A now, migrate to Option D** (CF-Access-Jwt-Assertion) | Ship fast, defer the heavy lift until 3rd app joins or users complain | `SSO.md` |
| 2026-04-22 | CMR→IM implicit upgrade shipped: CMR-typed messages with non-empty `RegimeCode` auto-upgrade to IM declaration | Closes the 998 half-state CMR class; applies to all import regimes (40/70/90), NOT regime 80 which stays CMR | commit `bfd4d61`, `IcumJsonIngestionService.cs:600+` |
| 2026-04-23 | Stats sourced direct-to-Postgres for HR, via HTTP API for NSCIM | HR Postgres is local + cheap; NSCIM API already exists and is trusted | `platform/NickERP.Portal/Services/StatsService.cs` |
| 2026-04-23 | Pre-rendering service scope: **all four scanners day 1**, Redis ON, 50 GB disk LRU | Broad MVP — avoids a second rollout wave; Redis re-enabled as a natural part of the cache tier | `.claude/plans/glimmering-sparking-valiant.md` |
| 2026-04-23 | ImageSharp chosen over SkiaSharp / Magick.NET for pre-rendering | Already referenced; fully managed (no native-lib deploy friction); TIFF first-class for FS6000 LPR | same plan |
| 2026-04-24 | All services upgraded to **.NET 10** with `RollForward=LatestMajor` | Stay current; survive future SDK installs | all `*.csproj` |
| 2026-04-24 | NSCIM migrated from SQL Server to **Postgres** | Same cluster as HR — unblocks FDW-based cross-DB joins | `publish/API/appsettings.json` |
| 2026-04-24 | All literal secrets replaced with `***USE_ENV_VAR_X***` placeholders | Git-safe; rotation = env-var change only | all `appsettings.json` |
| 2026-04-24 | NSCIM v2 architecture committed: vendor-neutral domain, location-federated, plugin-driven, Blazor Server primary, central Postgres + RLS, multi-tenant from day 1, in-house plugins only | Removes the 4 worst structural issues in v1 in one move; greenfield isolates risk from production v1 | `C:\Shared\ERP V2\docs\ARCHITECTURE.md` §2 |
| 2026-04-25 | v2 lives in its own git repo at `C:\Shared\ERP V2\` — not a subfolder of NSCIM_PRODUCTION | Independent history, independent remote, independent release cadence | initial commit `f50bf94` |
| 2026-04-25 | v2 GitHub remote: `github.com/bjforson/ERP-V2` (private). Local folder retains the space. | Match v1's account and visibility model; local space accepted as a permanent papercut | `git remote -v` in v2 repo |
| 2026-04-25 | **ROADMAP restructured to platform-first three-track shape** (Track A platform standalone, Track B v2 modules on top, Track C v1 lifecycle) | Decouples platform-design risk from v1-retrofit risk; SSO works coherently from line 1 of Track B because no module is built against stub contracts; v1 keeps shipping under its own cadence | this file |
| 2026-04-25 | NickHR and NickComms are **adapted, not rebuilt** — stay v1 indefinitely, consume new platform via thin shims (Track C.2) | Both are feature-complete and debt-free; rewriting identically on new platform = months for zero customer-visible value | this file §2 + §B.4 |
| 2026-04-25 | Inspection v2 = Track B.1 (was "Phase 7"). NickFinance = Track B.3 (was "Phase 6"). Phase numbers retired in favour of Track A.x / B.x / C.x | Phase numbering became confused after restructure (Phase 5 was placed after Phase 6 in document order); track shape is clearer | this file |
| 2026-04-25 | **Pre-rendering is mandatory, not optional** — both v1 (Track C.1.27, scale-triggered before parallel-run cutover) and v2 (baked into B.1.1 from line 1) | At 2000 images/day per Location, base64-per-request fails. v1 will serve un-cutover Locations through the parallel-run window — easily long enough to hit that throughput. v2 must not repeat v1's flat image-pipeline mistake. | this file + `glimmering-sparking-valiant.md` + `ARCHITECTURE.md` (to update) |

---

## 8. Parking lot — intentionally not yet

Things explicitly deferred. When one fires, move to a track.

- **Second tenant / multi-customer rollout** — A.3 makes it cheap; exercise when 1st sale is imminent.
- **Real mobile app** — retired in favour of responsive web. Revisit if field operators complain.
- ~~**Redis cache** — configured but disabled.~~ Promoted into Track C.1.27 (v1 pre-rendering, optional) and into Track B.1.2 (v2 image pipeline, mandatory).
- **Fleet / Customs modules** — placeholders on the portal. Design-ready when business prioritises.
- **External IdP federation** (Google Workspace / M365 / SAML) — A.2 supports it; turn on when above 50 users or a customer demands.
- **AI workflow / Claude integration** — scaffolding landed in v1 API `AiWorkflow` config. Lives in its own track parallel to A/B/C.

---

## 9. How we're tracking

- **This file is the source of truth** for platform-level plans. Edit freely.
- Track-sized work → one git branch per track-and-layer (e.g. `track-a/identity`, `track-b/inspection-v2/skeleton`).
- Task-sized work → one PR per task; reference the track+task code in the title (e.g. `feat(platform): A.2.4 CfAccessJwtMiddleware`).
- Each completed task → check the box here, add a line to `CHANGELOG.md`.
- **Don't check a box until the acceptance criteria for its layer are met.**

### Related docs in NSCIM_PRODUCTION
- `CHANGELOG.md` — what actually shipped
- `PLATFORM.md` — architectural contract; grows with each Track A layer
- `DEFERRED_ACTIONS.md` — operational follow-ups from each deploy
- `platform/NickERP.Portal/SSO.md` — Option-A-now-Option-D-later rationale
- `BACKGROUND_SERVICES_OPTIMIZATION_STATUS.md` — backs C.1.14–C.1.16
- `IMPLEMENTATION_STATUS_UPDATE.md` — Phase 2.1 `ImageAnalysisOrchestratorService` v1 status

### External plan files
- `C:\Users\Administrator\.claude\plans\glimmering-sparking-valiant.md` — full design for v1 pre-rendering (now optional Track C.1.27); detailed data model, interfaces, phased delivery, verification plan, risks. Kept as reference even if v1 pre-rendering is never built.

### v2 repo (`C:\Shared\ERP V2\`, sibling repo)
- `C:\Shared\ERP V2\README.md` — entry point.
- `C:\Shared\ERP V2\docs\ARCHITECTURE.md` — design of record for Track B.1. Domain vocabulary, entity model, plugin contracts, cross-cutting concerns, sub-phase plan.
- `C:\Shared\ERP V2\docs\MIGRATION-FROM-V1.md` — cutover plan (stub; grows through B.1.5).
- Remote: `github.com/bjforson/ERP-V2` (private).

---

## 10. First concrete next move

If we resume and pick one thing, start with **Track A.1.1**: install Seq self-hosted on TEST-SERVER. That's the foundation every later layer logs into. Forty minutes of work, immediate value, zero risk.

Rough commands:

```powershell
# On TEST-SERVER as Administrator
choco install seq -y
# Default port 5341, admin UI at http://localhost:5341
# Configure retention to 14 days, set admin password
```

Then **A.1.2** — start the `NickERP.Platform.Logging` package:

```bash
cd /c/Shared/NSCIM_PRODUCTION
dotnet new classlib -n NickERP.Platform.Logging -o platform/NickERP.Platform.Logging -f net10.0
dotnet sln NickscanERP.sln add platform/NickERP.Platform.Logging/NickERP.Platform.Logging.csproj --solution-folder platform
git add platform/NickERP.Platform.Logging NickscanERP.sln
git commit -m "chore(platform): seed NickERP.Platform.Logging (ROADMAP A.1.2)"
```

Then A.1.3 (OpenTelemetry conventions), A.1.4 (demo app), A.1.5 (docs) — and Track A.1 ships in a week. Each subsequent layer follows the same pattern.

The first Track B work can't start until A.1–A.5 are accepted (~3 months). Use that window to also pick off Track C quick wins where they're cheap and obviously useful (key rotation, backup cron, file-sink logging on v1).
