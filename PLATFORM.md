# NICKSCAN ERP SOLUTION — Platform Contract

> The single source of truth for how every module integrates with the platform.

## What is this document?

NICKSCAN ERP SOLUTION is built as a **platform + modules** architecture. This file defines the **Platform Contract** every module must implement to be a valid first-class citizen of the platform. If a module breaks this contract, it doesn't ship.

This is a **living document**. Every phase of the build updates it as new platform services come online. It is intentionally short and prescriptive — not a tutorial, not a marketing page.

---

## Vocabulary

| Term | Meaning |
|---|---|
| **Platform** | The shared infrastructure layer that every module sits on top of. Identity, tenancy, comms, files, audit, notifications, workflow, jobs, reporting. |
| **Module** | A self-contained business application with its own database, API, and UI. NSCIS, NickHR, Finance, Procurement, DMS, CRM are modules. |
| **Gateway** | A standalone Windows/HTTP service that the platform exposes to all modules. NickComms, NickFiles, NickAudit, NickNotify, NickWorkflow are gateways. |
| **Tenant** | An isolated instance of the platform. Nick TC-Scan Operations is tenant 1. Every other customer would be tenant 2, 3, … |
| **Platform contract** | This document. The set of requirements every module must satisfy. |

---

## Architecture overview

```
                            NICKSCAN ERP SOLUTION
                            ─────────────────────

   ┌───────────────────────────  PLATFORM LAYER  ───────────────────────────┐
   │                                                                        │
   │   Identity     Tenant     Audit     Notify   Files   Workflow  Jobs    │
   │   (Auth)      (Multi-T)   (Trail)   (Inbox)  (Blob)  (Engine)  (Sched) │
   │                                                                        │
   │              Comms (email/SMS/OTP — already built)                     │
   │              Reporting (read-replica + dashboards)                     │
   │              Org/Lookup service (cross-module)                         │
   │                                                                        │
   └────────────────────────────────────────────────────────────────────────┘
                                     │
        ┌──────────┬─────────┬───────┴───────┬─────────┬──────────┐
        │          │         │               │         │          │
   ┌────▼────┐ ┌──▼───┐ ┌───▼───┐  ┌────────▼┐ ┌──────▼─┐ ┌──────▼──┐
   │  NSCIS  │ │NickHR│ │Finance│  │Procurem.│ │  DMS   │ │   CRM   │
   └─────────┘ └──────┘ └───────┘  └─────────┘ └────────┘ └─────────┘
```

---

## Repository layout

```
NSCIM_PRODUCTION/
├── platform/                              ← shared platform libraries
│   ├── NickERP.Platform.Core/             ← contracts, DTOs, value types
│   ├── NickERP.Platform.Tenancy/          ← multi-tenancy plumbing (Phase 1)
│   ├── NickERP.Platform.Identity/         ← OIDC client helpers (Phase 2)
│   ├── NickERP.Platform.Comms.Client/     ← INickCommsClient (already in place)
│   ├── NickERP.Platform.Files/            ← INickFilesClient (Phase 3)
│   ├── NickERP.Platform.Audit/            ← INickAuditClient (Phase 4)
│   ├── NickERP.Platform.Notifications/    ← INickNotifyClient (Phase 5)
│   ├── NickERP.Platform.Workflow/         ← INickWorkflowClient (Phase 6)
│   ├── NickERP.Platform.BackgroundJobs/   ← shared job framework (Phase 7)
│   ├── NickERP.Platform.Reporting/        ← shared reporting registry (Phase 9)
│   └── NickERP.Platform.Web.Shared/       ← MudBlazor components, app launcher
│
├── services/                              ← standalone gateway services
│   ├── NickComms.Gateway/                 ← email + SMS + OTP (built)
│   ├── NickFiles.Gateway/                 ← blob storage (Phase 3)
│   ├── NickAudit.Gateway/                 ← central audit (Phase 4)
│   ├── NickNotify.Gateway/                ← notifications (Phase 5)
│   └── NickWorkflow.Gateway/              ← workflow runner (Phase 6)
│
├── modules/                               ← business modules
│   ├── nscis/                             ← NickScan Central Imaging System
│   ├── nickhr/                            ← NickHR
│   ├── finance/                           ← Phase 12
│   ├── procurement/                       ← Phase 13
│   ├── dms/                               ← Phase 14
│   └── crm/                               ← Phase 15
│
├── apps/                                  ← end-user apps
│   ├── erp-portal/                        ← unified launcher (Phase 2)
│   └── erp-mobile/                        ← PWA / Maui (Phase 16)
│
├── tools/                                 ← scripts, migration runners
│   ├── backup/
│   ├── restore/
│   └── synthetic-monitor/
│
├── docs/                                  ← architecture docs, runbooks
│   ├── architecture-image-pipeline.md     ← NSCIS image-pipeline spec
│   ├── viewer-phase-plan.md               ← NSCIS operator-viewer arc
│   ├── ops-fs6000-data-integrity.md       ← FS6000 ops runbook
│   └── plans/                             ← archived phase plans
│
├── PLATFORM.md                            ← this file
└── NickscanERP.slnx                       ← top-level solution
```

