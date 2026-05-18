# System-Wide Cache Completion Implementation Plan

Date: 2026-05-18
Status: Draft for implementation
Owner: NSCIM platform/cache performance workstream

## Purpose

Finish the cache work so NSCIM has a coherent system-wide cache platform, not only predictive preload for selected assignment and container workflows.

The current system has useful cache pieces:

- Redis or distributed-memory fallback through `ICacheService` and `RedisCacheService`.
- ASP.NET response caching and compression.
- Domain/local `IMemoryCache` usage in controllers, services, image processing, permissions, settings, and WebApp view preloaders.
- Predictive preload for Analyst and Audit assignment/container context.

The missing part is the durable system-wide cache layer that standardizes:

- L1 in-process cache plus L2 distributed cache.
- Prefix/tag invalidation that works across app instances when Redis is enabled.
- Stampede protection for hot keys.
- Central metrics for hit, miss, stale, fallback, invalidation, and warmup results.
- A consistent migration path for hot API/data flows.
- Operator controls and rollout gates.

## Current Baseline

Confirmed in current `main` on 2026-05-18:

- `src/NickScanCentralImagingPortal.Core/Interfaces/ICacheService.cs` exists.
- `src/NickScanCentralImagingPortal.Services/Caching/RedisCacheService.cs` is the active implementation registered in `Program.cs`.
- `RedisCacheService` wraps `IDistributedCache`.
- When Redis is disabled, `Program.cs` registers `AddDistributedMemoryCache()` and still uses `RedisCacheService`.
- `RedisCacheService.RemoveByPrefixAsync` uses an in-process `_knownKeys` tracker.
- `RedisCacheService.GetOrSetAsync` does cache-aside but has no per-key lock.
- `PredictivePreloadService`, `PredictivePreloadBackgroundService`, `PredictivePreloadController`, options, keys, DTOs, and tests exist.
- `PredictivePreload` is enabled in appsettings with background execution.
- Full image bytes are intentionally not cached by predictive preload.
- No `SystemCacheService`, `SystemCacheWarmupService`, or `SystemCacheOptions` implementation exists in the current tree.

## Target State

Build a first-class cache platform with the following shape:

- `ICacheService` remains the public abstraction for existing callers.
- New `SystemCacheService` becomes the default `ICacheService` implementation.
- L1 uses `IMemoryCache` with size limits and short TTLs.
- L2 uses `IDistributedCache` with Redis when enabled and distributed-memory fallback when disabled.
- Cache entries can be grouped by prefix and optional tags.
- Prefix and tag invalidation are durable when Redis is available.
- Cache-aside calls use per-key stampede protection.
- Predictive preload writes through the same cache service.
- Hot Web/API flows migrate gradually to the unified cache.
- Operator endpoints expose status, metrics, and safe invalidation controls.
- Deployment can be rolled out without changing functional behavior first.

## Non-Goals

- Do not cache full scanner image bytes in the first production pass.
- Do not remove existing local `IMemoryCache` callers in one large refactor.
- Do not introduce Redis as a hard dependency.
- Do not cache user-sensitive authorization decisions longer than their existing short TTLs.
- Do not change assignment, completeness, ICUMS, or scan identity business rules as part of cache plumbing.

## Architecture

### Components

1. `SystemCacheOptions`
   - Configuration section: `SystemCache`.
   - Controls L1/L2 enablement, TTL defaults, prefix index TTL, max entry size, metrics enablement, warmup enablement, and failure behavior.

2. `SystemCacheService`
   - Implements `ICacheService`.
   - Uses `IMemoryCache` as optional L1.
   - Uses `IDistributedCache` as L2.
   - Provides cache-aside `GetOrSetAsync`.
   - Adds per-key `SemaphoreSlim` stampede protection.
   - Tracks keys by prefix and tag.
   - Fails open: cache failures log and fall back to source data.

3. `SystemCacheKeyRegistry`
   - Centralizes key naming.
   - Defines key families and prefixes:
     - `ready-groups:*`
     - `preload:*`
     - `container:*`
     - `scanner:*`
     - `icums:*`
     - `boe:*`
     - `image-metadata:*`
     - `permissions:*`
     - `settings:*`
     - `public-stats:*`

4. `SystemCacheMetrics`
   - In-memory counters for hit, miss, set, remove, prefix remove, tag remove, factory success/failure, L1/L2 result.
   - Optional log summary every N minutes.
   - Exposed through admin diagnostics.

5. `SystemCacheWarmupService`
   - Hosted service.
   - Runs after startup delay.
   - Calls targeted warmup providers.
   - Supports disabled-by-default or safe limited warmup in production.

