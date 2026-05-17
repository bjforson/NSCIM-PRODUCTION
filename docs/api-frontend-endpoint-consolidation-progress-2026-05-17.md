# API And Frontend Endpoint Consolidation Progress

Date: 2026-05-17  
Branch/worktree used: `codex/endpoint-consolidation-batch-20260517` in `C:\Shared\NSCIM_PRODUCTION_ENDPOINT_BATCH`  
Production services touched: `NSCIM_API`, `NSCIM_WebApp`

## Current State

The endpoint-consolidation implementation is complete for the safe frontend-wiring and telemetry guardrail phases. No production routes were removed.

The latest static inventory reports:

| Metric | Current count |
| --- | ---: |
| Backend controller route actions | 909 |
| Controller files | 127 |
| Minimal API routes | 58 |
| FastAPI routes | 40 |
| WebApp `/api` callsites | 104 |
| Provider `/api` first segments | 119 |
| Consumer `/api` first segments | 69 |
| Unmatched local consumer segments | 1 |

The only unmatched consumer segment is the intentional endpoint tester placeholder:

| Segment | Location | Status |
| --- | --- | --- |
| `(empty)` from `/api/` | `src/NickScanWebApp.New/Components/Monitoring/DebugPanel.razor` | Intentional operator/debug tool. Do not count as a stale route. |

## Completed Batches

| Commit | Batch | Result |
| --- | --- | --- |
| `4a9917b` | Gateway and AI workflow client handling | Reduced direct frontend endpoint construction for gateway and AI workflow routes. |
| `5584f46` | AI workflow client through shared API service | Centralized AI workflow HTTP behavior behind the shared client wrapper. |
| `22fa0a8` | Auth bootstrap through authentication client | Routed auth profile and permission catalog calls through `AuthenticationClient`. |
| `89fc916` | Removed obsolete WebApp API service wrapper | Eliminated the duplicate WebApp-local API wrapper and kept only the shared HTTP wrapper. |
| `0ccb29f` | Endpoint usage telemetry canonicalization | Normalized endpoint usage paths for case/trailing-slash reporting without removing aliases. |
| `bcb7acf` | Route usage deprecation catalog | Moved hardcoded deprecated/phase-route checks into `EndpointRouteUsageCatalog` and exposed owner/replacement metadata. |
| `c821d86` | Signed image probe client | Replaced the one remaining ad hoc `new HttpClient()` image probe with `SignedImageProbeClient`. |

## Validation Performed

| Check | Result |
| --- | --- |
| `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore /p:UseSharedCompilation=false` | Passed, 202 existing warnings, 0 errors. |
| `dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore /p:UseSharedCompilation=false` | Passed, 263 existing warnings, 0 errors. |
| Route inventory script | Passed with only the intentional debug `/api/` placeholder unmatched. |
| WebApp health after deploy | `http://localhost:5299/health` returned 200. |
| WebApp login surface after deploy | `http://localhost:5299/login` returned 200. |
| API health after deploy | `http://localhost:5205/health`, `/health/live`, and `/health/ready` returned 200 after restart settled. |
| Protected monitoring endpoint unauthenticated smoke | `http://localhost:5205/api/Monitoring/deprecated-endpoints/summary` returned 401, confirming route is live and protected rather than missing. |
| Windows service events | Recent `NSCIM_API` and `NSCIM_WebApp` events showed clean stop/start messages, with no fresh crash events in the checked window. |

## Production Backups Created

| Batch | Backup path |
| --- | --- |
| Auth permissions client | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\webapp-auth-permissions-client-20260517-203000` |
| Auth fallback client | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\webapp-auth-fallback-client-20260517-203557` |
| Endpoint usage canonical API | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\api-endpoint-usage-canonical-20260517-204345` |
| Route usage catalog API | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\api-route-usage-catalog-20260517-205703` |
| Route usage catalog WebApp | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\webapp-route-usage-catalog-20260517-205703` |
| Signed image probe WebApp | `C:\Shared\NSCIM_PRODUCTION\deploy-backups\signed-image-probe-client-20260517-210348` |

## Consolidated Gains

- Frontend screens now depend on typed clients or explicit URL builders for normal API work rather than page-level raw endpoint strings.
- Auth and permission bootstrap calls now use a dedicated authentication client instead of a general-purpose WebApp API wrapper.
- The obsolete WebApp-local API wrapper has been removed, leaving one shared HTTP wrapper and domain-specific clients.
- Endpoint telemetry now canonicalizes path casing/trailing slashes for reporting so usage is not split between route spellings.
- Deprecated route classification now lives in `EndpointRouteUsageCatalog` with owner, reason, canonical replacement, and removal-window metadata.
- The endpoint usage monitoring UI can show canonical replacement and owner data for deprecated routes.
- The remaining signed image existence probe is now a named service that uses the configured `NickScanAPI` HTTP pipeline and redacts signed query strings from logs.

## Remaining Work

These are intentionally not done overnight because they require live telemetry or workflow-owner confirmation:

| Item | Why it remains | Recommended next step |
| --- | --- | --- |
| Remove compatibility routes | Route deletion can break operator screens, scripts, or external callers. | Wait for endpoint usage telemetry to show zero use for the configured safe-removal window, then remove one family at a time. |
| Expand `EndpointRouteUsageCatalog` beyond image legacy routes | The catalog should grow only where canonical ownership is explicit. | Add entries when a route family is formally declared compatibility or deprecated. |
| Canonical route casing changes | ASP.NET route matching is tolerant, but scripts and logs may still use old casing. | Keep typed clients stable first; introduce lower-case compatibility aliases only where telemetry shows split usage. |
| Debug panel `/api/` placeholder | It is an intentional operator tool and not a stale route. | Leave it, or exclude it explicitly in future inventory output. |
| External/service-only `/api` paths | `/api/BOEScanData`, `/api/rm/scan`, `/api/tags`, and `/api/generate` are not local NSCIM frontend routes. | Validate through their owning service/client configuration, not local route removal. |

## Next Safe Iteration

1. Keep collecting endpoint usage telemetry for deprecated and compatibility routes.
2. Add new catalog entries only when a route owner and canonical replacement are agreed.
3. Use `tools\diagnostics\Invoke-RouteCallsiteInventory.ps1` before every route behavior change.
4. Retire compatibility aliases in narrow batches, with one build/deploy/smoke cycle per route family.