The migration to this layout is gradual. Today (Phase 0) the new directories exist alongside the existing `src/`, `NickHR/`, and `.claude/worktrees/romantic-gauss/NickComms/` paths. Over the next phases each module's source code moves under `modules/`.

---

## The Platform Contract — every module MUST

### 1. Own its data
- Every module has **its own PostgreSQL database**. No shared schemas. No cross-module foreign keys.
- Cross-module reads happen via **service APIs** (HTTP) or **read replicas** (analytics only). Never via direct SQL joins across DBs.
- Schema changes ship as **EF Core migrations** in the module's Infrastructure project. No manual SQL deployments in production except for the platform-level read replica setup scripts in `tools/`.

### 2. Be tenant-aware (Phase 1 onward)
- Every entity that holds business data implements `ITenantOwned` (a marker interface from `NickERP.Platform.Tenancy`).
- Every `DbContext` configures EF Core global query filters to scope reads by `tenant_id`.
- Every `SaveChanges` writes the current tenant id automatically via the `TenantOwnedEntityInterceptor`.
- Every database has PostgreSQL Row-Level Security (RLS) enabled as a defense-in-depth backstop.
- Modules NEVER query without a tenant in scope. Background jobs that need to span tenants (e.g. nightly backup) explicitly impersonate each tenant in turn.

### 3. Use platform identity (Phase 2 onward)
- All authentication is delegated to the central identity provider (currently NSCIS auth, later OIDC).
- Modules receive JWT bearer tokens from the platform IdP and validate them via `NickERP.Platform.Identity` middleware.
- Modules NEVER store passwords or issue their own JWTs.
- The JWT contains at minimum: `sub` (user id), `tenant_id`, `aud` (target module), `roles`, `exp`.

### 4. Speak to gateways via shared clients
A module that needs comms, files, audit, notifications, workflow, etc. **registers the corresponding shared client** in DI:

```csharp
services.AddNickERPCommsClient();        // INickCommsClient → NickComms.Gateway
services.AddNickERPFilesClient();        // INickFilesClient → NickFiles.Gateway
services.AddNickERPAuditClient();        // INickAuditClient → NickAudit.Gateway
services.AddNickERPNotifyClient();       // INickNotifyClient → NickNotify.Gateway
services.AddNickERPWorkflowClient();     // INickWorkflowClient → NickWorkflow.Gateway
services.AddNickERPTenancy();            // ITenantContext + interceptor
services.AddNickERPIdentity(o => ...);   // OIDC bearer auth
services.AddNickERPBackgroundJobs();     // hosted job runner (Phase 7)
services.AddNickERPTelemetry();          // OpenTelemetry baseline (Phase 11)
```

A module **MUST NOT** instantiate gateway clients manually, **MUST NOT** use `HttpClient` directly to talk to platform services, and **MUST NOT** read gateway secrets from `appsettings.json` (use env vars or the per-tenant settings store).

### 5. Emit audit + notification events on writes
- Any state-changing operation emits an audit event via `INickAuditClient.RecordAsync` (Phase 4 onward). The platform interceptor automatically handles `IAuditedEntity` so most modules just opt into the marker interface.
- User-relevant state changes emit a notification via `INickNotifyClient.SendToUserAsync` (Phase 5 onward).
- Failure to emit either is a **logged warning, not a hard fail**. Audit and notifications are observability concerns; they must never block business operations.

### 6. Expose a manifest endpoint
Every module's API must expose:

```
GET /api/_module/manifest
```

returning:

```json
{
  "name": "NSCIS",
  "displayName": "NickScan Central Imaging System",
  "version": "1.8.0",
  "description": "Customs cargo scanning, image analysis, ICUMS integration",
  "platformContractVersion": "1.0",
  "icon": "shield-check",
  "color": "#1d4ed8",
  "routes": {
    "home": "/dashboard",
    "settings": "/admin/settings"
  },
  "platformDependencies": [
    "comms",
    "files",
    "audit",
    "notify",
    "workflow"
  ],
  "capabilities": [
    "container.scan",
    "container.review",
    "icums.submit",
    "image.analysis"
  ],
  "health": "/health"
}
```

The unified ERP portal (Phase 2) reads this to populate the App Launcher tile grid.

### 7. Expose health endpoints
Every module and gateway MUST expose:

- `GET /health/live` — process is alive (returns 200 if the HTTP server is responding)
- `GET /health/ready` — module is ready to serve requests (DB connection works, dependent gateways reachable)
- `GET /health` — composite (returns 200 if everything passes, 503 otherwise)