6. Warmup providers
   - `ReadyGroupsWarmupProvider`
   - `PredictiveAssignmentWarmupProvider`
   - `ContainerContextWarmupProvider`
   - `PermissionsWarmupProvider`
   - `SettingsWarmupProvider`
   - Later: `DashboardStatsWarmupProvider`

7. Operator API
   - Extend or add `/api/cache/system/*`.
   - Preserve `/api/cache/predictive/*` for predictive-specific operations.

## Blast Radius

### API

- `src/NickScanCentralImagingPortal.API/Program.cs`
- `src/NickScanCentralImagingPortal.API/appsettings.json`
- `src/NickScanCentralImagingPortal.API/appsettings.Production.template.json`
- `src/NickScanCentralImagingPortal.API/Controllers/PredictivePreloadController.cs`
- New `SystemCacheController` or extended cache admin controller
- Health and monitoring endpoints, if metrics are surfaced

### Core

- `src/NickScanCentralImagingPortal.Core/Interfaces/ICacheService.cs`
- New cache DTOs/options/interfaces under Core if needed

### Services

- `src/NickScanCentralImagingPortal.Services/Caching/RedisCacheService.cs`
- New `SystemCacheService`
- New `SystemCacheWarmupService`
- New metrics/registry/provider classes
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadService.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadBackgroundService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`
- `src/NickScanCentralImagingPortal.Services/Settings/SettingsProvider.cs`
- `src/NickScanCentralImagingPortal.Services/Permissions/PermissionService.cs`
- `src/NickScanCentralImagingPortal.Services/Permissions/RoleService.cs`
- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumApiService.cs`
- `src/NickScanCentralImagingPortal.Services/Monitoring/PerformanceMonitoringService.cs`

### Infrastructure

- `src/NickScanCentralImagingPortal.Infrastructure/Caching/CacheExtensions.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Repositories/ContainerDataRepository.cs`
- Optional repository-level migrations only if persistent cache metadata is ever needed. First pass should avoid DB schema.

### WebApp

- `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`
- `src/NickScanWebApp.New/Services/ContainerViewPreloader.cs`
- `src/NickScanWebApp.New/Services/AuditReviewViewPreloader.cs`
- `src/NickScanWebApp.New/Services/ImageAnalysisViewPreloader.cs`
- `src/NickScanWebApp.New/Services/DataCacheService.cs`
- Optional cache diagnostics page or monitoring panel later.

### Tests

- `tests/NickScanCentralImagingPortal.Integration.Tests/Caching/*`
- New core unit tests for key registry/options
- New integration tests for L1/L2 behavior, prefix/tag invalidation, stampede protection, warmup providers, and cache fallback

## Implementation Phases

## Phase 0: Sync, Inventory, and Safety Baseline

Goal: Start from current `main` and prove the existing cache/preload behavior before changing it.

- [ ] Confirm worktree is clean.
- [ ] Pull latest `main`.
- [ ] Run current cache tests:
  - [ ] `dotnet test tests/NickScanCentralImagingPortal.Integration.Tests/NickScanCentralImagingPortal.Integration.Tests.csproj --filter "FullyQualifiedName~Caching"`
- [ ] Run focused API build:
  - [ ] `dotnet build src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj -c Release`
- [ ] Record current predictive preload config.
- [ ] Record live baseline from logs or diagnostics:
  - [ ] preload candidate count
  - [ ] success count
  - [ ] duration
  - [ ] any recurrent preload failures
- [ ] Update this plan with baseline metrics.

Exit criteria:

- Existing tests/build pass.
- Known cache behavior is documented.
- No production deployment occurs in this phase.

## Phase 1: Cache Options, Key Registry, and Contracts

Goal: Define the cache platform contract without changing runtime behavior.

- [x] Add `SystemCacheOptions`.
- [x] Add config section to `appsettings.json`.
- [x] Add config section to `appsettings.Production.template.json`.
- [x] Add `SystemCacheKeyRegistry`.
- [x] Add key-family constants and helpers.
- [ ] Add cache entry metadata DTOs if needed.
- [x] Add tests for option defaults.
- [x] Add tests for key naming stability.
- [x] Keep `RedisCacheService` as the active registered implementation.

Exit criteria:

- No behavior change.
- API builds.
- New tests pass.

## Phase 2: Build `SystemCacheService`

Goal: Implement unified L1/L2 cache but keep rollout behind config.

