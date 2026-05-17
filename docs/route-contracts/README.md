# Route Contracts And Phase 0 Inventory

Date: 2026-05-16  
Scope: diagnostics and contract documentation only. No production route behavior changes.

This folder records the route categories used by the API/frontend consolidation work and the repeatable Phase 0 inventory guardrail. The inventory is static source analysis: it scans local files, prints a report, and does not call live services or write output files.

## Route Categories

### Canonical

Canonical routes are the preferred local contract for new WebApp code and typed clients. They should be owned by the domain or BFF that can safely serve the operator workflow.

Use this category when:

- the route is the intended long-term local API surface,
- the route has a clear owning controller/module,
- frontend callsites should move toward this shape,
- telemetry and docs should treat this path as the replacement for older aliases.

Examples from the current consolidation direction include `scan-assets` for source-scan image identity and module-owned BFF routes for scanner-specific workflows.

### Compatibility

Compatibility routes are intentionally preserved aliases or legacy spellings. They keep existing screens, scripts, or operators working while callsites move to canonical contracts.

Use this category when:

- deleting or renaming the route would be risky,
- the route exists to bridge old callers to a newer contract,
- runtime endpoint telemetry is needed before any retirement,
- the route should not attract new frontend callsites.

Compatibility is not a failure state. It is the safety net that lets teams migrate one domain at a time without breaking the live UI.

### External Or Service-Only

External/service-only paths are `/api` paths that are not local NSCIM WebApp controller contracts. They may be outbound service dependencies, model-provider APIs, FastAPI service routes, or tool-only backing endpoints.

The Phase 0 inventory explicitly distinguishes these known external/service-only prefixes so they are not reported as missing local NSCIM controllers:

- `/api/BOEScanData`
- `/api/rm/scan`
- `/api/tags`
- `/api/generate`

Do not fold these into local route cleanup just because they use `/api`. Validate them through their owning client/service configuration instead.

## Running The Inventory

From the active NSCIM checkout or endpoint-consolidation worktree:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-RouteCallsiteInventory.ps1
```

For source examples and first-segment tables:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-RouteCallsiteInventory.ps1 -Detailed
```

For machine-readable output:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-RouteCallsiteInventory.ps1 -AsJson
```

The script reports:

- backend ASP.NET controller routes/actions,
- C# minimal API mappings,
- FastAPI route decorators,
- WebApp literal `/api` callsites,
- provider and consumer first segments,
- known external/service-only WebApp callsites and source references,
- unmatched local consumer segments.

## Reading The Output

`Unmatched local consumer segments` means the WebApp has a literal `/api/{segment}` callsite whose first segment was not found in local provider routes scanned from controllers, minimal APIs, or FastAPI decorators, and was not in the known external/service-only list.

Treat this as a triage queue, not an automatic defect list:

- If the callsite is meant to be local, decide whether to add a canonical contract later or migrate the callsite to an existing canonical route.
- If the callsite is a compatibility alias, document the canonical replacement and check endpoint telemetry before removal.
- If the callsite is external/service-only, add it to the known external list only after confirming ownership.

## Phase 0 Guardrail

Before any API/WebApp behavior change in the consolidation corridor:

1. Run the inventory and keep the command output with the team handoff.
2. Check unmatched local consumer segments.
3. Check whether new `/api` first segments appeared.
4. Record whether each new segment is canonical, compatibility, or external/service-only.
5. Make route behavior changes only in later phases with explicit ownership and telemetry.