Health checks register with `services.AddHealthChecks()` and use the standard ASP.NET Core health check infrastructure.

### 8. Configuration via env vars + runtime settings

Three configuration sources in priority order:

1. **Environment variables** (highest priority) — used for service-to-service secrets, connection strings, gateway URLs, API keys. Naming convention: `NICKERP_<SERVICE>_<KEY>` or legacy `NICKCOMMS_*`, `NICKHR_*`. Never put secrets in `appsettings.json`.
2. **Per-tenant runtime settings** — stored in the tenant settings DB. Edited via the admin UI. Hot-reloads without restart.
3. **`appsettings.json`** — only for non-secret defaults that everyone needs (port numbers, log levels, feature flags). NEVER for credentials, never for placeholders that look like secrets.

The bug we hit in NickHR Phase 0 (the literal `***SET_VIA_NICKCOMMS_API_KEY_NICKHR_ENV_VAR***` placeholder) is the cautionary tale. All shared clients enforce `IsUnset()` semantics that treat `***...***` patterns as null.

### 9. Run as a Windows service (production)
Every module API and gateway service runs as a Windows service in production. The pattern:

- `Program.cs` calls `builder.Host.UseWindowsService()`
- Single-instance enforced via named mutex (`Global\NickERP_<ServiceName>_SingleInstance`)
- Logs to Windows Event Log + console + file via Serilog
- Auto-start on boot (`sc create ... start= auto`)
- Health-checked by the synthetic monitor (Phase 11) every 60 seconds

### 10. Keep the trunk green
- Branches are short-lived (≤ 2 days).
- All code merges to `main` via PR or fast-forward merge.
- `main` must always build and pass the smoke test suite.
- The git hygiene incident with NickHR (full codebase living as untracked files in a worktree) is the new floor — never again.

---

## Phase status (as of latest commit)

| Phase | Wave | Title | Status |
|---|---|---|---|
| 0 | A | Branding, structure, repo cleanup | **In progress** |
| 1 | A | Multi-tenancy foundation | Pending |
| 2 | A | SSO + Unified Portal | Pending |
| 3 | B | NickFiles.Gateway | Pending |
| 4 | B | Unified audit log | Pending |
| 5 | B | Unified notification center | Pending |
| 6 | C | Workflow / Approval engine | Pending |
| 7 | C | Background jobs framework | Pending |
| 8 | D | Org / lookup service | Pending |
| 9 | D | Reporting / BI | Pending |
| 10 | E | Tenant management UI + billing | Pending |
| 11 | D | Backups, DR, observability | Pending |
| 12 | F | Finance / GL | Pending |
| 13 | F | Procurement / Inventory / Assets | Pending |
| 14 | F | DMS | Pending |
| 15 | F | CRM | Pending |
| 16 | G | Mobile / PWA | Pending |
| 17 | H | Multi-channel comms expansion | Pending |
| 18 | I | AI/ML expansion | Pending |
| 19 | J | Public-facing portals | Pending |
| 20 | K | External integrations | Pending |
| 21 | L | Productization & GTM | Pending |

The full plan with per-phase tasks, file lists, and verification steps lives in `docs/plans/wave-a-platform-foundation.md` (current) and `.claude/plans/ethereal-snuggling-kahan.md` (historical).

---

## Glossary of clients (target end state)

| Client interface | Gateway | Phase added |
|---|---|---|
| `INickCommsClient` | `NickComms.Gateway` | (already built) |
| `ITenantContext` | `NickERP.Platform.Tenancy` (in-process) | 1 |
| `INickFilesClient` | `NickFiles.Gateway` | 3 |
| `INickAuditClient` | `NickAudit.Gateway` | 4 |
| `INickNotifyClient` | `NickNotify.Gateway` | 5 |
| `INickWorkflowClient` | `NickWorkflow.Gateway` | 6 |
| `IPlatformBackgroundJob` | `NickERP.Platform.BackgroundJobs` | 7 |
| `IOrgDirectory` | `NickOrg.Gateway` | 8 |
| `IReportDefinition` | `NickERP.Platform.Reporting` | 9 |
| `IFeatureGate` | `NickERP.Platform.Tenancy` | 10 |
| `INickFinanceClient` | `Finance` module | 12 |
| `INickProcurementClient` | `Procurement` module | 13 |
| `INickDmsClient` | `DMS` module | 14 |
| `INickCrmClient` | `CRM` module | 15 |
| `IIntelligenceProvider` | `NickAI.Gateway` | 18 |
| `IPaymentProvider` | `NickPayments.Gateway` | 20 |

---

## Versioning the platform contract

This document is versioned. Every breaking change to the contract bumps the major version. Modules declare which contract version they target in their manifest endpoint (`platformContractVersion`).

| Version | Date | Change |
|---|---|---|
| 1.0 | 2026-04-06 | Initial contract — Phase 0 setup |