- [x] Create `SystemCacheService`.
- [x] Implement `GetAsync<T>`.
- [x] Implement `SetAsync<T>`.
- [x] Implement `RemoveAsync`.
- [x] Implement `ExistsAsync`.
- [x] Implement `GetOrSetAsync`.
- [x] Add L1 `IMemoryCache` support.
- [x] Add L2 `IDistributedCache` support.
- [x] Add configurable L1/L2 TTL behavior.
- [ ] Add max serialized payload guard.
- [x] Add null-value handling policy.
- [x] Add serialization options consistent with existing cache behavior.
- [x] Add graceful fallback on cache errors.
- [ ] Add tests for L1 hit, L2 hit, miss, set, remove, exists, serialization failure, and fallback.

Exit criteria:

- `SystemCacheService` passes tests.
- Still not the production default unless `SystemCache:UseSystemCacheService=true`.

## Phase 3: Stampede Protection

Goal: Stop repeated LAN/database calls when many users request the same hot key.

- [x] Add per-key async locks in `SystemCacheService`.
- [x] Ensure `GetOrSetAsync` rechecks cache after acquiring lock.
- [x] Ensure lock dictionary is pruned to avoid unbounded growth.
- [x] Make lock timeout configurable.
- [x] Add tests proving parallel requests call the factory once.
- [x] Add tests proving factory exceptions do not poison the lock.
- [x] Add metrics counters for stampede prevented and factory failures.

Exit criteria:

- Parallel cache miss test proves one source fetch.
- Cache failures still fall back safely.

## Phase 4: Durable Prefix and Tag Invalidation

Goal: Make invalidation reliable across app instances when Redis is enabled, while still working with memory fallback.

- [ ] Add key indexing when `SetAsync` is called.
- [ ] Track keys by prefix.
- [ ] Track keys by tag.
- [ ] Use distributed index keys for L2 when Redis/distributed cache supports it.
- [ ] Keep in-process index as fallback.
- [ ] Add index TTL longer than entry TTL.
- [ ] Implement `RemoveByPrefixAsync` using index.
- [ ] Add `RemoveByTagAsync` if the interface is extended.
- [ ] Add compatibility path so existing callers of `RemoveByPrefixAsync` keep working.
- [ ] Add tests for prefix invalidation.
- [ ] Add tests for tag invalidation.
- [ ] Add tests for stale index entries.
- [ ] Add tests for missing index fallback.

Exit criteria:

- Prefix invalidation no longer depends only on a single-process `_knownKeys` dictionary.
- Existing `ReadyGroupsCacheService` invalidation still works.

## Phase 5: Metrics and Diagnostics

Goal: Make cache behavior visible before broad migration.

- [x] Add `SystemCacheMetrics`.
- [ ] Track:
  - [x] L1 hit
  - [ ] L1 miss
  - [x] L2 hit
  - [ ] L2 miss
  - [x] set
  - [x] remove
  - [x] prefix remove
  - [ ] tag remove
  - [ ] factory success
  - [x] factory failure
  - [ ] fallback to source
  - [ ] payload too large
  - [x] stampede prevented
- [ ] Add admin endpoint `GET /api/cache/system/status`.
- [ ] Add admin endpoint `GET /api/cache/system/metrics`.
- [ ] Add admin endpoint `POST /api/cache/system/invalidate/prefix`.
- [ ] Add admin endpoint `POST /api/cache/system/invalidate/tag`.
- [ ] Add optional metric log summary.
- [ ] Add endpoint tests.

Exit criteria:

- Operators can see whether cache is helping or just silently bypassing.

## Phase 6: Registration and Feature Flag Rollout

Goal: Safely switch `ICacheService` to `SystemCacheService`.

- [x] Add `SystemCache:UseSystemCacheService`.
- [x] Register both old and new implementations internally if needed.
- [x] Default to current behavior until tests pass.
- [ ] Switch development config to new implementation.
- [x] Keep production template explicit and controllable.
- [ ] Add startup log showing:
  - [ ] L1 enabled
  - [ ] L2 provider
  - [ ] Redis enabled/disabled
  - [ ] system cache enabled
  - [ ] warmup enabled
- [ ] Run existing predictive preload tests against new implementation.
- [ ] Run `RedisCacheServiceTests` or replace with system cache tests.

Exit criteria:

- Predictive preload works through `SystemCacheService`.
- Production can be toggled without code change.

## Phase 7: Warmup Framework

Goal: Replace ad hoc preload behavior with a clean warmup framework while preserving predictive preload.

- [ ] Add `ISystemCacheWarmupProvider`.
- [ ] Add `SystemCacheWarmupService`.
- [ ] Add warmup state DTO.
- [ ] Add admin endpoint to view warmup state.
- [ ] Add admin endpoint to run warmup once.
- [ ] Add provider for predictive role assignment candidates.
- [ ] Add provider for ready groups.
- [ ] Add provider for settings.
- [ ] Add provider for permission catalog.
- [ ] Add provider for container contexts using existing predictive preload service.
- [ ] Add max concurrency controls.
- [ ] Add CPU/DB-latency guard hooks if already available.
- [ ] Add startup delay.
- [ ] Add interval jitter to avoid synchronized warmup spikes.

Exit criteria:

- Warmup is coordinated and observable.
- Existing predictive preload remains functional.

## Phase 8: Hot Path Migration, Backend First

Goal: Move the biggest repeated data fetches through the unified cache in controlled slices.

Priority order:

1. Ready groups and role assignment lists.
2. Predictive assignment/container context.
3. Container summary and cargo group summary.
4. Scanner first-page data.
5. ICUMS first-page data.
6. BOE summary and related document IDs.
7. Image metadata.
8. Public stats/dashboard summaries.
9. Permission and role catalogs.
10. Settings reads.

Checklist:

- [ ] For each path, define canonical key.
- [ ] For each path, define TTL.
- [ ] For each path, define invalidation trigger.
- [ ] For each path, add cache-aside usage.
- [ ] For each path, add focused tests.
- [ ] For each path, add metric labels.
- [ ] Avoid migrating image bytes in this phase.

Exit criteria:

- High-value API reads use the unified cache.
- Invalidation is explicit for each migrated family.

## Phase 9: Frontend/WebApp Cache Integration

Goal: Reduce repeated LAN/API calls from the WebApp without breaking live screens.

- [ ] Audit `ContainerDetailsService` local cache keys.
- [ ] Fix `ClearContainerCache` so scanner and ICUMS paginated keys are cleared.
- [ ] Keep cache-first predictive context calls.
- [ ] Ensure direct-load fallback remains for all cache misses.
- [ ] Add partial-page hydration only after full-tab behavior is safe.
- [ ] Add client-side cache hit/fallback logging at debug level.
- [ ] Ensure image analysis, audit review, fullscreen viewer, and container details still use live image routes.
- [ ] Do not cache signed image URLs past their expiry.

Exit criteria:

- WebApp gains cache benefit without stale tab data or broken signed-image links.

## Phase 10: Invalidation Matrix

Goal: Prevent stale data after decisions, submissions, rematches, queue transitions, and ICUMS ingestion.

Create and implement invalidation rules for:

- [ ] New scan received.
- [ ] Container completeness row created or updated.
- [ ] BOE/ICUMS data ingested.
- [ ] Manual BOE match/rematch/unmatch.
- [ ] Analysis assignment created.
- [ ] Assignment released.
- [ ] Analyst decision saved.
- [ ] Audit decision saved.
- [ ] ICUMS submission queued.
- [ ] ICUMS submission acknowledged.
- [ ] Record completeness child state changes.
- [ ] CMR parent rollup changes.
- [ ] Split image choice selected.
- [ ] Split image crop regenerated.
- [ ] Settings changed.
- [ ] Permission/role changed.

Exit criteria:

- Every cache key family has an owner and invalidation trigger.
- No broad `clear everything` behavior is needed during normal workflow.

## Phase 11: Test Matrix

Required tests before staging:

- [ ] `SystemCacheService` unit/integration tests.
- [ ] `SystemCacheWarmupService` tests.
- [ ] Prefix/tag invalidation tests.
- [ ] Stampede protection tests.
- [ ] Predictive preload tests.
- [ ] Ready groups cache tests.
- [ ] Container details cache invalidation tests.
- [ ] WebApp service fallback tests where possible.
- [ ] API release build.
- [ ] WebApp release build.

Suggested commands:

```powershell
dotnet test tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj --filter "FullyQualifiedName~Caching"
dotnet test tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj
dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj -c Release
dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj -c Release
```

Exit criteria:

- All focused cache tests pass.
- Release builds pass.
- Any unrelated failures are documented and not hidden.

## Phase 12: Controlled Staging Verification

Goal: Prove the system cache under production-like startup without affecting production.

- [ ] Publish API staging package.
- [ ] Publish WebApp staging package if WebApp changed.
- [ ] Start staging API with:
  - [ ] `NSCIM_SKIP_SINGLE_INSTANCE=1`
  - [ ] `StagingVerification:DisableBackgroundServices=true`
  - [ ] `SystemCache:UseSystemCacheService=true`
  - [ ] `SystemCache:Warmup:Enabled=false`
  - [ ] `PredictivePreload:BackgroundEnabled=false`
- [ ] Verify health.
- [ ] Verify cache status endpoint.
- [ ] Run manual predictive preload once.
- [ ] Run manual system warmup once with small caps.
- [ ] Validate cache metrics show hits/misses/sets.
- [ ] Invalidate one container prefix and confirm it clears.
- [ ] Confirm direct-load fallback still works.

Exit criteria:

- Staging proves cache behavior without background service noise.

## Phase 13: Production Rollout

Goal: Deploy safely in layers.

Rollout order:

1. Deploy with `SystemCacheService` present but disabled.
2. Enable `SystemCacheService` for predictive preload only.
3. Enable metrics and diagnostics.
4. Enable warmup with low caps.
5. Migrate hot paths by domain.
6. Increase caps only after observing metrics.

Checklist:

- [ ] Create reviewed file list.
- [ ] Preserve production `appsettings*.json`.
- [ ] Back up live publish folders.
- [ ] Deploy API first.
- [ ] Deploy WebApp only if changed.
- [ ] Verify services running.
- [ ] Verify health endpoints.
- [ ] Verify cache status endpoint.
- [ ] Verify predictive preload pass.
- [ ] Check logs for fresh cache exceptions.
- [ ] Observe for at least one preload interval.
- [ ] Record live metrics snapshot.

Exit criteria:

- Production health remains green.
- Cache metrics show successful operation.
- No fresh assignment, image-analysis, ICUMS, or completeness regressions.

## Phase 14: Post-Launch Hardening

- [ ] Add dashboard card for cache health.
- [ ] Add alert threshold for repeated cache fallback.
- [ ] Add alert threshold for warmup failure rate.
- [ ] Add endpoint latency comparison before/after cache migration.
- [ ] Add periodic stale-index cleanup.
- [ ] Add docs for key naming and invalidation ownership.
- [ ] Review whether Redis should be enabled in production.
- [ ] Decide whether any image metadata caches need longer TTLs.
- [ ] Reassess full-image byte caching only after measuring LAN/storage cost and signed URL expiry behavior.

## Implementation Order Recommendation

Recommended first implementation batch:

1. Phase 1: options and key registry.
2. Phase 2: `SystemCacheService`.
3. Phase 3: stampede protection.
4. Phase 4: durable prefix/tag invalidation.
5. Phase 5: metrics and diagnostics.
6. Phase 6: feature-flagged registration.

Recommended second implementation batch:

1. Phase 7: warmup framework.
2. Phase 8: backend hot path migration for predictive preload and ready groups.
3. Phase 10: invalidation matrix.
4. Phase 11: expanded tests.

Recommended third implementation batch:

1. Phase 9: WebApp cleanup and cache-first polish.
2. Phase 12: staging.
3. Phase 13: production rollout.
4. Phase 14: hardening.

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Stale ICUMS or scanner tab data | User sees old operational data | Invalidation matrix before broad migration |
| Cache stampede remains | LAN/database load still spikes | Per-key `GetOrSetAsync` lock and test |
| Redis unavailable | Cache failures affect app | Distributed-memory fallback and fail-open behavior |
| Prefix invalidation misses keys | Old ready groups or container data persists | Durable prefix/tag index plus stale-index tests |
| Full image caching bloats memory/network | Poor performance or expiry bugs | Keep full image bytes out of first rollout |
| Security-sensitive data cached too long | Authorization/session risk | Short TTLs and no broad caching of auth decisions |
| Warmup overloads DB at startup | Slow service restart | Startup delay, caps, concurrency gate, CPU/DB guard |
| Hidden cache failures | Operators think cache is working when it is not | Metrics, diagnostics, and log summaries |

## Definition of Done

The system-wide cache work is complete when:

- `SystemCacheService` is the registered `ICacheService` implementation.
- L1 and L2 behavior is tested.
- Prefix/tag invalidation is tested and works across the configured cache backend.
- `GetOrSetAsync` has tested stampede protection.
- Predictive preload uses the unified cache.
- Warmup framework is implemented and operator-visible.
- High-value backend hot paths have migrated through the cache service.
- WebApp cache-first paths still have reliable direct-load fallback.
- Metrics are visible through admin diagnostics.
- Controlled staging verification passes.
- Production deployment is completed with live health and log verification.

## Immediate Next Step

Start Phase 1 and Phase 2 together in a small branch:

- Add `SystemCacheOptions`.
- Add `SystemCacheKeyRegistry`.
- Add `SystemCacheService` behind `SystemCache:UseSystemCacheService`.
- Add tests for L1/L2 get, set, remove, exists, cache-aside, and fallback.
- Keep production behavior unchanged until the feature flag is explicitly enabled.
